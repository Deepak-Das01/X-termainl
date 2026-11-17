using XTerminal.Core.Models;
using XTerminal.Terminal.ConPty;

namespace XTerminal.Protocols.Local;

public sealed class LocalShellSessionHost : ISessionHost
{
    private ConPtySession? _pty;
    private SessionDefinition? _definition;
    private int _reconnectGate;
    private int _disposed;

    public SessionKind Kind => SessionKind.LocalShell;
    public string Title => _definition?.Name ?? "Local";
    public bool IsConnected => _pty?.IsRunning == true;
    public bool IsImplemented => true;
    public string? StatusMessage { get; private set; }
    public ConnectionState State { get; private set; } = ConnectionState.Idle;

    public event Action<byte[]>? OutputReceived;
    public event Action? Closed;
    public event Action<ConnectionState, string?>? StateChanged;

    public async Task ConnectAsync(SessionDefinition definition, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        _definition = definition;
        await RetirePtyAsync().ConfigureAwait(false);

        SetState(ConnectionState.Connecting, "Starting local shell…");
        ConPtySession? pty = null;
        try
        {
            pty = new ConPtySession();
            pty.OutputReceived += data => OutputReceived?.Invoke(data);
            pty.Exited += OnPtyExited;
            pty.Start(definition);
            _pty = pty;
            StatusMessage = "Connected";
            SetState(ConnectionState.Connected, StatusMessage);
        }
        catch
        {
            if (pty is not null)
            {
                try { await pty.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
            }

            _pty = null;
            SetState(ConnectionState.Failed, "Failed to start local shell");
            throw;
        }
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_definition is null)
            return;

        if (Interlocked.CompareExchange(ref _reconnectGate, 1, 0) != 0)
            return;

        try
        {
            SetState(ConnectionState.Reconnecting, "Restarting local shell…");
            await ConnectAsync(_definition, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectGate, 0);
        }
    }

    public void CancelConnect()
    {
        // Local shells start synchronously.
    }

    public void Write(ReadOnlySpan<byte> data) => _pty?.Write(data);

    public void Resize(short columns, short rows) => _pty?.Resize(columns, rows);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await RetirePtyAsync().ConfigureAwait(false);
        SetState(ConnectionState.Disconnected, "Disposed");
    }

    private void OnPtyExited()
    {
        StatusMessage = "Shell exited";
        SetState(ConnectionState.Disconnected, StatusMessage);
        Closed?.Invoke();
    }

    private async Task RetirePtyAsync()
    {
        var pty = Interlocked.Exchange(ref _pty, null);
        if (pty is null) return;
        try { pty.Exited -= OnPtyExited; } catch { /* ignore */ }
        try { await pty.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
    }

    private void SetState(ConnectionState state, string? message)
    {
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke(state, message);
    }
}
