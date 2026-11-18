using XTerminal.Core.Models;
using XTerminal.Core.Models.Desktop;
using XTerminal.Core.Security;
using XTerminal.Core.Services;
using XTerminal.Protocols.Desktop;

namespace XTerminal.Protocols.Vnc;

public sealed class VncGraphicalSession : IGraphicalDesktopSession
{
    private readonly ISecretStore _secrets;
    private VncRfbClient? _client;
    private SessionDefinition? _definition;
    private int _disposed;
    private CancellationTokenSource? _connectCts;

    public VncGraphicalSession(ISecretStore secrets) => _secrets = secrets;

    public SessionKind Kind => SessionKind.Vnc;
    public string Title => _definition?.Name ?? "VNC";
    public bool IsConnected => _client?.IsConnected == true;
    public bool IsImplemented => true;
    public string? StatusMessage { get; private set; }
    public ConnectionState State { get; private set; } = ConnectionState.Idle;
    public VncRfbClient? Client => _client;

    public event Action<ConnectionState, string?>? StateChanged;
    public event Action<VncFramebuffer>? FrameUpdated;

    public async Task ConnectAsync(SessionDefinition definition, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var profile = definition.ToVncProfile();
        if (!VncProfileValidator.TryValidate(profile, out var error))
            throw new InvalidOperationException(error);

        _definition = definition;
        await DisconnectAsync().ConfigureAwait(false);
        SetState(ConnectionState.Connecting, $"Connecting VNC {profile.Host}:{profile.Port}…");

        _connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connectCts.CancelAfter(TimeSpan.FromSeconds(profile.ConnectionTimeoutSeconds));
        var token = _connectCts.Token;

        string? password = null;
        try
        {
            password = await _secrets.GetSecretAsync(DesktopSecretKeys.VncPassword(definition.Id), token)
                .ConfigureAwait(false);

            var client = new VncRfbClient();
            client.FrameUpdated += frame => FrameUpdated?.Invoke(frame);
            client.Disconnected += () =>
            {
                if (State == ConnectionState.Connected)
                    SetState(ConnectionState.Disconnected, "VNC disconnected");
            };
            client.ProtocolError += msg => SetState(ConnectionState.Failed, Sanitize(msg));

            await client.ConnectAsync(profile.Host, profile.Port, profile, password, token)
                .ConfigureAwait(false);

            _client = client;
            var sec = client.NegotiatedSecurityType == VncSecurityType.None
                ? "unencrypted"
                : "VNC Auth (not transport-encrypted)";
            SetState(ConnectionState.Connected,
                $"Connected · VNC · {profile.Host}:{profile.Port} · {sec} · RFB {client.NegotiatedVersion}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetState(ConnectionState.Failed, "VNC connection timed out.");
            throw;
        }
        catch (OperationCanceledException)
        {
            SetState(ConnectionState.Cancelled, "VNC connect cancelled");
            throw;
        }
        catch (Exception ex)
        {
            SetState(ConnectionState.Failed, Sanitize(ex.Message));
            throw;
        }
        finally
        {
            // Do not retain password string longer than needed (best-effort).
            password = null;
        }
    }

    public async Task DisconnectAsync()
    {
        try { _connectCts?.Cancel(); } catch { /* ignore */ }
        var client = _client;
        _client = null;
        if (client is not null)
            await client.DisposeAsync().ConfigureAwait(false);
        if (State is ConnectionState.Connected or ConnectionState.Connecting)
            SetState(ConnectionState.Disconnected, "Disconnected");
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_definition is null) return;
        if (_definition.AutoReconnect != true)
            throw new InvalidOperationException("VNC reconnect requires an explicit profile preference.");
        SetState(ConnectionState.Reconnecting, "Reconnecting VNC…");
        await ConnectAsync(_definition, cancellationToken).ConfigureAwait(false);
    }

    public void CancelConnect()
    {
        try { _connectCts?.Cancel(); } catch { /* ignore */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await DisconnectAsync().ConfigureAwait(false);
    }

    private void SetState(ConnectionState state, string? message)
    {
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke(state, message);
    }

    private static string Sanitize(string message) =>
        string.IsNullOrEmpty(message)
            ? "Unknown error"
            : message.Replace("password", "********", StringComparison.OrdinalIgnoreCase);
}
