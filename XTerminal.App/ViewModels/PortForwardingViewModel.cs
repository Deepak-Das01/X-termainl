using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTerminal.Core.Models;
using XTerminal.Core.Models.PortForwards;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Services;
using XTerminal.Protocols.Ssh;

namespace XTerminal.App.ViewModels;

public partial class PortForwardingViewModel : ObservableObject
{
    private readonly IPortForwardStore _store;
    private readonly ISshSessionLibrary _sessions;
    private readonly PortForwardRuntimeService _runtime;
    private PortForwardDocument _document = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private PortForwardRow? _selectedForward;
    [ObservableProperty] private bool _hasForwards;
    [ObservableProperty] private int _activeCount;
    [ObservableProperty] private int _stoppedCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private string? _errorBanner;
    [ObservableProperty] private bool _hasError;

    public ObservableCollection<PortForwardRow> Forwards { get; } = new();
    public ObservableCollection<SshSessionRecord> SessionOptions { get; } = new();

    public event Action<SshSessionRecord>? EditSessionRequested;

    public PortForwardingViewModel(
        IPortForwardStore store,
        ISshSessionLibrary sessions,
        PortForwardRuntimeService runtime)
    {
        _store = store;
        _sessions = sessions;
        _runtime = runtime;
        _runtime.StateChanged += RefreshStates;
    }

    public async Task InitializeAsync()
    {
        _document = await _store.LoadAsync();
        await RefreshSessionsAsync();
        RebuildList();
    }

    public async Task RefreshSessionsAsync()
    {
        var lib = await _sessions.LoadAsync();
        SessionOptions.Clear();
        foreach (var s in lib.Sessions.OrderBy(x => x.Name))
            SessionOptions.Add(s);
    }

    [RelayCommand]
    private async Task NewForwardAsync()
    {
        await RefreshSessionsAsync();
        if (SessionOptions.Count == 0)
        {
            SetError("Create an SSH session first.");
            return;
        }

        var def = new PortForwardDefinition
        {
            Name = "Local forward",
            SshSessionId = SessionOptions[0].Id,
            Type = SshForwardType.Local,
            ListenPort = 18080,
            DestinationHost = "127.0.0.1",
            DestinationPort = 80
        };

        var editor = new PortForwardEditorViewModel(def, SessionOptions.ToList());
        var dlg = new Views.PortForwardEditorWindow(editor)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (dlg.ShowDialog() != true || !dlg.Saved) return;

        var created = editor.ToDefinition();
        if (HasDuplicateBind(created))
        {
            SetError($"Another forward already uses {created.BindAddress}:{created.ListenPort}.");
            return;
        }

        _document.Forwards.Add(created);
        await _store.SaveAsync(_document);
        RebuildList();
        ClearError();
    }

    [RelayCommand]
    private async Task EditForwardAsync(PortForwardRow? row)
    {
        row ??= SelectedForward;
        if (row is null) return;
        await RefreshSessionsAsync();
        var editor = new PortForwardEditorViewModel(Clone(row.Definition), SessionOptions.ToList());
        var dlg = new Views.PortForwardEditorWindow(editor)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (dlg.ShowDialog() != true || !dlg.Saved) return;

        var updated = editor.ToDefinition();
        if (HasDuplicateBind(updated))
        {
            SetError($"Another forward already uses {updated.BindAddress}:{updated.ListenPort}.");
            return;
        }

        var idx = _document.Forwards.FindIndex(f => f.Id == updated.Id);
        if (idx >= 0) _document.Forwards[idx] = updated;
        await _store.SaveAsync(_document);
        RebuildList();
        ClearError();
    }

    private bool HasDuplicateBind(PortForwardDefinition candidate) =>
        PortForwardBindRules.HasDuplicateBind(
            _document.Forwards.Select(f => (
                f.Id,
                f.BindAddress,
                f.ListenPort,
                f.Type == SshForwardType.Remote)),
            candidate.Id,
            candidate.BindAddress,
            candidate.ListenPort,
            candidate.Type == SshForwardType.Remote);

    [RelayCommand]
    private async Task StartAsync(PortForwardRow? row)
    {
        row ??= SelectedForward;
        if (row is null) return;
        ClearError();
        var session = await ResolveSessionAsync(row.Definition.SshSessionId);
        if (session is null)
        {
            SetError("SSH session not found for this forward.");
            return;
        }

        await _runtime.StartAsync(row.Definition, session);
        RefreshStates();
        if (_runtime.GetState(row.Definition.Id) == PortForwardRuntimeState.Error)
            SetError(_runtime.GetError(row.Definition.Id) ?? "Failed to start forward.");
    }

    [RelayCommand]
    private async Task StopAsync(PortForwardRow? row)
    {
        row ??= SelectedForward;
        if (row is null) return;
        await _runtime.StopAsync(row.Definition.Id);
        RefreshStates();
    }

    [RelayCommand]
    private async Task RestartAsync(PortForwardRow? row)
    {
        row ??= SelectedForward;
        if (row is null) return;
        var session = await ResolveSessionAsync(row.Definition.SshSessionId);
        if (session is null)
        {
            SetError("SSH session not found for this forward.");
            return;
        }

        await _runtime.RestartAsync(row.Definition, session);
        RefreshStates();
    }

    [RelayCommand]
    private async Task DeleteAsync(PortForwardRow? row)
    {
        row ??= SelectedForward;
        if (row is null) return;
        var confirm = System.Windows.MessageBox.Show(
            $"Delete port forward '{row.Name}'?",
            "Confirm delete",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        await _runtime.StopAsync(row.Definition.Id);
        _document.Forwards.RemoveAll(f => f.Id == row.Definition.Id);
        await _store.SaveAsync(_document);
        RebuildList();
    }

    [RelayCommand]
    private async Task OpenSessionAsync(PortForwardRow? row)
    {
        row ??= SelectedForward;
        if (row is null) return;
        var session = await ResolveSessionAsync(row.Definition.SshSessionId);
        if (session is not null) EditSessionRequested?.Invoke(session);
    }

    partial void OnSearchTextChanged(string value) => RebuildList();

    private void RebuildList()
    {
        var query = SearchText?.Trim() ?? string.Empty;
        Forwards.Clear();
        foreach (var def in _document.Forwards.OrderBy(f => f.Name))
        {
            if (!string.IsNullOrEmpty(query) &&
                def.Name.Contains(query, StringComparison.OrdinalIgnoreCase) == false &&
                def.ListenPort.ToString().Contains(query) == false)
                continue;

            var sessionName = SessionOptions.FirstOrDefault(s => s.Id == def.SshSessionId)?.Name ?? "(missing)";
            Forwards.Add(CreateRow(def, sessionName));
        }

        HasForwards = Forwards.Count > 0;
        RefreshStates();
    }

    private void RefreshStates()
    {
        foreach (var row in Forwards)
        {
            row.State = _runtime.GetState(row.Definition.Id);
            row.StateLabel = row.State switch
            {
                PortForwardRuntimeState.Active => "● Active",
                PortForwardRuntimeState.Starting => "◌ Starting",
                PortForwardRuntimeState.Error => "! Error",
                _ => "○ Stopped"
            };
            row.Error = _runtime.GetError(row.Definition.Id);
            row.Traffic = _runtime.GetTrafficLabel(row.Definition.Id);
            row.Connections = _runtime.GetConnections(row.Definition.Id);
            // SSH.NET does not expose per-forward byte counters — never invent metrics.
            row.SentDisplay = "N/A";
            row.ReceivedDisplay = "N/A";
            row.ErrorMessage = row.Error;
        }

        ActiveCount = Forwards.Count(f => f.State == PortForwardRuntimeState.Active);
        ErrorCount = Forwards.Count(f => f.State == PortForwardRuntimeState.Error);
        StoppedCount = Forwards.Count - ActiveCount - ErrorCount -
                       Forwards.Count(f => f.State == PortForwardRuntimeState.Starting);
        if (StoppedCount < 0) StoppedCount = 0;
    }

    private async Task<SshSessionRecord?> ResolveSessionAsync(Guid id)
    {
        var lib = await _sessions.LoadAsync();
        return lib.Sessions.FirstOrDefault(s => s.Id == id);
    }

    private static PortForwardRow CreateRow(PortForwardDefinition def, string sessionName) => new()
    {
        Definition = def,
        Name = def.Name,
        TypeLabel = def.Type.ToString(),
        BindAddress = def.BindAddress,
        ListenPort = def.ListenPort,
        RemoteHost = def.Type == SshForwardType.Dynamic ? "—" : (def.DestinationHost ?? "—"),
        RemotePort = def.Type == SshForwardType.Dynamic ? 0 : def.DestinationPort,
        SessionName = sessionName,
        State = PortForwardRuntimeState.Stopped,
        StateLabel = "○ Stopped",
        Traffic = "—"
    };

    private static PortForwardDefinition Clone(PortForwardDefinition d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        SshSessionId = d.SshSessionId,
        Type = d.Type,
        BindAddress = d.BindAddress,
        ListenPort = d.ListenPort,
        DestinationHost = d.DestinationHost,
        DestinationPort = d.DestinationPort,
        AutoStart = d.AutoStart,
        ReconnectWithSsh = d.ReconnectWithSsh,
        AllowLan = d.AllowLan,
        CreatedUtc = d.CreatedUtc,
        ModifiedUtc = d.ModifiedUtc
    };

    private void SetError(string msg) { ErrorBanner = msg; HasError = true; }
    private void ClearError() { ErrorBanner = null; HasError = false; }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB"
    };
}

public partial class PortForwardRow : ObservableObject
{
    public required PortForwardDefinition Definition { get; init; }
    public string Name { get; set; } = string.Empty;
    public string TypeLabel { get; set; } = string.Empty;
    public string BindAddress { get; set; } = string.Empty;
    public int ListenPort { get; set; }
    public string RemoteHost { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public string SessionName { get; set; } = string.Empty;

    [ObservableProperty] private PortForwardRuntimeState _state;
    [ObservableProperty] private string _stateLabel = "○ Stopped";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _traffic = "—";
    [ObservableProperty] private int _connections;
    [ObservableProperty] private string _sentDisplay = "N/A";
    [ObservableProperty] private string _receivedDisplay = "N/A";
}

public partial class PortForwardEditorViewModel : ObservableObject
{
    public PortForwardEditorViewModel(PortForwardDefinition def, IReadOnlyList<SshSessionRecord> sessions)
    {
        Id = def.Id;
        Name = def.Name;
        Type = def.Type;
        BindAddress = def.BindAddress;
        ListenPort = def.ListenPort;
        DestinationHost = def.DestinationHost ?? "127.0.0.1";
        DestinationPort = def.DestinationPort;
        AutoStart = def.AutoStart;
        ReconnectWithSsh = def.ReconnectWithSsh;
        AllowLan = def.AllowLan;
        Sessions = sessions;
        SelectedSession = sessions.FirstOrDefault(s => s.Id == def.SshSessionId) ?? sessions.FirstOrDefault();
        Types = Enum.GetValues<SshForwardType>();
    }

    public Guid Id { get; }
    public IReadOnlyList<SshSessionRecord> Sessions { get; }
    public SshForwardType[] Types { get; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private SshSessionRecord? _selectedSession;
    [ObservableProperty] private SshForwardType _type;
    [ObservableProperty] private string _bindAddress = "127.0.0.1";
    [ObservableProperty] private int _listenPort = 8080;
    [ObservableProperty] private string _destinationHost = "127.0.0.1";
    [ObservableProperty] private int _destinationPort = 80;
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private bool _reconnectWithSsh = true;
    [ObservableProperty] private bool _allowLan;
    [ObservableProperty] private string? _validationError;

    public bool IsDynamic => Type == SshForwardType.Dynamic;
    public bool ShowDestination => Type != SshForwardType.Dynamic;

    partial void OnTypeChanged(SshForwardType value)
    {
        OnPropertyChanged(nameof(IsDynamic));
        OnPropertyChanged(nameof(ShowDestination));
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) { ValidationError = "Name is required."; return false; }
        if (SelectedSession is null) { ValidationError = "SSH session is required."; return false; }
        if (ListenPort is < 1 or > 65535) { ValidationError = "Listen port must be 1–65535."; return false; }
        if (Type != SshForwardType.Dynamic && DestinationPort is < 1 or > 65535)
        {
            ValidationError = "Destination port must be 1–65535.";
            return false;
        }

        ValidationError = null;
        return true;
    }

    public PortForwardDefinition ToDefinition() => new()
    {
        Id = Id,
        Name = Name.Trim(),
        SshSessionId = SelectedSession!.Id,
        Type = Type,
        BindAddress = string.IsNullOrWhiteSpace(BindAddress) ? "127.0.0.1" : BindAddress.Trim(),
        ListenPort = ListenPort,
        DestinationHost = DestinationHost,
        DestinationPort = DestinationPort,
        AutoStart = AutoStart,
        ReconnectWithSsh = ReconnectWithSsh,
        AllowLan = AllowLan,
        ModifiedUtc = DateTimeOffset.UtcNow
    };
}
