using XTerminal.Core.Models.X11;

namespace XTerminal.Core.Services.X11;

/// <summary>
/// Session-scoped X11 forwarding coordinator. Phase 4C: always reports backend unavailable
/// for actual x11-req; still enforces policy, cookie isolation, and lifecycle cleanup.
/// </summary>
public sealed class X11ForwardingCoordinator : IDisposable
{
    private readonly X11CookieRegistry _cookies = new();
    private readonly Dictionary<Guid, X11ForwardingStatus> _status = new();
    private readonly object _gate = new();
    private int _disposed;

    public X11ForwardingStatus GetStatus(Guid sshSessionId)
    {
        lock (_gate)
            return _status.TryGetValue(sshSessionId, out var s) ? s : X11ForwardingStatus.Disabled;
    }

    /// <summary>
    /// Attempts to enable forwarding for an SSH session. Does not claim success without a backend.
    /// </summary>
    public (bool Accepted, X11ForwardingStatus Status, string Message) RequestForwarding(
        Guid sshSessionId,
        X11SessionSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!X11ProfileValidator.TryValidate(settings, out var error))
            return (false, X11ForwardingStatus.Rejected, error);

        if (!settings.EnableForwarding)
        {
            SetStatus(sshSessionId, X11ForwardingStatus.Disabled);
            _cookies.Release(sshSessionId);
            return (true, X11ForwardingStatus.Disabled, "X11 forwarding disabled.");
        }

        if (!X11BackendCapability.IsForwardingAvailable)
        {
            SetStatus(sshSessionId, X11ForwardingStatus.BackendUnavailable);
            _cookies.Release(sshSessionId);
            return (false, X11ForwardingStatus.BackendUnavailable, X11BackendCapability.BlockerReason);
        }

        // Future backend path: generate cookie, send x11-req, track channels.
        _ = _cookies.CreateForSession(sshSessionId);
        SetStatus(sshSessionId, X11ForwardingStatus.Requesting);
        return (false, X11ForwardingStatus.BackendUnavailable, X11BackendCapability.BlockerReason);
    }

    public bool TryValidatePresentedCookie(Guid sshSessionId, ReadOnlySpan<byte> presented) =>
        _cookies.ValidateForSession(sshSessionId, presented);

    public bool ShouldAcceptChannel(Guid sshSessionId)
    {
        lock (_gate)
        {
            if (!_status.TryGetValue(sshSessionId, out var status))
                return false;
            return X11SetupMessageParser.ShouldAcceptIncomingX11Channel(
                forwardingAuthorized: status is X11ForwardingStatus.Accepted or X11ForwardingStatus.ActiveGraphical or X11ForwardingStatus.Requesting,
                sessionOpen: status is not (X11ForwardingStatus.Disabled or X11ForwardingStatus.Disconnected or X11ForwardingStatus.Rejected or X11ForwardingStatus.BackendUnavailable));
        }
    }

    public void OnSessionClosed(Guid sshSessionId)
    {
        _cookies.Release(sshSessionId);
        SetStatus(sshSessionId, X11ForwardingStatus.Disconnected);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_gate)
        {
            foreach (var id in _status.Keys.ToList())
                _cookies.Release(id);
            _status.Clear();
        }
        _cookies.Dispose();
    }

    private void SetStatus(Guid id, X11ForwardingStatus status)
    {
        lock (_gate) _status[id] = status;
    }
}
