using XTerminal.Core.Models;

namespace XTerminal.Protocols.Desktop;

/// <summary>
/// Graphical remote desktop session — distinct from terminal byte-stream hosts.
/// Do not route framebuffer pixels through <see cref="ISessionHost.OutputReceived"/>.
/// </summary>
public interface IGraphicalDesktopSession : IAsyncDisposable
{
    SessionKind Kind { get; }
    string Title { get; }
    bool IsConnected { get; }
    bool IsImplemented { get; }
    string? StatusMessage { get; }
    ConnectionState State { get; }

    event Action<ConnectionState, string?>? StateChanged;

    Task ConnectAsync(SessionDefinition definition, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task ReconnectAsync(CancellationToken cancellationToken = default);
    void CancelConnect();
}
