using System.Collections.Concurrent;
using Renci.SshNet;
using XTerminal.Core.Models;
using XTerminal.Core.Models.PortForwards;
using XTerminal.Core.Models.Ssh;

namespace XTerminal.Protocols.Ssh;

/// <summary>
/// Runtime manager for SSH port forwards. Reuses <see cref="SshTransportFactory"/> auth — no second SSH stack.
/// </summary>
public sealed class PortForwardRuntimeService : IAsyncDisposable
{
    private readonly SshTransportFactory _transport;
    private readonly SshConnectionManager? _connections;
    private readonly ConcurrentDictionary<Guid, ActiveForward> _active = new();

    public PortForwardRuntimeService(SshTransportFactory transport, SshConnectionManager? connections = null)
    {
        _transport = transport;
        _connections = connections;
    }

    public event Action? StateChanged;

    public PortForwardRuntimeState GetState(Guid id) =>
        _active.TryGetValue(id, out var a) ? a.State : PortForwardRuntimeState.Stopped;

    public string? GetError(Guid id) =>
        _active.TryGetValue(id, out var a) ? a.Error : null;

    public DateTimeOffset? GetStartedUtc(Guid id) =>
        _active.TryGetValue(id, out var a) ? a.StartedUtc : null;

    public TimeSpan? GetUptime(Guid id) =>
        _active.TryGetValue(id, out var a) && a.StartedUtc is { } s &&
        a.State is PortForwardRuntimeState.Running or PortForwardRuntimeState.Active
            ? DateTimeOffset.UtcNow - s
            : null;

    public string GetTrafficLabel(Guid id) =>
        _active.TryGetValue(id, out var a) &&
        a.State is PortForwardRuntimeState.Running or PortForwardRuntimeState.Active
            ? "listener open"
            : "N/A";

    public int GetConnections(Guid id) =>
        // Approximate: listener up ≠ verified destination connectivity.
        _active.TryGetValue(id, out var a) &&
        a.State is PortForwardRuntimeState.Running or PortForwardRuntimeState.Active
            ? Math.Max(1, a.Connections)
            : 0;

    /// <summary>SSH.NET does not expose trustworthy per-forward byte counters.</summary>
    public (long? Sent, long? Received) GetBytes(Guid id) => (null, null);

    public async Task StartAsync(PortForwardDefinition def, SshSessionRecord session, CancellationToken ct = default)
    {
        await StopAsync(def.Id);

        var entry = new ActiveForward { State = PortForwardRuntimeState.Starting };
        _active[def.Id] = entry;
        StateChanged?.Invoke();

        try
        {
            Validate(def);
            DetectLocalConflict(def);

            var bind = NormalizeBind(def);
            SshClient client;
            TunnelOwnership? ownership = null;
            SshConnectionLease? lease = null;
            if (_connections?.TryAcquireForward(session.Id) is { } sharedLease)
            {
                lease = sharedLease;
                client = lease.Client;
                entry.UsesSharedConnection = true;
                entry.Lease = lease;
            }
            else
            {
                var owned = await _transport.OpenSshClientAsync(session, ct);
                client = owned.Client;
                ownership = owned.Ownership;
            }

            ForwardedPort port = def.Type switch
            {
                SshForwardType.Remote => new ForwardedPortRemote(
                    bind,
                    (uint)def.ListenPort,
                    def.DestinationHost ?? "127.0.0.1",
                    (uint)def.DestinationPort),
                SshForwardType.Dynamic => new ForwardedPortDynamic(bind, (uint)def.ListenPort),
                _ => new ForwardedPortLocal(
                    bind,
                    (uint)def.ListenPort,
                    def.DestinationHost ?? "127.0.0.1",
                    (uint)def.DestinationPort)
            };

            client.AddForwardedPort(port);
            await Task.Run(() => port.Start(), ct);

            entry.Client = client;
            entry.Ownership = ownership;
            entry.Port = port;
            entry.State = PortForwardRuntimeState.Running;
            entry.Error = null;
            entry.SessionId = session.Id;
            entry.Reconnect = def.ReconnectWithSsh;
            entry.Connections = 1;
            entry.StartedUtc = DateTimeOffset.UtcNow;
            entry.LastTransitionUtc = entry.StartedUtc;
        }
        catch (Exception ex)
        {
            entry.State = PortForwardRuntimeState.Failed;
            entry.Error = Sanitize(ex.Message);
            entry.LastTransitionUtc = DateTimeOffset.UtcNow;
            DisposeClient(entry.Client, entry.Ownership);
            entry.Client = null;
            entry.Ownership = null;
            entry.Port = null;
        }

        StateChanged?.Invoke();
    }

    public Task StopAsync(Guid id) =>
        Task.Run(() => StopCore(id));

    private void StopCore(Guid id)
    {
        if (!_active.TryGetValue(id, out var entry))
            return;

        entry.State = PortForwardRuntimeState.Stopping;
        entry.LastTransitionUtc = DateTimeOffset.UtcNow;
        StateChanged?.Invoke();
        _active.TryRemove(id, out _);
        try { entry.Port?.Stop(); } catch { /* ignore */ }
        try
        {
            if (entry.Port is not null && entry.Client is not null)
                entry.Client.RemoveForwardedPort(entry.Port);
        }
        catch { /* ignore */ }
        try { entry.Port?.Dispose(); } catch { /* ignore */ }
        if (entry.UsesSharedConnection)
            entry.Lease?.Dispose();
        else
            DisposeClient(entry.Client, entry.Ownership);
        entry.Lease = null;
        entry.Client = null;
        entry.Ownership = null;
        entry.Port = null;
        entry.State = PortForwardRuntimeState.Stopped;
        StateChanged?.Invoke();
    }

    private static void DisposeClient(SshClient? client, TunnelOwnership? ownership)
    {
        if (ownership is not null)
        {
            try { ownership.Dispose(); } catch { /* ignore */ }
            return;
        }

        if (client is null) return;

        // SSH.NET Disconnect can block indefinitely when forwarded ports are winding down.
        // Bound the teardown so callers (and live tests) cannot hang forever.
        try
        {
            var done = Task.Run(() =>
            {
                try
                {
                    if (client.IsConnected)
                        client.Disconnect();
                }
                catch { /* ignore */ }

                try { client.Dispose(); } catch { /* ignore */ }
            });
            if (!done.Wait(TimeSpan.FromSeconds(8)))
            {
                try { client.Dispose(); } catch { /* ignore */ }
            }
        }
        catch
        {
            try { client.Dispose(); } catch { /* ignore */ }
        }
    }

    public async Task RestartAsync(PortForwardDefinition def, SshSessionRecord session, CancellationToken ct = default)
    {
        await StopAsync(def.Id);
        await StartAsync(def, session, ct);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _active.Keys.ToList())
            await StopAsync(id);
    }

    private static void Validate(PortForwardDefinition def)
    {
        if (def.ListenPort is < 1 or > 65535)
            throw new InvalidOperationException("Listen port must be between 1 and 65535.");
        if (def.Type != SshForwardType.Dynamic && def.DestinationPort is < 1 or > 65535)
            throw new InvalidOperationException("Destination port must be between 1 and 65535.");
        if (def.Type != SshForwardType.Dynamic && string.IsNullOrWhiteSpace(def.DestinationHost))
            throw new InvalidOperationException("Destination host is required.");
    }

    private static string NormalizeBind(PortForwardDefinition def)
    {
        if (def.AllowLan && (def.BindAddress is "127.0.0.1" or "localhost" or "::1"))
            return "0.0.0.0";
        return string.IsNullOrWhiteSpace(def.BindAddress) ? "127.0.0.1" : def.BindAddress;
    }

    private static void DetectLocalConflict(PortForwardDefinition def)
    {
        if (def.Type == SshForwardType.Remote) return;
        try
        {
            var bind = NormalizeBind(def);
            if (!System.Net.IPAddress.TryParse(bind is "localhost" ? "127.0.0.1" : bind, out var ip))
                ip = System.Net.IPAddress.Loopback;
            var listener = new System.Net.Sockets.TcpListener(ip, def.ListenPort);
            listener.Start();
            listener.Stop();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Unable to bind {def.BindAddress}:{def.ListenPort} — {Sanitize(ex.Message)}");
        }
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown error";
        return message.Length > 240 ? message[..240] + "…" : message;
    }

    private sealed class ActiveForward
    {
        public PortForwardRuntimeState State { get; set; } = PortForwardRuntimeState.Stopped;
        public string? Error { get; set; }
        public SshClient? Client { get; set; }
        public TunnelOwnership? Ownership { get; set; }
        public ForwardedPort? Port { get; set; }
        public Guid SessionId { get; set; }
        public bool Reconnect { get; set; }
        public bool UsesSharedConnection { get; set; }
        public SshConnectionLease? Lease { get; set; }
        public int Connections { get; set; }
        public DateTimeOffset? StartedUtc { get; set; }
        public DateTimeOffset? LastTransitionUtc { get; set; }
    }
}
