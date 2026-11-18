using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTerminal.App.Desktop;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Desktop;
using XTerminal.Core.Security;
using XTerminal.Core.Services;
using XTerminal.Protocols.Desktop;
using XTerminal.Protocols.Vnc;

namespace XTerminal.App.ViewModels;

public partial class DesktopTabViewModel : ObservableObject, IWorkspaceTab, IAsyncDisposable
{
    private readonly ISecretStore _secrets;
    private IGraphicalDesktopSession? _session;
    private VncGraphicalSession? _vnc;
    private int _disposed;

    public DesktopTabViewModel(SessionDefinition definition, ISecretStore secrets)
    {
        Definition = definition;
        _secrets = secrets;
        Title = definition.Name;
        Glyph = definition.Kind == SessionKind.Rdp ? "🖥" : "🖼";
        Accent = definition.Kind == SessionKind.Rdp ? "#5B9BD5" : "#7EAE9E";
        Status = "Disconnected";
    }

    public Guid Id { get; } = Guid.NewGuid();
    public SessionDefinition Definition { get; }
    public string Glyph { get; }
    public string Accent { get; }
    public bool IsEditor => false;
    public bool IsDirty => false;
    public bool IsTerminalGroup => false;
    public bool IsDesktopTab => true;

    [ObservableProperty] private string _title;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private ConnectionState _connectionState = ConnectionState.Idle;
    [ObservableProperty] private bool _isPlaceholder;
    [ObservableProperty] private bool _isFullscreen;
    [ObservableProperty] private ImageSource? _vncFrame;
    [ObservableProperty] private bool _isRdp;
    [ObservableProperty] private string _securityBanner = "";

    public RdpActiveXHost? RdpControl { get; private set; }
    public event Action? StatusChanged;
    public event Action? FrameChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Definition.Kind == SessionKind.Rdp)
        {
            IsRdp = true;
            await StartRdpAsync(cancellationToken);
            return;
        }

        if (Definition.Kind == SessionKind.Vnc)
        {
            IsRdp = false;
            await StartVncAsync(cancellationToken);
            return;
        }

        throw new InvalidOperationException("Not a graphical desktop session.");
    }

    private async Task StartRdpAsync(CancellationToken cancellationToken = default)
    {
        if (!RdpActiveXHost.IsRuntimeAvailable())
        {
            ConnectionState = ConnectionState.Failed;
            Status = "Microsoft RDP ActiveX (MsRdpClient9) is not available on this system.";
            StatusChanged?.Invoke();
            return;
        }

        var profile = Definition.ToRdpProfile();
        if (!RdpProfileValidator.TryValidate(profile, out var error))
        {
            ConnectionState = ConnectionState.Failed;
            Status = error;
            StatusChanged?.Invoke();
            return;
        }

        ConnectionState = ConnectionState.Connecting;
        Status = $"Connecting RDP {profile.Host}:{profile.Port}…";
        StatusChanged?.Invoke();

        RdpControl = new RdpActiveXHost();
        RdpControl.Connected += () =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ConnectionState = ConnectionState.Connected;
                Status = $"Connected · RDP · {profile.Host}:{profile.Port} · NLA/CredSSP";
                StatusChanged?.Invoke();
            });
        };
        RdpControl.Disconnected += _ =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ConnectionState = ConnectionState.Disconnected;
                Status = "RDP disconnected";
                StatusChanged?.Invoke();
            });
        };
        RdpControl.FatalError += msg =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ConnectionState = ConnectionState.Failed;
                Status = msg;
                StatusChanged?.Invoke();
            });
        };

        // Control must be sited in WindowsFormsHost before Connect — view calls AttachRdpHost then BeginRdpConnect.
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void BeginRdpConnect()
    {
        if (RdpControl is null || Definition.Kind != SessionKind.Rdp) return;
        var profile = Definition.ToRdpProfile();
        var password = _secrets.GetSecretAsync(DesktopSecretKeys.RdpPassword(Definition.Id))
            .GetAwaiter().GetResult();
        try
        {
            RdpControl.ApplyAndConnect(
                profile.Host,
                profile.Port,
                profile.Username,
                profile.Domain,
                password,
                profile.DesktopWidth,
                profile.DesktopHeight,
                profile.ColorDepth,
                profile.SmartSizing,
                enableCredSsp: profile.RequireNla,
                authenticationLevel: profile.AuthenticationLevel,
                redirectClipboard: profile.RedirectClipboard,
                redirectDrives: profile.RedirectDrives,
                redirectPrinters: profile.RedirectPrinters,
                redirectAudio: profile.RedirectAudio);
        }
        catch (Exception ex)
        {
            ConnectionState = ConnectionState.Failed;
            Status = ex.Message;
            StatusChanged?.Invoke();
        }
    }

    private async Task StartVncAsync(CancellationToken cancellationToken = default)
    {
        var profile = Definition.ToVncProfile();
        if (profile.AuthenticationMode == VncAuthenticationMode.None || profile.AllowUnencrypted)
            SecurityBanner = "VNC session may be unencrypted — desktop traffic can be observed on the network.";
        else
            SecurityBanner = "VNC Auth does not encrypt framebuffer or input traffic.";

        var session = new VncGraphicalSession(_secrets);
        _vnc = session;
        _session = session;
        session.StateChanged += (state, msg) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ConnectionState = state;
                Status = msg ?? state.ToString();
                StatusChanged?.Invoke();
            });
        };
        session.FrameUpdated += frame =>
        {
            Application.Current?.Dispatcher.Invoke(() => ApplyVncFrame(frame));
        };

        await session.ConnectAsync(Definition, cancellationToken);
    }

    private void ApplyVncFrame(VncFramebuffer frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length < frame.Width * frame.Height * 4)
            return;
        var bmp = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Width * 4, 0);
        bmp.Freeze();
        VncFrame = bmp;
        FrameChanged?.Invoke();
    }

    public void SendVncPointer(int framebufferX, int framebufferY, byte buttons)
    {
        _vnc?.Client?.SendPointer(framebufferX, framebufferY, buttons);
    }

    public void SendVncKey(uint keysym, bool down)
    {
        _vnc?.Client?.SendKey(keysym, down);
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        if (Definition.Kind == SessionKind.Rdp)
        {
            try { RdpControl?.Disconnect(); } catch { /* ignore */ }
            IsPlaceholder = false;
            await StartRdpAsync();
            BeginRdpConnect();
            return;
        }

        if (_session is not null)
            await _session.ReconnectAsync();
        else
            await StartVncAsync();
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (Definition.Kind == SessionKind.Rdp)
        {
            RdpControl?.Disconnect();
            ConnectionState = ConnectionState.Disconnected;
            Status = "Disconnected";
            StatusChanged?.Invoke();
            return;
        }

        if (_session is not null)
            await _session.DisconnectAsync();
    }

    [RelayCommand]
    private void CancelConnect()
    {
        _session?.CancelConnect();
        try { RdpControl?.Disconnect(); } catch { /* ignore */ }
        ConnectionState = ConnectionState.Cancelled;
        Status = "Cancelled";
        StatusChanged?.Invoke();
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        IsFullscreen = !IsFullscreen;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { RdpControl?.Disconnect(); } catch { /* ignore */ }
        RdpControl?.Dispose();
        RdpControl = null;
        if (_session is not null)
            await _session.DisposeAsync();
        _session = null;
        _vnc = null;
    }
}
