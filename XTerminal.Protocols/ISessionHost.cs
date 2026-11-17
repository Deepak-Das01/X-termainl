using XTerminal.Core.Models;

namespace XTerminal.Protocols;

public interface ISessionHost : IAsyncDisposable
{
    SessionKind Kind { get; }
    string Title { get; }
    bool IsConnected { get; }
    bool IsImplemented { get; }
    string? StatusMessage { get; }
    ConnectionState State { get; }
    /// <summary>Approximate SSH round-trip latency in ms, or null when unavailable.</summary>
    int? LatencyMs => null;

    event Action<byte[]>? OutputReceived;
    event Action? Closed;
    event Action<ConnectionState, string?>? StateChanged;

    Task ConnectAsync(SessionDefinition definition, CancellationToken cancellationToken = default);
    Task ReconnectAsync(CancellationToken cancellationToken = default);
    void CancelConnect();
    void Write(ReadOnlySpan<byte> data);
    void Resize(short columns, short rows);
}
