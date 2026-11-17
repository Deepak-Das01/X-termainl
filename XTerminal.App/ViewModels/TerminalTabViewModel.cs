using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTerminal.Core.Models;
using XTerminal.Core.Services;
using XTerminal.Protocols;
using XTerminal.Protocols.Ssh;

namespace XTerminal.App.ViewModels;

public partial class TerminalTabViewModel : ObservableObject, IWorkspaceTab, IAsyncDisposable
{
    private ISessionHost? _host;
    private readonly SessionHostFactory _factory;
    private readonly object _bufferLock = new();
    private readonly StringBuilder _buffer = new();
    private Action<string>? _outputHandler;
    private readonly ShellIntegrationParser _shellIntegration = new();

    [ObservableProperty] private string _title = "Terminal";
    [ObservableProperty] private string _status = "Starting…";
    [ObservableProperty] private ConnectionState _connectionState = ConnectionState.Idle;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _isPlaceholder;
    [ObservableProperty] private string? _remoteHost;
    [ObservableProperty] private string? _remoteUser;
    [ObservableProperty] private string? _authMethodLabel;
    [ObservableProperty] private string? _connectionDurationLabel;
    [ObservableProperty] private bool _privateInputMode;
    [ObservableProperty] private bool _isBroadcastTarget;
    [ObservableProperty] private double _zoomFontSize;
    [ObservableProperty] private int? _latencyMs;
    [ObservableProperty] private int? _lastExitCode;
    [ObservableProperty] private string? _lastCommandDurationLabel;
    [ObservableProperty] private string _shellIntegrationStatus = "Disabled";
    [ObservableProperty] private string? _trackedWorkingDirectory;
    private bool _privateInputWasActive;
    private string? _pendingIntegratedCommand;
    private DateTimeOffset? _pendingCommandStartedUtc;

    public Guid Id { get; } = Guid.NewGuid();
    public SessionDefinition Definition { get; }
    public string Glyph { get; }
    public string Accent { get; }
    public bool IsDirty => false;
    public bool IsEditor => false;
    public bool IsTerminalGroup => false;

    public event Action? Closed;
    public event Action? StatusChanged;
    public event Action<TerminalTabViewModel, ShellIntegrationEvent>? ShellIntegrationEvent;

    public TerminalTabViewModel(SessionDefinition definition, SessionHostFactory factory)
    {
        Definition = definition;
        _factory = factory;
        Title = definition.Name;
        Glyph = ShellPresentation.GlyphFor(definition);
        Accent = ShellPresentation.AccentFor(definition);
        RemoteHost = definition.Host;
        RemoteUser = definition.Username;
    }

    public void AttachOutput(Action<string> handler)
    {
        _outputHandler = handler;
        string snapshot;
        lock (_bufferLock) { snapshot = _buffer.ToString(); }
        if (snapshot.Length > 0) handler(snapshot);
    }

    public void DetachOutput(Action<string> handler)
    {
        if (ReferenceEquals(_outputHandler, handler)) _outputHandler = null;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _host = _factory.Create(Definition.Kind);
        _host.OutputReceived += OnOutput;
        _host.StateChanged += OnStateChanged;
        _host.Closed += () =>
        {
            ConnectionState = ConnectionState.Disconnected;
            Status = "Disconnected";
            StatusChanged?.Invoke();
            Closed?.Invoke();
        };

        try
        {
            ConnectionState = ConnectionState.Connecting;
            Status = "Connecting…";
            StatusChanged?.Invoke();
            await _host.ConnectAsync(Definition, cancellationToken);
            IsPlaceholder = !_host.IsImplemented;
            if (_host is SshSessionHost ssh)
            {
                RemoteHost = ssh.Hostname;
                RemoteUser = ssh.Username;
                AuthMethodLabel = ssh.AuthMethodLabel;
                RefreshDuration(ssh);
            }

            ConnectionState = _host.State;
            Status = _host.StatusMessage ?? ConnectionState.ToString();
            StatusChanged?.Invoke();
        }
        catch (Exception ex)
        {
            ConnectionState = ConnectionState.Failed;
            Status = ex.Message.Length > 120 ? ex.Message[..120] + "…" : ex.Message;
            StatusChanged?.Invoke();
            Emit($"\u001b[31mFailed to start session:\u001b[0m {Sanitize(ex.Message)}\r\n");
            IsPlaceholder = true;
        }
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        if (_host is null) return;
        try
        {
            await _host.ReconnectAsync();
            ConnectionState = _host.State;
            Status = _host.StatusMessage ?? ConnectionState.ToString();
            StatusChanged?.Invoke();
        }
        catch (Exception ex)
        {
            ConnectionState = ConnectionState.Failed;
            Status = "Failed";
            StatusChanged?.Invoke();
            Emit($"\u001b[31mReconnect failed:\u001b[0m {Sanitize(ex.Message)}\r\n");
        }
    }

    [RelayCommand]
    private void CancelConnect() => _host?.CancelConnect();

    public void WriteInput(string data)
    {
        if (string.IsNullOrEmpty(data) || _host is null) return;
        _host.Write(Encoding.UTF8.GetBytes(data));
    }

    public void TogglePrivateInput()
    {
        PrivateInputMode = !PrivateInputMode;
        if (PrivateInputMode)
        {
            _privateInputWasActive = true;
            _pendingIntegratedCommand = null;
            _pendingCommandStartedUtc = null;
        }

        StatusChanged?.Invoke();
    }

    public void AdjustZoom(double delta)
    {
        if (ZoomFontSize <= 0) ZoomFontSize = 13;
        ZoomFontSize = Math.Clamp(ZoomFontSize + delta, 8, 32);
        StatusChanged?.Invoke();
    }

    public void ResetZoom()
    {
        ZoomFontSize = 0;
        StatusChanged?.Invoke();
    }

    public void Resize(int cols, int rows)
    {
        if (_host is null || cols <= 0 || rows <= 0) return;
        _host.Resize((short)cols, (short)rows);
    }

    /// <summary>Optional OSC parsing — never executes local actions from terminal sequences.</summary>
    public bool ShellIntegrationEnabled
    {
        get => _shellIntegration.Enabled;
        set
        {
            _shellIntegration.Enabled = value;
            if (!value)
            {
                _shellIntegration.Reset();
                LastExitCode = null;
                LastCommandDurationLabel = null;
                ShellIntegrationStatus = "Disabled";
            }
            else
            {
                _shellIntegration.Reset();
                ShellIntegrationStatus = "Detecting";
            }
        }
    }

    private void OnStateChanged(ConnectionState state, string? message)
    {
        ConnectionState = state;
        Status = message ?? state.ToString();
        if (_host is SshSessionHost ssh)
        {
            AuthMethodLabel = ssh.AuthMethodLabel;
            RefreshDuration(ssh);
        }

        if (_host?.LatencyMs is int ms)
            LatencyMs = ms;
        else if (state is ConnectionState.Disconnected or ConnectionState.Failed or ConnectionState.Cancelled)
        {
            LatencyMs = null;
            ConnectionDurationLabel = null;
        }

        StatusChanged?.Invoke();
    }

    private void RefreshDuration(SshSessionHost ssh)
    {
        if (ssh.ConnectionDuration is { } d)
            ConnectionDurationLabel = d.TotalHours >= 1
                ? $"{(int)d.TotalHours}h {d.Minutes:D2}m"
                : $"{d.Minutes:D2}:{d.Seconds:D2}";
        else
            ConnectionDurationLabel = null;
    }

    private void OnOutput(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        if (_shellIntegration.Enabled)
        {
            // Always parse to keep the buffer healthy, but suppress command-text side effects
            // while Private Input is active or until a clean prompt after private input.
            var suppressSensitive = PrivateInputMode || _privateInputWasActive;
            foreach (var ev in _shellIntegration.Push(text))
            {
                ShellIntegrationStatus = _shellIntegration.State.ToString();
                if (suppressSensitive &&
                    ev.Kind is ShellIntegrationEventKind.CommandStarted or ShellIntegrationEventKind.CommandFinished)
                {
                    // Delayed finish events must not publish command text entered under Private Input.
                    continue;
                }

                if (ev.Kind == ShellIntegrationEventKind.PromptStarted ||
                    ev.Kind == ShellIntegrationEventKind.PromptCompleted)
                {
                    if (!PrivateInputMode)
                        _privateInputWasActive = false;
                }

                if (ev.Kind == ShellIntegrationEventKind.CommandStarted)
                {
                    _pendingIntegratedCommand = ev.Command;
                    _pendingCommandStartedUtc = ev.Utc;
                }

                if (ev.Kind == ShellIntegrationEventKind.CommandFinished)
                {
                    LastExitCode = ev.ExitCode;
                    LastCommandDurationLabel = ev.Duration is { } d
                        ? (d.TotalSeconds >= 1 ? $"{d.TotalSeconds:0.#}s" : $"{d.TotalMilliseconds:0}ms")
                        : null;
                }

                if (ev.Kind == ShellIntegrationEventKind.Directory &&
                    !string.IsNullOrWhiteSpace(ev.WorkingDirectory))
                {
                    TrackedWorkingDirectory = ev.WorkingDirectory;
                    Definition.WorkingDirectory = ev.WorkingDirectory;
                }

                ShellIntegrationEvent?.Invoke(this, ev);
                StatusChanged?.Invoke();
            }
        }

        Emit(text);
    }

    public (string? Command, DateTimeOffset? StartedUtc) ConsumePendingIntegratedCommand()
    {
        var cmd = _pendingIntegratedCommand;
        var started = _pendingCommandStartedUtc;
        _pendingIntegratedCommand = null;
        _pendingCommandStartedUtc = null;
        return (cmd, started);
    }

    private void Emit(string text)
    {
        lock (_bufferLock)
        {
            _buffer.Append(text);
            if (_buffer.Length > 512_000) _buffer.Remove(0, _buffer.Length - 256_000);
        }

        _outputHandler?.Invoke(text);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            _host.OutputReceived -= OnOutput;
            _host.StateChanged -= OnStateChanged;
            await _host.DisposeAsync();
            _host = null;
        }
    }

    private static string Sanitize(string message) =>
        message.Replace("password", "********", StringComparison.OrdinalIgnoreCase)
               .Replace("passphrase", "********", StringComparison.OrdinalIgnoreCase);
}
