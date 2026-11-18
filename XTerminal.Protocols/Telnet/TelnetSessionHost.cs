using System.Net.Sockets;
using System.Text;
using XTerminal.Core.Models;
using XTerminal.Core.Services;

namespace XTerminal.Protocols.Telnet;

public sealed class TelnetSessionHost : ISessionHost
{
    private readonly TelnetNegotiationEngine _engine = new();
    private SessionDefinition? _definition;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readCts;
    private CancellationTokenSource? _connectCts;
    private Task? _readTask;
    private int _disposed;
    private int _intentionalClose;
    private short _cols = 80;
    private short _rows = 24;

    public SessionKind Kind => SessionKind.Telnet;
    public string Title => _definition?.Name ?? "Telnet";
    public bool IsConnected => _client?.Connected == true && _stream is not null;
    public bool IsImplemented => true;
    public string? StatusMessage { get; private set; }
    public ConnectionState State { get; private set; } = ConnectionState.Idle;
    public TelnetNegotiationEngine Engine => _engine;

    public event Action<byte[]>? OutputReceived;
    public event Action? Closed;
    public event Action<ConnectionState, string?>? StateChanged;

    public async Task ConnectAsync(SessionDefinition definition, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (string.IsNullOrWhiteSpace(definition.Host))
            throw new InvalidOperationException("Telnet host is required.");

        var port = definition.Port is > 0 and <= 65535 ? definition.Port.Value : 23;
        var timeoutSec = definition.ConnectionTimeoutSeconds is >= 3 and <= 120
            ? definition.ConnectionTimeoutSeconds.Value
            : 20;

        _definition = definition;
        await DisconnectCoreAsync().ConfigureAwait(false);
        Interlocked.Exchange(ref _intentionalClose, 0);

        _engine.SetTerminalType(string.IsNullOrWhiteSpace(definition.TerminalType)
            ? "xterm-256color"
            : definition.TerminalType!);
        _engine.SetTerminalSize(_cols, _rows);

        SetState(ConnectionState.Connecting, $"Connecting to {definition.Host}:{port} (Telnet · plaintext)…");
        _connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connectCts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
        var token = _connectCts.Token;

        TcpClient? client = null;
        try
        {
            client = new TcpClient();
            await client.ConnectAsync(definition.Host!, port, token).ConfigureAwait(false);
            var stream = client.GetStream();
            stream.ReadTimeout = timeoutSec * 1000;
            stream.WriteTimeout = timeoutSec * 1000;

            _client = client;
            _stream = stream;
            client = null;

            var initial = _engine.BuildInitialNegotiation();
            if (initial.Length > 0)
                await stream.WriteAsync(initial, token).ConfigureAwait(false);

            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token));
            SetState(ConnectionState.Connected,
                $"Connected · Telnet (unencrypted) · {definition.Host}:{port}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SafeClose(client);
            SetState(ConnectionState.Failed, "Telnet connection timed out.");
            throw new TimeoutException("Telnet connection timed out.");
        }
        catch (OperationCanceledException)
        {
            SafeClose(client);
            SetState(ConnectionState.Cancelled, "Telnet connect cancelled");
            throw;
        }
        catch (Exception ex)
        {
            SafeClose(client);
            var msg = Classify(ex);
            SetState(ConnectionState.Failed, msg);
            throw new InvalidOperationException(msg, ex);
        }
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_definition is null) return;
        if (_definition.AutoReconnect != true)
            throw new InvalidOperationException("Telnet reconnect requires an explicit profile preference.");
        SetState(ConnectionState.Reconnecting, "Reconnecting Telnet…");
        await ConnectAsync(_definition, cancellationToken).ConfigureAwait(false);
    }

    public void CancelConnect()
    {
        try { _connectCts?.Cancel(); } catch { /* ignore */ }
        try { _readCts?.Cancel(); } catch { /* ignore */ }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        var stream = _stream;
        if (stream is null || !IsConnected || data.Length == 0)
            return;

        var payload = TelnetNegotiationEngine.EscapeApplicationData(data);
        try
        {
            stream.Write(payload, 0, payload.Length);
            if (_definition?.LocalEcho == true && !_engine.RemoteEcho)
                OutputReceived?.Invoke(data.ToArray());
        }
        catch (Exception ex)
        {
            SetState(ConnectionState.Failed, "Telnet write failed: " + Sanitize(ex.Message));
        }
    }

    public void Resize(short columns, short rows)
    {
        _cols = columns;
        _rows = rows;
        _engine.SetTerminalSize(columns, rows);
        if (!IsConnected || !_engine.LocalEnabled.Contains(TelnetOption.Naws))
            return;
        try
        {
            var naws = _engine.BuildNawsSubnegotiation();
            _stream?.Write(naws, 0, naws.Length);
        }
        catch { /* ignore */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await DisconnectCoreAsync().ConfigureAwait(false);
        SetState(ConnectionState.Disconnected, "Disposed");
        Closed?.Invoke();
    }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        var buffer = new byte[8192];
        var stream = _stream;
        if (stream is null) return;

        try
        {
            while (!token.IsCancellationRequested && _client?.Connected == true)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }

                if (read <= 0)
                    break;

                var replies = new List<byte>();
                var app = _engine.Push(buffer.AsSpan(0, read), replies);
                if (replies.Count > 0)
                {
                    try { await stream.WriteAsync(replies.ToArray(), token).ConfigureAwait(false); }
                    catch { /* ignore */ }
                }

                if (app.Length > 0)
                    OutputReceived?.Invoke(app);
            }
        }
        finally
        {
            if (Volatile.Read(ref _intentionalClose) == 0 &&
                State is ConnectionState.Connected or ConnectionState.Reconnecting)
            {
                SetState(ConnectionState.Disconnected, "Telnet connection closed");
                Closed?.Invoke();
            }
        }
    }

    private async Task DisconnectCoreAsync()
    {
        Interlocked.Exchange(ref _intentionalClose, 1);
        try { _connectCts?.Cancel(); } catch { /* ignore */ }
        try { _readCts?.Cancel(); } catch { /* ignore */ }

        var stream = _stream;
        _stream = null;
        var client = _client;
        _client = null;

        try { stream?.Close(); } catch { /* ignore */ }
        try { client?.Close(); } catch { /* ignore */ }

        if (_readTask is not null)
        {
            try { await _readTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch { /* ignore */ }
            _readTask = null;
        }

        try { stream?.Dispose(); } catch { /* ignore */ }
        try { client?.Dispose(); } catch { /* ignore */ }
    }

    private static void SafeClose(TcpClient? client)
    {
        if (client is null) return;
        try { client.Close(); } catch { /* ignore */ }
        try { client.Dispose(); } catch { /* ignore */ }
    }

    private static string Classify(Exception ex)
    {
        var msg = Sanitize(ex.Message);
        if (ex is SocketException se)
        {
            return se.SocketErrorCode switch
            {
                SocketError.HostNotFound => "DNS lookup failed.",
                SocketError.ConnectionRefused => "Connection refused.",
                SocketError.TimedOut => "Connection timed out.",
                SocketError.NetworkUnreachable => "Network unreachable.",
                _ => "Network error: " + msg
            };
        }

        if (msg.Contains("No such host", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase))
            return "DNS lookup failed.";
        return msg;
    }

    private static string Sanitize(string message) =>
        string.IsNullOrEmpty(message)
            ? "Unknown error"
            : message
                .Replace("password", "********", StringComparison.OrdinalIgnoreCase)
                .Replace("passphrase", "********", StringComparison.OrdinalIgnoreCase);

    private void SetState(ConnectionState state, string? message)
    {
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke(state, message);
    }
}
