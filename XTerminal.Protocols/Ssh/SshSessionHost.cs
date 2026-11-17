using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Security;
using XTerminal.Core.Services;

namespace XTerminal.Protocols.Ssh;

public sealed class SshSessionHost : ISessionHost
{
    private readonly ISecretStore _secrets;
    private readonly ISshSessionLibrary _library;
    private readonly SshHostKeyPolicy _hostKeys;
    private readonly SshConnectionManager? _connectionManager;

    private SshSessionRecord? _record;
    private SshClient? _client;
    private SshClient? _jumpClient;
    private SshConnectionLease? _shellLease;
    private bool _ownsClientExclusively;
    private ShellStream? _shell;
    private CancellationTokenSource? _connectCts;
    private CancellationTokenSource? _readCts;
    private CancellationTokenSource? _latencyCts;
    private Task? _readTask;
    private readonly List<ForwardedPort> _forwards = new();
    private short _cols = 120;
    private short _rows = 40;
    private int _reconnectAttempt;
    private int _reconnectGate;

    public SshSessionHost(
        ISecretStore secrets,
        ISshSessionLibrary library,
        SshHostKeyPolicy hostKeys,
        SshConnectionManager? connectionManager = null)
    {
        _secrets = secrets;
        _library = library;
        _hostKeys = hostKeys;
        _connectionManager = connectionManager;
    }

    public SessionKind Kind => SessionKind.Ssh;
    public string Title => _record?.Name ?? "SSH";
    public bool IsConnected => _client?.IsConnected == true && _shell is not null;
    public bool IsImplemented => true;
    public string? StatusMessage { get; private set; }
    public ConnectionState State { get; private set; } = ConnectionState.Idle;
    public string? Hostname => _record?.Host;
    public string? Username => _record?.Username;
    public int? LatencyMs { get; private set; }

    public event Action<byte[]>? OutputReceived;
    public event Action? Closed;
    public event Action<ConnectionState, string?>? StateChanged;

    public async Task ConnectAsync(SessionDefinition definition, CancellationToken cancellationToken = default)
    {
        if (definition.SshSessionId is null)
        {
            throw new InvalidOperationException("SSH session id is missing.");
        }

        var library = await _library.LoadAsync(cancellationToken);
        _record = library.Sessions.FirstOrDefault(s => s.Id == definition.SshSessionId.Value);
        if (_record is null)
        {
            // Ephemeral Quick Connect / unsaved session — synthesize from definition.
            if (string.IsNullOrWhiteSpace(definition.Host) || string.IsNullOrWhiteSpace(definition.Username))
                throw new InvalidOperationException("SSH session was not found in the library.");

            _record = new SshSessionRecord
            {
                Id = definition.SshSessionId.Value,
                Name = definition.Name,
                Host = definition.Host!,
                Port = definition.Port is null or <= 0 ? 22 : definition.Port.Value,
                Username = definition.Username!,
                AuthMethod = SshAuthMethod.Password,
                StrictHostKeyChecking = true,
                StartupDirectory = definition.WorkingDirectory
            };
        }

        await ConnectCoreAsync(cancellationToken);
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_record is null)
            return;

        if (Interlocked.CompareExchange(ref _reconnectGate, 1, 0) != 0)
            return;

        try
        {
            await DisconnectCoreAsync();
            SetState(ConnectionState.Reconnecting, "Reconnecting…");
            await ConnectCoreAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectGate, 0);
        }
    }

    public void CancelConnect()
    {
        try { _connectCts?.Cancel(); } catch { /* ignore */ }
        SetState(ConnectionState.Cancelled, "Cancelled");
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_shell is null || !IsConnected)
        {
            return;
        }

        _shell.Write(data);
        _shell.Flush();
    }

    public void Resize(short columns, short rows)
    {
        _cols = columns;
        _rows = rows;
        try
        {
            _shell?.ChangeWindowSize((uint)columns, (uint)rows, 0, 0);
        }
        catch
        {
            // ignore resize races during reconnect
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancelConnect();
        await DisconnectCoreAsync();
    }

    private async Task ConnectCoreAsync(CancellationToken externalToken)
    {
        _connectCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var token = _connectCts.Token;
        SetState(ConnectionState.Connecting, $"Connecting to {_record!.Host}…");

        try
        {
            WriteSystem($"\u001b[90mConnecting to {_record.Username}@{_record.Host}:{_record.Port}…\u001b[0m\r\n");

            SshClient client;
            var canShare = _connectionManager is not null &&
                           string.IsNullOrWhiteSpace(_record.ProxyJumpHost);
            if (canShare &&
                _connectionManager!.TryAcquireShell(_record.Id) is { } existingLease)
            {
                _shellLease = existingLease;
                client = existingLease.Client;
                _client = client;
                _ownsClientExclusively = false;
            }
            else
            {
                client = await CreateClientAsync(_record, token);
                token.ThrowIfCancellationRequested();
                await Task.Run(() => client.Connect(), token);
                if (canShare)
                {
                    _shellLease = _connectionManager!.RegisterShell(_record.Id, client, _record);
                    client = _shellLease.Client;
                    _client = client;
                    _ownsClientExclusively = false;
                }
                else
                {
                    _client = client;
                    _ownsClientExclusively = true;
                }
            }

            StartPortForwards(client, _record);
            _shell = client.CreateShellStream(
                _record.TerminalType,
                (uint)_cols,
                (uint)_rows,
                0, 0,
                1024);

            SendStartupCommands(_shell, _record);

            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token));

            _record.LastConnectedUtc = DateTimeOffset.UtcNow;
            var doc = await _library.LoadAsync(token);
            var existing = doc.Sessions.FirstOrDefault(s => s.Id == _record.Id);
            if (existing is not null)
            {
                existing.LastConnectedUtc = _record.LastConnectedUtc;
                await _library.SaveAsync(doc, token);
            }

            _reconnectAttempt = 0;
            SetState(ConnectionState.Connected, "Connected");
            WriteSystem($"\u001b[32mConnected.\u001b[0m\r\n");
            StartLatencyProbe();
        }
        catch (OperationCanceledException)
        {
            await DisconnectCoreAsync();
            SetState(ConnectionState.Cancelled, "Connection cancelled");
            WriteSystem("\u001b[33mConnection cancelled.\u001b[0m\r\n");
        }
        catch (Exception ex)
        {
            await DisconnectCoreAsync();
            var safe = Sanitize(ex.Message);
            SetState(ConnectionState.Failed, safe);
            WriteSystem($"\u001b[31mConnection failed:\u001b[0m {safe}\r\n");

            if (_record?.AutoReconnect == true && _reconnectAttempt < _record.MaxReconnectAttempts)
            {
                _reconnectAttempt++;
                SetState(ConnectionState.Reconnecting, $"Reconnect attempt {_reconnectAttempt}…");
                await Task.Delay(1500, CancellationToken.None);
                await ConnectCoreAsync(CancellationToken.None);
            }
        }
    }

    private async Task<SshClient> CreateClientAsync(SshSessionRecord session, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(session.ProxyJumpHost))
        {
            return await CreateClientViaJumpAsync(session, token);
        }

        var methods = await SshAuthFactory.CreateAsync(session, _secrets, forJumpHost: false, token);
        var info = new ConnectionInfo(session.Host, session.Port, session.Username, methods)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(5, session.ConnectionTimeoutSeconds)),
            Encoding = Encoding.UTF8
        };

        ApplyCompression(info, session.Compression);
        var client = new SshClient(info);
        ConfigureClient(client, session);
        return client;
    }

    private async Task<SshClient> CreateClientViaJumpAsync(SshSessionRecord session, CancellationToken token)
    {
        var jumpUser = session.ProxyJumpUsername ?? session.Username;
        var jumpMethods = await SshAuthFactory.CreateAsync(session, _secrets, forJumpHost: true, token);
        var jumpInfo = new ConnectionInfo(
            session.ProxyJumpHost!,
            session.ProxyJumpPort <= 0 ? 22 : session.ProxyJumpPort,
            jumpUser,
            jumpMethods)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(5, session.ConnectionTimeoutSeconds))
        };

        ApplyCompression(jumpInfo, session.Compression);
        _jumpClient = new SshClient(jumpInfo);
        ConfigureClient(_jumpClient, session, isJump: true);
        await Task.Run(() => _jumpClient.Connect(), token);

        var local = new ForwardedPortLocal("127.0.0.1", 0, session.Host, (uint)session.Port);
        _jumpClient.AddForwardedPort(local);
        local.Start();
        _forwards.Add(local);

        var targetMethods = await SshAuthFactory.CreateAsync(session, _secrets, forJumpHost: false, token);
        var targetInfo = new ConnectionInfo("127.0.0.1", (int)local.BoundPort, session.Username, targetMethods)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(5, session.ConnectionTimeoutSeconds))
        };
        ApplyCompression(targetInfo, session.Compression);
        var target = new SshClient(targetInfo);
        ConfigureClient(target, session);
        return target;
    }

    private void ConfigureClient(SshClient client, SshSessionRecord session, bool isJump = false)
    {
        client.KeepAliveInterval = TimeSpan.FromSeconds(Math.Max(0, session.KeepAliveIntervalSeconds));
        var host = isJump ? session.ProxyJumpHost! : session.Host;
        var port = isJump
            ? (session.ProxyJumpPort <= 0 ? 22 : session.ProxyJumpPort)
            : session.Port;
        _hostKeys.Attach(client, host, port, session.StrictHostKeyChecking);

        client.ErrorOccurred += (_, args) =>
        {
            var msg = Sanitize(args.Exception.Message);
            SetState(ConnectionState.Failed, msg);
        };
    }

    private void StartPortForwards(SshClient client, SshSessionRecord session)
    {
        foreach (var fwd in session.PortForwards.Where(f => f.Enabled))
        {
            try
            {
                ForwardedPort port = fwd.Type switch
                {
                    SshForwardType.Remote => new ForwardedPortRemote(
                        fwd.BindAddress,
                        (uint)fwd.ListenPort,
                        fwd.DestinationHost ?? "127.0.0.1",
                        (uint)fwd.DestinationPort),
                    SshForwardType.Dynamic => new ForwardedPortDynamic(fwd.BindAddress, (uint)fwd.ListenPort),
                    _ => new ForwardedPortLocal(
                        fwd.BindAddress,
                        (uint)fwd.ListenPort,
                        fwd.DestinationHost ?? "127.0.0.1",
                        (uint)fwd.DestinationPort)
                };

                client.AddForwardedPort(port);
                port.Start();
                _forwards.Add(port);
                WriteSystem($"\u001b[90mPort forward started: {fwd.Type} {fwd.ListenPort}\u001b[0m\r\n");
            }
            catch (Exception ex)
            {
                WriteSystem($"\u001b[33mPort forward failed ({fwd.Type} {fwd.ListenPort}): {Sanitize(ex.Message)}\u001b[0m\r\n");
            }
        }
    }

    private static void SendStartupCommands(ShellStream shell, SshSessionRecord session)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in session.EnvironmentVariables)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            // Avoid echoing secrets: only send non-empty keys; values may be non-secret env config.
            sb.Append("export ")
              .Append(key.Replace("'", "'\\''"))
              .Append("='")
              .Append((value ?? string.Empty).Replace("'", "'\\''"))
              .Append("'; ");
        }

        if (!string.IsNullOrWhiteSpace(session.StartupDirectory))
        {
            sb.Append("cd '")
              .Append(session.StartupDirectory.Replace("'", "'\\''"))
              .Append("'; ");
        }

        if (!string.IsNullOrWhiteSpace(session.StartupCommand))
        {
            sb.Append(session.StartupCommand);
            if (!session.StartupCommand.EndsWith('\n'))
            {
                sb.Append('\n');
            }
        }
        else if (sb.Length > 0)
        {
            sb.Append('\n');
        }

        if (sb.Length > 0)
        {
            shell.Write(sb.ToString());
            shell.Flush();
        }
    }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        var buffer = new byte[8192];
        try
        {
            while (!token.IsCancellationRequested && _shell is not null)
            {
                var read = await _shell.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                if (read <= 0)
                {
                    if (_client?.IsConnected != true)
                    {
                        break;
                    }

                    await Task.Delay(20, token);
                    continue;
                }

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                OutputReceived?.Invoke(chunk);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (SshConnectionException) { }
        finally
        {
            if (State is ConnectionState.Connected or ConnectionState.Reconnecting)
            {
                SetState(ConnectionState.Disconnected, "Disconnected");
                Closed?.Invoke();

                if (_record?.AutoReconnect == true && _reconnectAttempt < _record.MaxReconnectAttempts)
                {
                    _reconnectAttempt++;
                    _ = ReconnectAsync(CancellationToken.None);
                }
            }
        }
    }

    private void StartLatencyProbe()
    {
        try { _latencyCts?.Cancel(); } catch { /* ignore */ }
        _latencyCts = new CancellationTokenSource();
        var token = _latencyCts.Token;
        _ = Task.Run(async () =>
        {
            // Slow SSH keepalive RTT sample — not ICMP ping.
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
                    var client = _client;
                    if (client is null || !client.IsConnected) continue;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    // SSH.NET KeepAliveInterval does not expose RTT; SendKeepAlive is obsolete but
                    // remains the least invasive way to sample transport round-trip without a remote command.
#pragma warning disable CS0618
                    await Task.Run(() => client.SendKeepAlive(), token).ConfigureAwait(false);
#pragma warning restore CS0618
                    sw.Stop();
                    LatencyMs = (int)Math.Clamp(sw.ElapsedMilliseconds, 0, 60_000);
                    if (_record is not null && _connectionManager?.TryGet(_record.Id) is { } shared)
                    {
                        shared.LatencyMs = LatencyMs;
                        shared.LastRoundTripUtc = DateTimeOffset.UtcNow;
                    }

                    StateChanged?.Invoke(State, StatusMessage);
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    // keep last sample
                }
            }
        }, token);
    }

    private async Task DisconnectCoreAsync()
    {
        try { _latencyCts?.Cancel(); } catch { /* ignore */ }
        LatencyMs = null;

        try { _readCts?.Cancel(); } catch { /* ignore */ }
        if (_readTask is not null)
        {
            try { await _readTask.ConfigureAwait(false); } catch { /* ignore */ }
        }

        foreach (var forward in _forwards)
        {
            try
            {
                if (forward.IsStarted)
                    forward.Stop();
            }
            catch { /* ignore */ }

            try { forward.Dispose(); } catch { /* ignore */ }
        }

        _forwards.Clear();

        try { _shell?.Dispose(); } catch { /* ignore */ }
        _shell = null;

        // Release shared lease first — manager disposes the client only when no leases remain.
        var lease = _shellLease;
        _shellLease = null;
        lease?.Dispose();

        if (_ownsClientExclusively)
        {
            try { if (_client?.IsConnected == true) _client.Disconnect(); } catch { /* ignore */ }
            try { _client?.Dispose(); } catch { /* ignore */ }

            try { if (_jumpClient?.IsConnected == true) _jumpClient.Disconnect(); } catch { /* ignore */ }
            try { _jumpClient?.Dispose(); } catch { /* ignore */ }
        }

        _client = null;
        _jumpClient = null;
        _ownsClientExclusively = false;
    }

    private void WriteSystem(string text) => OutputReceived?.Invoke(Encoding.UTF8.GetBytes(text));

    private void SetState(ConnectionState state, string? message)
    {
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke(state, message);
    }

    private static void ApplyCompression(ConnectionInfo info, bool enabled)
    {
        try
        {
            if (enabled && info.CompressionAlgorithms.ContainsKey("zlib@openssh.com"))
            {
                // Prefer zlib compression over the default "none".
                info.CompressionAlgorithms.SetPosition("zlib@openssh.com", 0);
            }
        }
        catch
        {
            // ignore if API surface differs
        }
    }

    private static string Sanitize(string message)
    {
        // Never allow accidental secret material patterns into UI/logs.
        if (string.IsNullOrEmpty(message))
        {
            return "Unknown error";
        }

        return message
            .Replace("password", "********", StringComparison.OrdinalIgnoreCase)
            .Replace("passphrase", "********", StringComparison.OrdinalIgnoreCase);
    }
}
