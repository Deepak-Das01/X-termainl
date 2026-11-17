using System.Collections.Concurrent;
using Renci.SshNet;
using XTerminal.Core.Models.Ssh;

namespace XTerminal.Protocols.Ssh;

public enum SshLeaseKind
{
    Shell,
    Forward
}

/// <summary>
/// Thread-safe shared <see cref="SshClient"/> ownership for shell + port forwards.
/// SFTP remains a separate <see cref="SftpClient"/> connection (SSH.NET limitation).
/// </summary>
public sealed class SshConnectionManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, SharedSshConnection> _connections = new();
    private int _disposed;

    public SharedSshConnection? TryGet(Guid sessionId) =>
        _connections.TryGetValue(sessionId, out var c) && !c.IsDisposed ? c : null;

    /// <summary>
    /// Registers a newly connected client and returns the owner's shell lease.
    /// If an entry already exists for the same identity, the new client is disposed and the existing connection is leased.
    /// </summary>
    public SshConnectionLease RegisterShell(Guid sessionId, SshClient client, SshSessionRecord record)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        while (true)
        {
            if (_connections.TryGetValue(sessionId, out var existing) && !existing.IsDisposed)
            {
                if (!existing.MatchesIdentity(record))
                    throw new InvalidOperationException(
                        "An SSH connection is already open for this session with a different host identity.");

                // Another consumer already owns a live client — dispose the duplicate.
                TryDisposeClient(client);
                return existing.Acquire(SshLeaseKind.Shell);
            }

            var shared = new SharedSshConnection(this, sessionId, client, record);
            if (_connections.TryAdd(sessionId, shared))
                return shared.Acquire(SshLeaseKind.Shell);

            TryDisposeClient(client);
            // race: retry
        }
    }

    public SshConnectionLease? TryAcquireForward(Guid sessionId)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!_connections.TryGetValue(sessionId, out var c) || c.IsDisposed || !c.Client.IsConnected)
            return null;
        return c.Acquire(SshLeaseKind.Forward);
    }

    public SshConnectionLease? TryAcquireShell(Guid sessionId)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!_connections.TryGetValue(sessionId, out var c) || c.IsDisposed)
            return null;
        return c.Acquire(SshLeaseKind.Shell);
    }

    internal void RemoveIfCurrent(Guid sessionId, SharedSshConnection shared) =>
        _connections.TryRemove(new KeyValuePair<Guid, SharedSshConnection>(sessionId, shared));

    private static void TryDisposeClient(SshClient client)
    {
        try { if (client.IsConnected) client.Disconnect(); } catch { /* ignore */ }
        try { client.Dispose(); } catch { /* ignore */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var id in _connections.Keys.ToList())
        {
            if (_connections.TryRemove(id, out var c))
                await c.ForceDisposeAsync().ConfigureAwait(false);
        }
    }
}

public sealed class SharedSshConnection
{
    private readonly SshConnectionManager _owner;
    private readonly object _gate = new();
    private int _refCount;
    private int _disposed;

    internal SharedSshConnection(SshConnectionManager owner, Guid sessionId, SshClient client, SshSessionRecord record)
    {
        _owner = owner;
        SessionId = sessionId;
        Client = client;
        Record = CloneIdentity(record);
        Host = record.Host;
        Port = record.Port <= 0 ? 22 : record.Port;
        Username = record.Username;
    }

    public Guid SessionId { get; }
    public SshClient Client { get; }
    public SshSessionRecord Record { get; }
    public string Host { get; }
    public int Port { get; }
    public string Username { get; }
    public int RefCount => Volatile.Read(ref _refCount);
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    public DateTimeOffset? LastRoundTripUtc { get; set; }
    public int? LatencyMs { get; set; }

    public bool MatchesIdentity(SshSessionRecord record) =>
        string.Equals(Host, record.Host, StringComparison.OrdinalIgnoreCase) &&
        Port == (record.Port <= 0 ? 22 : record.Port) &&
        string.Equals(Username, record.Username, StringComparison.Ordinal);

    internal SshConnectionLease Acquire(SshLeaseKind kind)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            Interlocked.Increment(ref _refCount);
            return new SshConnectionLease(this, kind);
        }
    }

    internal void Release(SshConnectionLease lease)
    {
        lock (_gate)
        {
            if (_disposed != 0) return;
            var remaining = Interlocked.Decrement(ref _refCount);
            if (remaining > 0) return;
            if (remaining < 0)
            {
                Interlocked.Exchange(ref _refCount, 0);
                return;
            }

            _disposed = 1;
            _owner.RemoveIfCurrent(SessionId, this);
            TryDisposeClient(Client);
        }
    }

    internal async ValueTask ForceDisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed != 0) return;
            _disposed = 1;
            _refCount = 0;
        }

        await Task.Run(() => TryDisposeClient(Client)).ConfigureAwait(false);
    }

    private static void TryDisposeClient(SshClient client)
    {
        try { if (client.IsConnected) client.Disconnect(); } catch { /* ignore */ }
        try { client.Dispose(); } catch { /* ignore */ }
    }

    private static SshSessionRecord CloneIdentity(SshSessionRecord r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Host = r.Host,
        Port = r.Port,
        Username = r.Username
    };
}

public sealed class SshConnectionLease : IDisposable
{
    private SharedSshConnection? _connection;

    internal SshConnectionLease(SharedSshConnection connection, SshLeaseKind kind)
    {
        _connection = connection;
        Kind = kind;
    }

    public SshLeaseKind Kind { get; }
    public SharedSshConnection Connection =>
        _connection ?? throw new ObjectDisposedException(nameof(SshConnectionLease));

    public SshClient Client => Connection.Client;
    public Guid SessionId => Connection.SessionId;

    public void Dispose()
    {
        var c = Interlocked.Exchange(ref _connection, null);
        c?.Release(this);
    }
}
