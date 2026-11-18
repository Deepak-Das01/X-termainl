using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using XTerminal.App.Services;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Security;
using XTerminal.Core.Services;
using XTerminal.Core.Settings;
using XTerminal.Protocols;
using XTerminal.Protocols.Ssh;

namespace XTerminal.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISessionStore _sessionStore;
    private readonly ISettingsStore _settingsStore;
    private readonly SessionHostFactory _hostFactory;
    private readonly RemoteEditorFileService _remoteEditor = new();
    private readonly IWorkspaceLayoutStore _workspaceLayout = new JsonWorkspaceLayoutStore();
    private readonly IWorkspaceProfileStore _workspaceProfiles = new JsonWorkspaceProfileStore();
    private CancellationTokenSource? _workspaceSaveCts;
    private bool _suppressPersist;
    private string? _lastWorkspaceRecoveryNote;

    [ObservableProperty] private IWorkspaceTab? _selectedTab;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ActivitySection _activeSection = ActivitySection.Terminal;
    [ObservableProperty] private bool _isUtilityPanelOpen;
    [ObservableProperty] private bool _isActivityBarExpanded;
    [ObservableProperty] private AppThemeMode _themeMode = AppThemeMode.Dark;
    [ObservableProperty] private WindowMaterialMode _windowMaterial = WindowMaterialMode.Mica;
    [ObservableProperty] private string _fontFamily = "Cascadia Mono";
    [ObservableProperty] private double _fontSize = 13;
    [ObservableProperty] private string _schemeId = "xterminal-dark";
    [ObservableProperty] private CursorStyleMode _cursorStyle = CursorStyleMode.Bar;
    [ObservableProperty] private bool _cursorBlink = true;
    [ObservableProperty] private string _settingsCategory = "General";
    [ObservableProperty] private KnownHostRow? _selectedKnownHost;
    [ObservableProperty] private string _statusShell = "Ready";
    [ObservableProperty] private string _statusConnection = "Idle";
    [ObservableProperty] private string _statusEncoding = "UTF-8";
    [ObservableProperty] private string _statusTerminalType = "xterm-256color";
    [ObservableProperty] private string _statusDirectory = Environment.CurrentDirectory;
    [ObservableProperty] private string _statusHost = "—";
    [ObservableProperty] private string _statusUser = "—";
    [ObservableProperty] private string _statusLanguage = "—";
    [ObservableProperty] private string _statusCursor = "—";
    [ObservableProperty] private string _editorFontFamily = "Cascadia Code";
    [ObservableProperty] private double _editorFontSize = 14;
    [ObservableProperty] private double _editorLineHeight = 1.45;
    [ObservableProperty] private bool _editorWordWrap;
    [ObservableProperty] private bool _editorMinimap = true;
    [ObservableProperty] private int _editorTabSize = 4;
    [ObservableProperty] private bool _editorInsertSpaces = true;
    [ObservableProperty] private bool _editorAutoSave;
    [ObservableProperty] private bool _editorRenderWhitespace;

    public event Action? AppearanceChanged;
    public event Action? ThemeChanged;
    public event Action? EditorAppearanceChanged;

    public ObservableCollection<IWorkspaceTab> Tabs { get; } = new();
    public ObservableCollection<SessionGroupViewModel> SessionGroups { get; } = new();
    public ObservableCollection<ActivityItemViewModel> Activities { get; } = new();
    public ObservableCollection<SchemeTileViewModel> SchemeTiles { get; } = new();
    public ObservableCollection<string> FontFamilies { get; } = new();
    public ObservableCollection<string> SettingsCategories { get; } =
    [
        "General", "Appearance", "Terminal", "Profiles", "SSH", "Editor", "Files",
        "History", "Keybindings", "Security", "Advanced", "About"
    ];

    public AppSettings CurrentSettings { get; private set; } = new();
    public ObservableCollection<KeybindingRow> KeybindingRows { get; } = new();
    public ObservableCollection<KeybindingRow> FilteredKeybindingRows { get; } = new();
    public ObservableCollection<ShellProfileSettings> ShellProfiles { get; } = new();
    public ObservableCollection<KnownHostRow> KnownHosts { get; } = new();
    public KeybindingService? Keybindings { get; private set; }

    [ObservableProperty] private KeybindingRow? _selectedKeybinding;
    [ObservableProperty] private string _keybindingFilter = "";
    [ObservableProperty] private string _keybindingRecordHint = "";
    [ObservableProperty] private bool _isRecordingKeybinding;
    public string AboutVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    public string AboutRuntime => $"{Environment.OSVersion} · .NET {Environment.Version}";
    public string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XTerminal");

    [ObservableProperty] private bool _historyEnabled = true;
    [ObservableProperty] private int _historyRetentionDays = 30;
    [ObservableProperty] private int _historyMaxEntries = 10_000;
    [ObservableProperty] private bool _historyExcludeSsh;
    [ObservableProperty] private string _historyExcludePatternsText = string.Empty;

    public SessionsViewModel Sessions { get; }
    public SftpWorkspaceViewModel Sftp { get; }
    public FilesExplorerViewModel Files { get; }
    public PortForwardingViewModel Ports { get; }
    public SnippetsViewModel Snippets { get; }
    public HistoryViewModel History { get; }
    public CommandHistoryTracker HistoryTracker { get; }
    public TerminalGroupTabViewModel? SelectedTerminalGroup => SelectedTab as TerminalGroupTabViewModel;
    public TerminalTabViewModel? SelectedTerminalTab => SelectedTerminalGroup?.ActivePane;
    public EditorTabViewModel? SelectedEditorTab => SelectedTab as EditorTabViewModel;
    public bool IsTerminalTabSelected => SelectedTab is TerminalGroupTabViewModel;
    public bool IsEditorTabSelected => SelectedTab is EditorTabViewModel;

    public ObservableCollection<ClosedTabRecord> RecentlyClosed { get; } = new();
    public NotificationService Notifications { get; } = new();
    public AppCommandRegistry Commands { get; } = new();

    [ObservableProperty] private bool _isCommandPaletteOpen;
    [ObservableProperty] private bool _isQuickSwitcherOpen;
    [ObservableProperty] private string _paletteQuery = string.Empty;
    [ObservableProperty] private string _switcherQuery = string.Empty;
    [ObservableProperty] private string _zoomToast = string.Empty;
    [ObservableProperty] private bool _showZoomToast;

    public IReadOnlyList<ProfileItem> Profiles { get; } =
    [
        new() { Name = "Windows PowerShell", Subtitle = "powershell.exe", Glyph = "❯", Accent = "#5391FE", Shell = ShellKind.PowerShell },
        new() { Name = "Command Prompt", Subtitle = "cmd.exe", Glyph = "▣", Accent = "#F0C000", Shell = ShellKind.Cmd },
        new() { Name = "Ubuntu (WSL)", Subtitle = "wsl.exe", Glyph = "🐧", Accent = "#DD4814", Shell = ShellKind.Wsl }
    ];

    public bool IsSettingsView => ActiveSection == ActivitySection.Settings;
    public bool IsTerminalView => ActiveSection == ActivitySection.Terminal;
    public bool IsSessionsView => ActiveSection == ActivitySection.Sessions;
    public bool IsSftpView => ActiveSection == ActivitySection.Sftp;
    public bool IsFilesView => ActiveSection == ActivitySection.Files;
    public bool IsPortsView => ActiveSection == ActivitySection.PortForwarding;
    public bool IsSnippetsView => ActiveSection == ActivitySection.Snippets;
    public bool IsHistoryView => ActiveSection == ActivitySection.History;
    public bool IsComingSoonView => false;
    public bool ShowCancelConnect =>
        IsTerminalView && IsTerminalTabSelected &&
        SelectedTerminalTab?.ConnectionState is ConnectionState.Connecting or ConnectionState.Reconnecting;
    public bool ShowReconnect =>
        IsTerminalView && IsTerminalTabSelected &&
        SelectedTerminalTab?.ConnectionState is ConnectionState.Failed or ConnectionState.Disconnected
            or ConnectionState.Cancelled;
    public AppSettings SnapshotSettings() => BuildSettings();

    public MainViewModel(
        ISessionStore sessionStore,
        ISettingsStore settingsStore,
        SessionHostFactory hostFactory,
        SessionsViewModel sessionsViewModel,
        SftpWorkspaceViewModel sftpWorkspace,
        FilesExplorerViewModel filesExplorer,
        PortForwardingViewModel ports,
        SnippetsViewModel snippets,
        HistoryViewModel history)
    {
        _sessionStore = sessionStore;
        _settingsStore = settingsStore;
        _hostFactory = hostFactory;
        Sessions = sessionsViewModel;
        Sftp = sftpWorkspace;
        Files = filesExplorer;
        Ports = ports;
        Snippets = snippets;
        History = history;
        HistoryTracker = new CommandHistoryTracker(history);
        Sftp.AttachNotifications(Notifications);
        Sessions.ConnectRequested += s => _ = ConnectSshAsync(s);
        Sessions.SftpRequested += s => _ = OpenSftpAsync(s);
        Sftp.OpenInEditorRequested += (path, _, _) => _ = OpenRemoteInEditorAsync(path);
        Sftp.PickSessionRequested += OnPickSftpSession;
        Files.OpenFileRequested += path => _ = OpenLocalPathAsync(path);
        Files.OpenTerminalRequested += (dir, shell) => _ = OpenTerminalAtAsync(dir, shell);
        Snippets.ApplySnippetRequested += OnApplySnippet;
        History.ApplyCommandRequested += OnApplySnippet;
        History.CreateSnippetRequested += cmd => _ = Snippets.AddFromHistoryAsync(cmd);
        Ports.EditSessionRequested += record => Sessions.EditSessionCommand.Execute(record);
        BuildActivities();
        BuildFonts();
        BuildSchemes();
        RegisterCommands();
    }

    public async Task InitializeAsync()
    {
        _suppressPersist = true;
        var settings = await _settingsStore.LoadAsync();
        ApplySettingsToUi(settings);
        _suppressPersist = false;

#if DEBUG
        var sw = System.Diagnostics.Stopwatch.StartNew();
        AppLog.Info(LogCategory.App, "Startup: begin store load");
#endif
        // Do not serialize store I/O — terminal becomes usable as soon as shell profile opens.
        var sessionsTask = _sessionStore.LoadAsync();
        var loadSide = Task.WhenAll(
            Sessions.LoadAsync(),
            Files.InitializeAsync(),
            Ports.InitializeAsync(),
            Snippets.InitializeAsync(),
            History.InitializeAsync(settings.History));

        var sessions = await sessionsTask;
        RebuildGroups(sessions);

        var restored = false;
        if (settings.Advanced.RestoreWorkspaceLayout)
            restored = await TryRestoreWorkspaceAsync(settings);

        if (!restored && settings.General.OpenDefaultShellOnStartup)
        {
            var preferredShell = ResolveDefaultShellKind(settings);
            var local = sessions.FirstOrDefault(s => s.Kind == SessionKind.LocalShell && s.Shell == preferredShell)
                        ?? sessions.FirstOrDefault(s => s.Kind == SessionKind.LocalShell);
            if (local is not null)
                await OpenSessionCoreAsync(local);
            else
                await OpenSessionCoreAsync(new SessionDefinition
                {
                    Name = preferredShell.ToString(),
                    Kind = SessionKind.LocalShell,
                    Shell = preferredShell,
                    Group = "Local"
                });
        }

        if (settings.General.RestoreLastActivity)
        {
            try
            {
                var ui = await new JsonUiStateStore().LoadAsync();
                var name = ui.LastActivitySection ?? settings.General.DefaultWorkspace;
                if (Enum.TryParse<ActivitySection>(name, true, out var section) &&
                    section != ActivitySection.Settings)
                {
                    ActiveSection = section;
                    RefreshActivitySelection();
                }
            }
            catch { /* ignore */ }
        }

        await loadSide;
#if DEBUG
        AppLog.Info(LogCategory.App, $"Startup: stores+shell ready in {sw.ElapsedMilliseconds} ms");
#endif

        foreach (var row in Ports.Forwards.Where(r => r.Definition.AutoStart).ToList())
            _ = Ports.StartCommand.ExecuteAsync(row);

        ThemeChanged?.Invoke();
        AppearanceChanged?.Invoke();
        EditorAppearanceChanged?.Invoke();
        UpdateStatus();
    }

    public async Task OpenTerminalAtAsync(string directory, ShellKind shell)
    {
        ActiveSection = ActivitySection.Terminal;
        RefreshActivitySelection();
        await OpenSessionCoreAsync(new SessionDefinition
        {
            Name = shell.ToString(),
            Kind = SessionKind.LocalShell,
            Shell = shell,
            Group = "Local",
            WorkingDirectory = directory
        });
    }

    private void OnApplySnippet(string text, bool run)
    {
        if (SelectedTerminalTab is null)
        {
            _ = NewPowerShellCommand.ExecuteAsync(null);
        }

        // Defer until tab may exist
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (SelectedTerminalTab is null) return;
            ActiveSection = ActivitySection.Terminal;
            RefreshActivitySelection();
            var payload = run ? text + "\r" : text;
            SelectedTerminalTab.WriteInput(payload);
        });
    }

    [RelayCommand]
    private void SelectActivity(ActivityItemViewModel? item)
    {
        if (item is null || !item.IsEnabled) return;

        if (ActiveSection == ActivitySection.Settings)
            _ = PersistAsync();

        if (item.Section == ActivitySection.Editor)
        {
            ActiveSection = ActivitySection.Terminal;
            foreach (var a in Activities) a.IsSelected = a.Section == ActivitySection.Terminal;
            NotifyViewFlags();
            if (SelectedEditorTab is null)
            {
                NewUntitledEditor();
            }
            else
            {
                SelectedTab = SelectedEditorTab;
            }

            return;
        }

        ActiveSection = item.Section;
        foreach (var a in Activities) a.IsSelected = a.Section == item.Section;
        NotifyViewFlags();

        if (item.Section == ActivitySection.Sftp && !Sftp.IsConnected)
        {
            var record = ResolveSshRecordFromTabs() ?? Sessions.SelectedSession;
            if (record is not null) _ = Sftp.OpenSessionAsync(record);
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        ActiveSection = ActivitySection.Settings;
        foreach (var a in Activities) a.IsSelected = false;
        SettingsCategory = "General";
        NotifyViewFlags();
    }

    [RelayCommand]
    private async Task ConnectSshAsync(SshSessionRecord? record)
    {
        if (record is null) return;
        ActiveSection = ActivitySection.Terminal;
        RefreshActivitySelection();
        await OpenSessionCoreAsync(new SessionDefinition
        {
            Name = record.Name,
            Kind = SessionKind.Ssh,
            Host = record.Host,
            Port = record.Port,
            Username = record.Username,
            Group = "Remote",
            SshSessionId = record.Id,
            WorkingDirectory = record.StartupDirectory
        });
    }

    [RelayCommand]
    private async Task OpenSftpAsync(SshSessionRecord? record)
    {
        record ??= ResolveSshRecordFromTabs();
        if (record is null)
        {
            ActiveSection = ActivitySection.Sessions;
            RefreshActivitySelection();
            return;
        }

        ActiveSection = ActivitySection.Sftp;
        RefreshActivitySelection();
        IsUtilityPanelOpen = true;
        await Sftp.OpenSessionAsync(record);
        UpdateStatus();
    }

    private void OnPickSftpSession()
    {
        ActiveSection = ActivitySection.Sessions;
        RefreshActivitySelection();
    }

    private SshSessionRecord? ResolveSshRecordFromTabs()
    {
        var id = SelectedTerminalTab?.Definition.SshSessionId
                 ?? Tabs.OfType<TerminalGroupTabViewModel>()
                     .Select(g => g.ActivePane)
                     .FirstOrDefault(t => t?.Definition.Kind == SessionKind.Ssh && t.Definition.SshSessionId is not null)
                     ?.Definition.SshSessionId;
        return id is null ? null : Sessions.FindSession(id.Value);
    }

    [RelayCommand]
    private void ToggleUtilityPanel()
    {
        IsUtilityPanelOpen = !IsUtilityPanelOpen;
        _ = PersistAsync();
    }

    [RelayCommand]
    private void ToggleActivityBar()
    {
        IsActivityBarExpanded = !IsActivityBarExpanded;
        _ = PersistAsync();
    }

    [RelayCommand]
    private void SelectSettingsCategory(string? category)
    {
        if (!string.IsNullOrWhiteSpace(category)) SettingsCategory = category;
    }

    [RelayCommand] private void DecreaseFontSize() => FontSize = Math.Max(8, FontSize - 1);
    [RelayCommand] private void IncreaseFontSize() => FontSize = Math.Min(32, FontSize + 1);

    [RelayCommand]
    private void SelectScheme(SchemeTileViewModel? tile)
    {
        if (tile is null) return;
        SchemeId = tile.Scheme.Id;
        foreach (var s in SchemeTiles) s.IsSelected = s.Scheme.Id == SchemeId;
    }

    [RelayCommand]
    private async Task OpenSessionAsync(SessionDefinition? session)
    {
        if (session is null) return;
        ActiveSection = ActivitySection.Terminal;
        RefreshActivitySelection();
        await OpenSessionCoreAsync(session);
    }

    [RelayCommand]
    private async Task OpenProfileAsync(ProfileItem? profile)
    {
        if (profile is null) return;
        ActiveSection = ActivitySection.Terminal;
        RefreshActivitySelection();
        await OpenSessionCoreAsync(new SessionDefinition
        {
            Name = profile.Name,
            Kind = SessionKind.LocalShell,
            Shell = profile.Shell,
            Group = "Local"
        });
    }

    [RelayCommand] private async Task NewPowerShellAsync() => await OpenProfileAsync(Profiles[0]);
    [RelayCommand] private async Task NewCmdAsync() => await OpenProfileAsync(Profiles[1]);
    [RelayCommand] private async Task NewWslAsync() => await OpenProfileAsync(Profiles[2]);

    [RelayCommand]
    private void NewUntitledEditor()
    {
        ActiveSection = ActivitySection.Terminal;
        RefreshActivitySelection();
        AddEditorTab(EditorTabViewModel.CreateUntitled());
    }

    [RelayCommand]
    private async Task OpenLocalFileAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open file",
            Filter = "All files|*.*|Text|*.txt;*.md;*.json;*.xml;*.yml;*.yaml;*.ps1;*.py;*.js;*.ts;*.html;*.css;*.sql;*.ini"
        };
        if (dlg.ShowDialog() != true) return;
        await OpenLocalPathAsync(dlg.FileName);
    }

    [RelayCommand]
    private async Task SaveEditorAsync()
    {
        if (SelectedEditorTab is null) return;
        await SaveEditorCoreAsync(SelectedEditorTab, saveAs: false);
    }

    [RelayCommand]
    private async Task SaveEditorAsAsync()
    {
        if (SelectedEditorTab is null) return;
        await SaveEditorCoreAsync(SelectedEditorTab, saveAs: true);
    }

    [RelayCommand]
    private async Task CloseTabAsync(IWorkspaceTab? tab)
    {
        tab ??= SelectedTab;
        if (tab is null) return;
        if (tab.IsPinned)
        {
            Notifications.Show("Pinned tab", "Unpin the tab before closing, or use Close on the tab menu after unpinning.");
            return;
        }

        if (tab is EditorTabViewModel editor && editor.IsDirty)
        {
            var result = MessageBox.Show(
                $"'{editor.Title.TrimStart('•', ' ')}' has unsaved changes.\n\nSave before closing?",
                "Unsaved changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel) return;
            if (result == MessageBoxResult.Yes)
            {
                var saved = await SaveEditorCoreAsync(editor, saveAs: false);
                if (!saved) return;
            }
        }

        RememberClosed(tab);
        if (tab is TerminalGroupTabViewModel g)
        {
            g.SetBroadcast(false);
            foreach (var pane in g.AllPanes)
                HistoryTracker.ForgetTab(pane.Id);
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        switch (tab)
        {
            case TerminalGroupTabViewModel group:
                await group.DisposeAsync();
                break;
            case EditorTabViewModel ed:
                if (ed.Remote is not null) _remoteEditor.Cleanup(ed.Remote);
                await ed.DisposeAsync();
                break;
        }

        SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        UpdateStatus();
        NotifyTabSelection();
    }

    private void RememberClosed(IWorkspaceTab tab)
    {
        ClosedTabRecord? record = tab switch
        {
            TerminalGroupTabViewModel g when g.ActivePane?.Definition.Kind == SessionKind.Ssh => new()
            {
                Kind = ClosedTabKind.Ssh,
                Title = g.Title,
                Glyph = g.Glyph,
                Session = Clone(g.ActivePane.Definition),
                SshSessionId = g.ActivePane.Definition.SshSessionId
            },
            TerminalGroupTabViewModel g => new()
            {
                Kind = ClosedTabKind.LocalShell,
                Title = g.Title,
                Glyph = g.Glyph,
                Session = g.ActivePane is null ? null : Clone(g.ActivePane.Definition)
            },
            EditorTabViewModel e when e.Kind == EditorDocumentKind.Local && e.LocalPath is not null => new()
            {
                Kind = ClosedTabKind.EditorLocal,
                Title = e.Title,
                Glyph = e.Glyph,
                LocalPath = e.LocalPath
            },
            _ => null
        };
        if (record is null) return;
        RecentlyClosed.Insert(0, record);
        while (RecentlyClosed.Count > 10) RecentlyClosed.RemoveAt(RecentlyClosed.Count - 1);
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        FlushNestedSettings();
        await _settingsStore.SaveAsync(BuildSettings());
        Notifications.Show("Settings", "Settings saved.");
    }

    /// <summary>Persists nested CurrentSettings graph changed via Settings bindings.</summary>
    public void FlushNestedSettings()
    {
        Files.ApplyFilesSettings(CurrentSettings.Files);
        Sftp.ApplyFilesSettings(CurrentSettings.Files);
        foreach (var group in Tabs.OfType<TerminalGroupTabViewModel>())
            group.SecuritySettings = CurrentSettings.Security;
        Notifications.Enabled = CurrentSettings.General.ShowToastNotifications;
        AppLog.Configure(CurrentSettings.Advanced.EnableFileLogging, CurrentSettings.Advanced.MaxLogFiles);
        _ = PersistAsync();
    }

    partial void OnSelectedTabChanged(IWorkspaceTab? oldValue, IWorkspaceTab? newValue)
    {
        if (oldValue is not null) oldValue.IsActive = false;
        if (newValue is not null)
        {
            newValue.IsActive = true;
            if (newValue.IsEditor) ActiveSection = ActivitySection.Terminal;
        }

        NotifyTabSelection();
        UpdateStatus();
    }

    partial void OnSearchTextChanged(string value) => FilterGroups(value);

    partial void OnActiveSectionChanged(ActivitySection value)
    {
        if (_suppressPersist || value == ActivitySection.Settings) return;
        _ = PersistLastActivityAsync(value);
    }

    private static async Task PersistLastActivityAsync(ActivitySection section)
    {
        try
        {
            var store = new JsonUiStateStore();
            var ui = await store.LoadAsync();
            ui.LastActivitySection = section.ToString();
            await store.SaveAsync(ui);
        }
        catch { /* ignore */ }
    }
    partial void OnThemeModeChanged(AppThemeMode value) { ThemeChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnWindowMaterialChanged(WindowMaterialMode value) { ThemeChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnFontFamilyChanged(string value) { AppearanceChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnFontSizeChanged(double value) { AppearanceChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnSchemeIdChanged(string value) { AppearanceChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnCursorStyleChanged(CursorStyleMode value) { AppearanceChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnCursorBlinkChanged(bool value) { AppearanceChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnIsUtilityPanelOpenChanged(bool value) => _ = PersistAsync();
    partial void OnIsActivityBarExpandedChanged(bool value) => _ = PersistAsync();
    partial void OnEditorFontFamilyChanged(string value) { EditorAppearanceChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnEditorFontSizeChanged(double value) { EditorAppearanceChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnEditorLineHeightChanged(double value) { EditorAppearanceChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnEditorWordWrapChanged(bool value) { EditorAppearanceChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnEditorMinimapChanged(bool value) { EditorAppearanceChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnEditorTabSizeChanged(int value) { EditorAppearanceChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnEditorInsertSpacesChanged(bool value) { EditorAppearanceChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnEditorAutoSaveChanged(bool value) => _ = PersistAsync();
    partial void OnEditorRenderWhitespaceChanged(bool value) { EditorAppearanceChanged?.Invoke(); _ = PersistAsync(); }
    partial void OnHistoryEnabledChanged(bool value) { History.ApplySettings(BuildSettings().History); _ = PersistAsync(); }
    partial void OnHistoryRetentionDaysChanged(int value) { History.ApplySettings(BuildSettings().History); _ = PersistAsync(); }
    partial void OnHistoryMaxEntriesChanged(int value) { History.ApplySettings(BuildSettings().History); _ = PersistAsync(); }
    partial void OnHistoryExcludeSshChanged(bool value) { History.ApplySettings(BuildSettings().History); _ = PersistAsync(); }
    partial void OnHistoryExcludePatternsTextChanged(string value) { History.ApplySettings(BuildSettings().History); _ = PersistAsync(); }

    private async Task OpenSessionCoreAsync(SessionDefinition session, bool startHosts = true)
    {
        if (!SessionHostFactory.IsTerminalSessionSupported(session.Kind))
        {
            var label = session.Kind switch
            {
                SessionKind.Sftp => "SFTP",
                _ => session.Kind.ToString()
            };
            MessageBox.Show(
                session.Kind == SessionKind.Sftp
                    ? "SFTP is available from the SFTP activity in the sidebar. Terminal SFTP tabs are not used."
                    : $"{label} is not supported in this release. Local shells and SSH remain available.",
                "Unsupported protocol",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var group = new TerminalGroupTabViewModel(Clone(session), _hostFactory)
        {
            SecuritySettings = CurrentSettings.Security
        };
        foreach (var pane in group.AllPanes)
        {
            AttachShellIntegration(pane);
            if (!startHosts && session.Kind == SessionKind.Ssh)
            {
                pane.IsPlaceholder = true;
                pane.Status = "Restored · disconnected (reconnect to connect)";
            }
        }
        group.StatusChanged += UpdateStatus;
        group.LayoutChanged += () => ScheduleWorkspaceSave();
        InsertTabOrdered(group);
        SelectedTab = group;
        if (startHosts)
            await group.StartAsync();
        UpdateStatus();
        NotifyTabSelection();
        ScheduleWorkspaceSave();
    }

    private void InsertTabOrdered(IWorkspaceTab tab)
    {
        if (tab.IsPinned)
        {
            var lastPinned = Tabs.TakeWhile(t => t.IsPinned).Count();
            Tabs.Insert(lastPinned, tab);
        }
        else
        {
            Tabs.Add(tab);
        }
    }

    private void AddEditorTab(EditorTabViewModel tab)
    {
        tab.MetaChanged += UpdateStatus;
        tab.AutoSaveRequested += OnEditorAutoSaveAsync;
        InsertTabOrdered(tab);
        SelectedTab = tab;
        UpdateStatus();
        NotifyTabSelection();
    }

    public async Task OpenLocalPathAsync(string path)
    {
        var existing = Tabs.OfType<EditorTabViewModel>()
            .FirstOrDefault(t => t.Kind == EditorDocumentKind.Local &&
                                 string.Equals(t.LocalPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedTab = existing;
            ActiveSection = ActivitySection.Terminal;
            RefreshActivitySelection();
            return;
        }

        var content = await File.ReadAllTextAsync(path, Encoding.UTF8);
        ActiveSection = ActivitySection.Terminal;
        RefreshActivitySelection();
        AddEditorTab(EditorTabViewModel.CreateLocal(path, content));
    }

    public async Task OpenRemoteInEditorAsync(string remotePath)
    {
        try
        {
            if (!Sftp.IsConnected)
            {
                MessageBox.Show("Connect SFTP before opening remote files.", "Editor",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var existing = Tabs.OfType<EditorTabViewModel>()
                .FirstOrDefault(t => t.Remote is not null &&
                                     string.Equals(t.Remote.RemotePath, remotePath, StringComparison.Ordinal));
            if (existing is not null)
            {
                SelectedTab = existing;
                ActiveSection = ActivitySection.Terminal;
                RefreshActivitySelection();
                return;
            }

            _remoteEditor.MaxBytes = Math.Max(64_000, CurrentSettings.Files.MaxEditorFileBytes);
            var session = await _remoteEditor.OpenAsync(Sftp.Service, remotePath);
            // Bind tab to the active SFTP session id/host — never open against a different connection by path alone.
            if (Sftp.ActiveSessionId is Guid sid && session.SessionId != sid)
                throw new InvalidOperationException("Remote editor session mismatch.");
            ActiveSection = ActivitySection.Terminal;
            RefreshActivitySelection();
            AddEditorTab(EditorTabViewModel.CreateRemote(session));
        }
        catch (Exception ex)
        {
            MessageBox.Show(SshErrorMapper.Sanitize(ex.Message), "Open remote file",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OnEditorAutoSaveAsync(EditorTabViewModel tab)
    {
        if (!EditorAutoSave || !tab.IsDirty) return;
        if (tab.Kind == EditorDocumentKind.Untitled) return;
        await SaveEditorCoreAsync(tab, saveAs: false, quiet: true);
    }

    public async Task<bool> SaveEditorCoreAsync(EditorTabViewModel tab, bool saveAs, bool quiet = false)
    {
        try
        {
            if (tab.Kind == EditorDocumentKind.RemoteSftp && tab.Remote is not null && !saveAs)
            {
                try
                {
                    await _remoteEditor.UploadAtomicAsync(Sftp.Service, tab.Remote, tab.Content, overwriteConflict: false);
                    tab.MarkRemoteSaved(tab.Remote);
                    if (!quiet) tab.StatusMessage = "Saved to remote";
                    return true;
                }
                catch (RemoteEditConflictException conflict)
                {
                    var choice = MessageBox.Show(
                        conflict.Message +
                        "\n\nYes = Overwrite remote\nNo = Reload remote (discard local edits)\nCancel = keep editing / save a local copy next",
                        "Remote conflict",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);
                    if (choice == MessageBoxResult.Yes)
                    {
                        await _remoteEditor.UploadAtomicAsync(Sftp.Service, tab.Remote, tab.Content, overwriteConflict: true);
                        tab.MarkRemoteSaved(tab.Remote);
                        return true;
                    }

                    if (choice == MessageBoxResult.No)
                    {
                        await _remoteEditor.ReloadAsync(Sftp.Service, tab.Remote);
                        tab.LoadContent(tab.Remote.Content, markClean: true);
                        tab.StatusMessage = "Reloaded from remote";
                        return false;
                    }

                    var dlg = new SaveFileDialog
                    {
                        Title = "Save a local copy",
                        FileName = Path.GetFileName(tab.Remote.RemotePath),
                        Filter = "All files|*.*"
                    };
                    if (dlg.ShowDialog() == true)
                    {
                        await File.WriteAllTextAsync(dlg.FileName, tab.Content, new UTF8Encoding(false));
                        tab.StatusMessage = "Saved local copy";
                    }

                    return false;
                }
            }

            var path = tab.LocalPath;
            if (saveAs || string.IsNullOrWhiteSpace(path) || tab.Kind == EditorDocumentKind.Untitled)
            {
                var dlg = new SaveFileDialog
                {
                    Title = "Save As",
                    FileName = tab.Title.TrimStart('•', ' '),
                    Filter = "All files|*.*"
                };
                if (dlg.ShowDialog() != true) return false;
                path = dlg.FileName;
            }

            var tmp = path + ".xterminal-tmp";
            await File.WriteAllTextAsync(tmp, tab.Content, new UTF8Encoding(false));
            File.Copy(tmp, path, overwrite: true);
            File.Delete(tmp);
            tab.MarkSaved(path);
            return true;
        }
        catch (Exception ex)
        {
            if (!quiet)
            {
                MessageBox.Show(SshErrorMapper.Sanitize(ex.Message), "Save failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            tab.StatusMessage = "Save failed";
            return false;
        }
    }

    private void UpdateStatus()
    {
        StatusLanguage = "—";
        StatusCursor = "—";

        if (ActiveSection == ActivitySection.Files)
        {
            StatusShell = "Files";
            StatusConnection = "local";
            StatusDirectory = Files.PathText;
            StatusHost = $"{Files.ItemCount} items";
            StatusUser = "—";
            StatusTerminalType = "files";
            StatusEncoding = "—";
            OnPropertyChanged(nameof(ShowCancelConnect));
            OnPropertyChanged(nameof(ShowReconnect));
            return;
        }

        if (ActiveSection == ActivitySection.PortForwarding)
        {
            StatusShell = "Ports";
            StatusConnection = $"{Ports.ActiveCount} active";
            StatusDirectory = $"{Ports.StoppedCount} stopped";
            StatusHost = $"{Ports.ErrorCount} errors";
            StatusUser = "—";
            StatusTerminalType = "tunnels";
            StatusEncoding = "—";
            OnPropertyChanged(nameof(ShowCancelConnect));
            OnPropertyChanged(nameof(ShowReconnect));
            return;
        }

        if (ActiveSection == ActivitySection.Snippets)
        {
            StatusShell = "Snippets";
            StatusConnection = $"{Snippets.FlatSnippets.Count} shown";
            StatusDirectory = "—";
            StatusHost = "local";
            StatusUser = "—";
            StatusTerminalType = "—";
            StatusEncoding = "—";
            OnPropertyChanged(nameof(ShowCancelConnect));
            OnPropertyChanged(nameof(ShowReconnect));
            return;
        }

        if (ActiveSection == ActivitySection.History)
        {
            StatusShell = "History";
            StatusConnection = History.HistoryEnabled ? "enabled" : "disabled";
            StatusDirectory = $"{History.Entries.Count} shown";
            StatusHost = "local";
            StatusUser = "—";
            StatusTerminalType = "—";
            StatusEncoding = "—";
            OnPropertyChanged(nameof(ShowCancelConnect));
            OnPropertyChanged(nameof(ShowReconnect));
            return;
        }

        if (ActiveSection == ActivitySection.Sftp && Sftp.IsConnected)
        {
            StatusShell = Sftp.SessionLabel;
            StatusConnection = Sftp.StatusText;
            StatusDirectory = Sftp.PathText;
            StatusHost = Sftp.RemoteHost ?? "—";
            StatusUser = Sftp.RemoteUser ?? "—";
            StatusTerminalType = "sftp";
            StatusEncoding = "UTF-8";
            OnPropertyChanged(nameof(ShowCancelConnect));
            OnPropertyChanged(nameof(ShowReconnect));
            return;
        }

        if (SelectedTab is null)
        {
            StatusShell = "No session";
            StatusConnection = "Idle";
            StatusDirectory = Environment.CurrentDirectory;
            StatusHost = "—";
            StatusUser = "—";
            StatusTerminalType = "—";
            OnPropertyChanged(nameof(ShowCancelConnect));
            OnPropertyChanged(nameof(ShowReconnect));
            return;
        }

        if (SelectedTab is EditorTabViewModel editor)
        {
            StatusShell = editor.Title;
            StatusConnection = editor.IsDirty ? "Unsaved" : editor.StatusMessage;
            StatusDirectory = editor.DisplayPath;
            StatusEncoding = editor.Encoding;
            StatusLanguage = editor.DisplayLanguage;
            StatusCursor = $"Ln {editor.CursorLine}, Col {editor.CursorColumn}";
            StatusTerminalType = "editor";
            StatusHost = editor.Kind == EditorDocumentKind.RemoteSftp ? (editor.Remote?.SessionName ?? "remote") : "local";
            StatusUser = editor.Kind == EditorDocumentKind.RemoteSftp ? (Sftp.RemoteUser ?? "—") : Environment.UserName;
            OnPropertyChanged(nameof(ShowCancelConnect));
            OnPropertyChanged(nameof(ShowReconnect));
            return;
        }

        if (SelectedTab is TerminalGroupTabViewModel group && group.ActivePane is { } terminal)
        {
            StatusShell = terminal.Title;
            StatusConnection = terminal.Status;
            if (CurrentSettings.Advanced.ShowCommandExecutionStatus && terminal.LastExitCode is int code)
                StatusConnection = $"{StatusConnection} · exit {code}";
            if (CurrentSettings.Advanced.ShowCommandDuration &&
                !string.IsNullOrWhiteSpace(terminal.LastCommandDurationLabel))
                StatusConnection = $"{StatusConnection} · {terminal.LastCommandDurationLabel}";
            StatusDirectory = terminal.TrackedWorkingDirectory
                              ?? terminal.Definition.WorkingDirectory
                              ?? Environment.CurrentDirectory;
            if (CurrentSettings.Advanced.EnableShellIntegration)
                StatusShell = $"{terminal.Title} · {terminal.ShellIntegrationStatus}";
            StatusEncoding = "UTF-8";
            StatusTerminalType = terminal.Definition.Kind == SessionKind.Ssh ? "ssh / xterm-256color" : "xterm-256color";
            StatusHost = terminal.RemoteHost ?? terminal.Definition.Host ?? "local";
            StatusUser = terminal.RemoteUser ?? terminal.Definition.Username ?? Environment.UserName;
            if (!string.IsNullOrWhiteSpace(terminal.AuthMethodLabel))
                StatusUser = $"{StatusUser} · {terminal.AuthMethodLabel}";
            if (!string.IsNullOrWhiteSpace(terminal.ConnectionDurationLabel))
                StatusConnection = $"{StatusConnection} · {terminal.ConnectionDurationLabel}";
            if (terminal.LastExitCode is int exit)
                StatusTerminalType = $"{StatusTerminalType} · exit {exit}";
            if (group.PaneCount > 1)
                StatusTerminalType = $"Pane {group.PaneIndex}/{group.PaneCount}";
            if (group.BroadcastEnabled)
                StatusConnection = $"Broadcast: {group.BroadcastCount}";
            if (terminal.PrivateInputMode)
                StatusUser = "🔒 Private";
            if (terminal.LatencyMs is int ms)
                StatusHost = $"{StatusHost} · {ms} ms";
        }

        OnPropertyChanged(nameof(ShowCancelConnect));
        OnPropertyChanged(nameof(ShowReconnect));
    }

    private void ScheduleWorkspaceSave()
    {
        if (!CurrentSettings.Advanced.RestoreWorkspaceLayout) return;
        _workspaceSaveCts?.Cancel();
        _workspaceSaveCts = new CancellationTokenSource();
        var token = _workspaceSaveCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, token);
                await SaveWorkspaceLayoutAsync();
            }
            catch (OperationCanceledException) { /* debounced */ }
        }, token);
    }

    public Task FlushWorkspaceLayoutAsync() => SaveWorkspaceLayoutAsync();

    private async Task SaveWorkspaceLayoutAsync()
    {
        if (!CurrentSettings.Advanced.RestoreWorkspaceLayout) return;
        try
        {
            var doc = BuildWorkspaceDocumentFromUi("Previous session");
            await _workspaceLayout.SaveAsync(doc);
        }
        catch
        {
            // never block UX on layout persistence
        }
    }

    private Core.Models.Ui.WorkspaceLayoutDocument BuildWorkspaceDocumentFromUi(string name)
    {
        var doc = new Core.Models.Ui.WorkspaceLayoutDocument
        {
            Version = Core.Models.Ui.WorkspaceLayoutDocument.CurrentSchemaVersion,
            WorkspaceName = name,
            RestoreEnabled = CurrentSettings.Advanced.RestoreWorkspaceLayout,
            AutoReconnectRemote = CurrentSettings.Advanced.AutoReconnectRestoredSessions,
            ActiveSection = ActiveSection.ToString(),
            LocalFilesPath = Files.PathText,
            ActivityBarExpanded = IsActivityBarExpanded,
            UtilityPanelOpen = IsUtilityPanelOpen,
            ActiveTabId = SelectedTab is TerminalGroupTabViewModel tg ? tg.Id
                : SelectedTab is EditorTabViewModel et ? et.Id
                : null
        };

        foreach (var tab in Tabs)
        {
            if (tab is TerminalGroupTabViewModel g)
            {
                var pane = g.ActivePane;
                doc.Tabs.Add(new Core.Models.Ui.WorkspaceTabLayout
                {
                    TabId = g.Id,
                    Kind = "terminal",
                    Title = g.Title,
                    IsPinned = g.IsPinned,
                    SshSessionId = pane?.Definition.SshSessionId,
                    LocalShell = pane?.Definition.Kind == SessionKind.LocalShell
                        ? pane.Definition.Shell.ToString()
                        : null,
                    WorkingDirectory = pane?.Definition.WorkingDirectory,
                    ActivePaneId = pane?.Id,
                    SplitRoot = g.ExportSplitLayout()
                });
            }
            else if (tab is EditorTabViewModel ed)
            {
                // Paths and session refs only — never unsaved document contents.
                doc.Tabs.Add(new Core.Models.Ui.WorkspaceTabLayout
                {
                    TabId = ed.Id,
                    Kind = "editor",
                    Title = ed.Title,
                    IsPinned = ed.IsPinned,
                    EditorPath = ed.Kind == EditorDocumentKind.RemoteSftp
                        ? ed.Remote?.RemotePath
                        : ed.LocalPath,
                    EditorIsRemote = ed.Kind == EditorDocumentKind.RemoteSftp,
                    EditorSshSessionId = ed.Remote?.SessionId,
                    EditorLanguage = ed.LanguageId
                });
            }
        }

        return doc;
    }

    /// <summary>
    /// Restores tab metadata only — never terminal buffers or automatic command replay.
    /// SSH reconnect requires explicit AutoReconnectRestoredSessions.
    /// </summary>
    private async Task<bool> TryRestoreWorkspaceAsync(AppSettings settings)
    {
        try
        {
            var loaded = await _workspaceLayout.LoadAsync();
            var validated = WorkspaceLayoutValidator.ValidateAndNormalize(loaded);
            if (!validated.IsValid || validated.Document is null)
            {
                _lastWorkspaceRecoveryNote = validated.Error ?? "Workspace layout invalid; starting clean.";
                return false;
            }

            var doc = validated.Document;
            if (!doc.RestoreEnabled || doc.Tabs.Count == 0)
                return false;

            if (validated.UsedFallback)
                _lastWorkspaceRecoveryNote = "Recovered workspace from last valid backup.";

            var opened = 0;
            TerminalGroupTabViewModel? preferred = null;

            foreach (var tab in doc.Tabs)
            {
                try
                {
                    if (tab.Kind == "editor")
                    {
                        if (tab.EditorIsRemote)
                        {
                            // Remote editors require an active matching SFTP session — skip silently if unavailable.
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(tab.EditorPath) && File.Exists(tab.EditorPath))
                        {
                            await OpenLocalPathAsync(tab.EditorPath);
                            opened++;
                        }

                        continue;
                    }

                    if (tab.Kind != "terminal") continue;

                    var reconnect = settings.Advanced.AutoReconnectRestoredSessions && doc.AutoReconnectRemote;
                    if (await RestoreTerminalTabAsync(tab, reconnect))
                    {
                        opened++;
                        if (doc.ActiveTabId == tab.TabId && SelectedTab is TerminalGroupTabViewModel g)
                            preferred = g;
                    }
                }
                catch
                {
                    // Isolate failures — continue restoring remaining tabs.
                }
            }

            if (preferred is not null)
                SelectedTab = preferred;

            if (!string.IsNullOrWhiteSpace(doc.LocalFilesPath) && Directory.Exists(doc.LocalFilesPath))
                await Files.NavigateToPathAsync(doc.LocalFilesPath);

            IsActivityBarExpanded = doc.ActivityBarExpanded;
            IsUtilityPanelOpen = doc.UtilityPanelOpen;

            if (!string.IsNullOrWhiteSpace(doc.ActiveSection) &&
                Enum.TryParse<ActivitySection>(doc.ActiveSection, true, out var section) &&
                section != ActivitySection.Settings)
            {
                var activity = Activities.FirstOrDefault(a => a.Section == section);
                if (activity is not null) SelectActivity(activity);
            }

            return opened > 0;
        }
        catch
        {
            _lastWorkspaceRecoveryNote = "Workspace restoration failed; falling back to default startup.";
            return false;
        }
    }

    private async Task<bool> RestoreTerminalTabAsync(Core.Models.Ui.WorkspaceTabLayout tab, bool reconnectRemote)
    {
        // Prefer full split tree when present.
        if (tab.SplitRoot is not null)
        {
            SessionDefinition SeedDefinition()
            {
                if (tab.SshSessionId is Guid sid)
                {
                    var record = Sessions.FindSession(sid);
                    if (record is not null)
                    {
                        return new SessionDefinition
                        {
                            Name = string.IsNullOrWhiteSpace(tab.Title) ? record.Name : tab.Title,
                            Kind = SessionKind.Ssh,
                            Host = record.Host,
                            Port = record.Port,
                            Username = record.Username,
                            Group = "SSH",
                            SshSessionId = record.Id
                        };
                    }
                }

                if (!string.IsNullOrWhiteSpace(tab.LocalShell) &&
                    Enum.TryParse<ShellKind>(tab.LocalShell, true, out var shell))
                {
                    return new SessionDefinition
                    {
                        Name = string.IsNullOrWhiteSpace(tab.Title) ? shell.ToString() : tab.Title,
                        Kind = SessionKind.LocalShell,
                        Shell = shell,
                        Group = "Local",
                        WorkingDirectory = tab.WorkingDirectory
                    };
                }

                return new SessionDefinition
                {
                    Name = string.IsNullOrWhiteSpace(tab.Title) ? "Terminal" : tab.Title,
                    Kind = SessionKind.LocalShell,
                    Shell = ShellKind.PowerShell,
                    Group = "Local"
                };
            }

            var seed = SeedDefinition();
            if (seed.Kind == SessionKind.Ssh && seed.SshSessionId is null)
                return false; // missing profile — skip

            var start = seed.Kind != SessionKind.Ssh || reconnectRemote;
            var group = new TerminalGroupTabViewModel(seed, _hostFactory)
            {
                SecuritySettings = CurrentSettings.Security,
                IsPinned = tab.IsPinned
            };
            if (!string.IsNullOrWhiteSpace(tab.Title))
                group.Title = tab.Title;

            group.RebuildFromSplitLayout(tab.SplitRoot, leaf =>
            {
                if (leaf.SshSessionId is Guid leafSid)
                {
                    var record = Sessions.FindSession(leafSid);
                    if (record is not null)
                    {
                        return new SessionDefinition
                        {
                            Name = string.IsNullOrWhiteSpace(leaf.PaneTitle) ? record.Name : leaf.PaneTitle,
                            Kind = SessionKind.Ssh,
                            Host = record.Host,
                            Port = record.Port,
                            Username = record.Username,
                            Group = "SSH",
                            SshSessionId = record.Id
                        };
                    }
                }

                if (!string.IsNullOrWhiteSpace(leaf.LocalShell) &&
                    Enum.TryParse<ShellKind>(leaf.LocalShell, true, out var shell))
                {
                    return new SessionDefinition
                    {
                        Name = string.IsNullOrWhiteSpace(leaf.PaneTitle) ? shell.ToString() : leaf.PaneTitle!,
                        Kind = SessionKind.LocalShell,
                        Shell = shell,
                        Group = "Local",
                        WorkingDirectory = leaf.WorkingDirectory
                    };
                }

                return Clone(seed);
            });

            foreach (var pane in group.AllPanes)
            {
                AttachShellIntegration(pane);
                if (!start && pane.Definition.Kind == SessionKind.Ssh)
                {
                    pane.IsPlaceholder = true;
                    pane.Status = "Restored · disconnected";
                }
            }

            group.StatusChanged += UpdateStatus;
            group.LayoutChanged += () => ScheduleWorkspaceSave();
            InsertTabOrdered(group);
            SelectedTab = group;
            if (start)
                await group.StartAsync();
            return true;
        }

        if (tab.SshSessionId is Guid sid)
        {
            var record = Sessions.FindSession(sid);
            if (record is null) return false;
            await OpenSessionCoreAsync(new SessionDefinition
            {
                Name = string.IsNullOrWhiteSpace(tab.Title) ? record.Name : tab.Title,
                Kind = SessionKind.Ssh,
                Host = record.Host,
                Port = record.Port,
                Username = record.Username,
                Group = "SSH",
                SshSessionId = record.Id
            }, startHosts: reconnectRemote);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(tab.LocalShell) &&
            Enum.TryParse<ShellKind>(tab.LocalShell, true, out var localShell))
        {
            await OpenSessionCoreAsync(new SessionDefinition
            {
                Name = string.IsNullOrWhiteSpace(tab.Title) ? localShell.ToString() : tab.Title,
                Kind = SessionKind.LocalShell,
                Shell = localShell,
                Group = "Local",
                WorkingDirectory = tab.WorkingDirectory
            });
            return true;
        }

        return false;
    }

    private void ApplySettingsToUi(AppSettings settings)
    {
        CurrentSettings = settings;
        ThemeMode = settings.ThemeMode;
        WindowMaterial = settings.WindowMaterial;
        IsUtilityPanelOpen = settings.IsUtilityPanelOpen;
        IsActivityBarExpanded = settings.IsActivityBarExpanded;
        FontFamily = settings.Terminal.FontFamily;
        FontSize = settings.Terminal.FontSize;
        SchemeId = settings.Terminal.SchemeId;
        CursorStyle = settings.Terminal.CursorStyle;
        CursorBlink = settings.Terminal.CursorBlink;
        EditorFontFamily = settings.Editor.FontFamily;
        EditorFontSize = settings.Editor.FontSize;
        EditorLineHeight = settings.Editor.LineHeight;
        EditorWordWrap = settings.Editor.WordWrap;
        EditorMinimap = settings.Editor.Minimap;
        EditorTabSize = settings.Editor.TabSize;
        EditorInsertSpaces = settings.Editor.InsertSpaces;
        EditorAutoSave = settings.Editor.AutoSave;
        EditorRenderWhitespace = settings.Editor.RenderWhitespace;
        HistoryEnabled = settings.History.Enabled;
        HistoryRetentionDays = settings.History.RetentionDays;
        HistoryMaxEntries = settings.History.MaxEntries;
        HistoryExcludeSsh = settings.History.ExcludeSshSessions;
        HistoryExcludePatternsText = string.Join(Environment.NewLine, settings.History.ExcludePatterns);
        foreach (var tile in SchemeTiles) tile.IsSelected = tile.Scheme.Id == SchemeId;

        ShellProfiles.Clear();
        foreach (var p in settings.Profiles) ShellProfiles.Add(p);
        EnsureKeybindings(settings);
        RefreshKeybindingRows();
        RefreshKnownHosts();
        foreach (var group in Tabs.OfType<TerminalGroupTabViewModel>())
            group.SecuritySettings = settings.Security;
        Notifications.Enabled = settings.General.ShowToastNotifications;
        AppLog.Configure(settings.Advanced.EnableFileLogging, settings.Advanced.MaxLogFiles);
        Files.ApplyFilesSettings(settings.Files);
        Sftp.ApplyFilesSettings(settings.Files);
        ApplyShellIntegrationSetting(settings.Advanced.EnableShellIntegration);
        Sftp.SetFollowTerminalDirectory(settings.Advanced.FollowTerminalDirectoryInSftp);
        SyncCommandShortcuts();
        OnPropertyChanged(nameof(CurrentSettings));
    }

    private void ApplyShellIntegrationSetting(bool enabled)
    {
        foreach (var group in Tabs.OfType<TerminalGroupTabViewModel>())
        {
            foreach (var pane in group.AllPanes)
            {
                pane.ShellIntegrationEnabled = enabled;
                AttachShellIntegration(pane);
            }
        }
    }

    private void AttachShellIntegration(TerminalTabViewModel pane)
    {
        pane.ShellIntegrationEnabled = CurrentSettings.Advanced.EnableShellIntegration;
        pane.ShellIntegrationEvent -= OnPaneShellIntegrationEvent;
        pane.ShellIntegrationEvent += OnPaneShellIntegrationEvent;
    }

    private void OnPaneShellIntegrationEvent(TerminalTabViewModel pane, ShellIntegrationEvent ev)
    {
        HistoryTracker.OnShellIntegrationEvent(pane, ev);

        if (ev.Kind != ShellIntegrationEventKind.Directory) return;
        if (!CurrentSettings.Advanced.FollowTerminalDirectoryInSftp) return;
        if (!CurrentSettings.Advanced.TrackTerminalWorkingDirectory) return;
        if (!Sftp.IsConnected || Sftp.ActiveSessionId is null) return;
        if (pane.Definition.SshSessionId is not Guid sid) return;

        if (!Sftp.TryAcceptTerminalDirectory(sid, ev.WorkingDirectory, out var path, out _))
            return;

        _ = Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Sftp.NavigateToRemoteDirectoryAsync(path);
            }
            catch
            {
                // Ignore unavailable paths / disconnected SFTP.
            }
        });
    }

    private AppSettings BuildSettings()
    {
        CurrentSettings.ThemeMode = ThemeMode;
        CurrentSettings.WindowMaterial = WindowMaterial;
        CurrentSettings.IsUtilityPanelOpen = IsUtilityPanelOpen;
        CurrentSettings.IsActivityBarExpanded = IsActivityBarExpanded;
        CurrentSettings.Terminal.FontFamily = FontFamily;
        CurrentSettings.Terminal.FontSize = FontSize;
        CurrentSettings.Terminal.SchemeId = SchemeId;
        CurrentSettings.Terminal.CursorStyle = CursorStyle;
        CurrentSettings.Terminal.CursorBlink = CursorBlink;
        CurrentSettings.Editor.FontFamily = EditorFontFamily;
        CurrentSettings.Editor.FontSize = EditorFontSize;
        CurrentSettings.Editor.LineHeight = EditorLineHeight;
        CurrentSettings.Editor.WordWrap = EditorWordWrap;
        CurrentSettings.Editor.Minimap = EditorMinimap;
        CurrentSettings.Editor.TabSize = EditorTabSize;
        CurrentSettings.Editor.InsertSpaces = EditorInsertSpaces;
        CurrentSettings.Editor.AutoSave = EditorAutoSave;
        CurrentSettings.Editor.RenderWhitespace = EditorRenderWhitespace;
        CurrentSettings.History.Enabled = HistoryEnabled;
        CurrentSettings.History.RetentionDays = HistoryRetentionDays;
        CurrentSettings.History.MaxEntries = HistoryMaxEntries;
        CurrentSettings.History.ExcludeSshSessions = HistoryExcludeSsh;
        CurrentSettings.History.ExcludePatterns = HistoryExcludePatternsText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        CurrentSettings.Profiles = ShellProfiles.ToList();
        if (Keybindings is not null)
            CurrentSettings.Keybindings = Keybindings.ExportOverrides();
        return CurrentSettings;
    }

    private void EnsureKeybindings(AppSettings settings)
    {
        Keybindings ??= new KeybindingService(new (string, string)[]
        {
            ("app.palette", "Ctrl+Shift+P"),
            ("app.switcher", "Ctrl+P"),
            ("settings.open", "Ctrl+,"),
            ("editor.save", "Ctrl+S"),
            ("editor.saveAs", "Ctrl+Shift+S"),
            ("editor.open", "Ctrl+O"),
            ("term.splitRight", "Alt+Shift+Right"),
            ("term.splitDown", "Alt+Shift+Down"),
            ("tab.reopen", "Ctrl+Shift+T"),
            ("tab.close", "Ctrl+W"),
            ("pane.close", "Ctrl+Shift+W"),
        });
        Keybindings.LoadOverrides(settings.Keybindings);
    }

    private void RefreshKeybindingRows()
    {
        KeybindingRows.Clear();
        if (Keybindings is null) return;
        SyncCommandShortcuts();
        var conflicts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in Keybindings.DetectConflicts())
        {
            conflicts[c.CommandIdA] = c.CommandIdB;
            conflicts[c.CommandIdB] = c.CommandIdA;
        }
        foreach (var cmd in Commands.Commands.OrderBy(c => c.Category).ThenBy(c => c.Title))
        {
            conflicts.TryGetValue(cmd.Id, out var other);
            KeybindingRows.Add(new KeybindingRow
            {
                CommandId = cmd.Id,
                Title = cmd.Title,
                Category = cmd.Category.ToString(),
                Gesture = Keybindings.GetGesture(cmd.Id),
                DefaultGesture = Keybindings.GetDefaultGesture(cmd.Id),
                ConflictWith = other ?? ""
            });
        }

        ApplyKeybindingFilter();
    }

    partial void OnKeybindingFilterChanged(string value) => ApplyKeybindingFilter();

    private void ApplyKeybindingFilter()
    {
        FilteredKeybindingRows.Clear();
        var q = KeybindingFilter?.Trim() ?? "";
        foreach (var row in KeybindingRows)
        {
            if (string.IsNullOrEmpty(q) ||
                row.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                row.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                row.Gesture.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                row.CommandId.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                FilteredKeybindingRows.Add(row);
            }
        }
    }

    public void BeginKeybindingRecord()
    {
        if (SelectedKeybinding is null)
        {
            KeybindingRecordHint = "Select a command row first, then click Record.";
            return;
        }

        IsRecordingKeybinding = true;
        KeybindingRecordHint = $"Recording for “{SelectedKeybinding.Title}” — press the shortcut now (Esc cancels).";
    }

    public void CancelKeybindingRecord()
    {
        IsRecordingKeybinding = false;
        KeybindingRecordHint = "";
    }

    public bool TryCompleteKeybindingRecord(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers)
    {
        if (!IsRecordingKeybinding || SelectedKeybinding is null || Keybindings is null)
            return false;

        if (key is System.Windows.Input.Key.Escape)
        {
            CancelKeybindingRecord();
            return true;
        }

        if (key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
            or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
            or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift
            or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin)
            return true;

        if (KeybindingService.IsModifierOnly(key))
            return true;

        var gesture = KeybindingService.ToGesture(key, modifiers);
        if (string.IsNullOrWhiteSpace(gesture))
            return true;

        if (Keybindings.IsReserved(gesture))
        {
            KeybindingRecordHint = $"“{gesture}” is reserved and cannot be assigned.";
            return true;
        }

        if (!Keybindings.TrySetGesture(SelectedKeybinding.CommandId, gesture, allowReplaceConflict: false, out var conflictError))
        {
            var replace = MessageBox.Show(
                $"{conflictError}\n\nReassign “{gesture}” to “{SelectedKeybinding.Title}” and clear the previous binding?",
                "Shortcut conflict",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (replace != MessageBoxResult.Yes)
            {
                KeybindingRecordHint = "Recording cancelled — conflict not applied.";
                IsRecordingKeybinding = false;
                return true;
            }

            if (!Keybindings.TrySetGesture(SelectedKeybinding.CommandId, gesture, allowReplaceConflict: true, out var err2))
            {
                KeybindingRecordHint = err2 ?? "Could not assign shortcut.";
                return true;
            }
        }

        SelectedKeybinding.Gesture = gesture;
        IsRecordingKeybinding = false;
        KeybindingRecordHint = $"Assigned {gesture} to {SelectedKeybinding.Title}.";
        RefreshKeybindingRows();
        SelectedKeybinding = FilteredKeybindingRows.FirstOrDefault(r => r.CommandId == SelectedKeybinding.CommandId);
        SyncCommandShortcuts();
        _ = PersistAsync();
        return true;
    }

    public void ClearSelectedKeybinding()
    {
        if (SelectedKeybinding is null || Keybindings is null) return;
        Keybindings.SetGesture(SelectedKeybinding.CommandId, "");
        SelectedKeybinding.Gesture = "";
        RefreshKeybindingRows();
        _ = PersistAsync();
    }

    private void SyncCommandShortcuts()
    {
        if (Keybindings is null) return;
        foreach (var cmd in Commands.Commands)
            cmd.Shortcut = Keybindings.GetGesture(cmd.Id);
    }

    private void RefreshKnownHosts()
    {
        KnownHosts.Clear();
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XTerminal", "known_hosts");
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;
                KnownHosts.Add(new KnownHostRow { HostPort = parts[0], Fingerprint = parts[1] });
            }
        }
        catch
        {
            // surface empty list
        }
    }

    [RelayCommand]
    private void OpenAppDataFolder()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppDataDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Notifications.Show("App data", ex.Message);
        }
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"X-Terminal {AboutVersion}");
        sb.AppendLine(AboutRuntime);
        sb.AppendLine($"AppData: {AppDataDirectory}");
        sb.AppendLine($"Tabs: {Tabs.Count}");
        sb.AppendLine($"Theme: {ThemeMode}");
        if (!string.IsNullOrWhiteSpace(Sessions.LastDiagnosticClipboard))
        {
            sb.AppendLine("--- Last SSH diagnostics ---");
            sb.AppendLine(Sessions.LastDiagnosticClipboard);
        }

        try { Clipboard.SetText(sb.ToString()); } catch { /* ignore */ }
        Notifications.Show("Diagnostics", "Copied non-sensitive diagnostic text.");
    }

    [RelayCommand]
    private async Task RemoveKnownHostAsync(KnownHostRow? row)
    {
        if (row is null) return;
        if (MessageBox.Show(
                $"Remove trusted host key for {row.HostPort}?\n\nThis cannot be undone.",
                "Remove known host",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XTerminal", "known_hosts");
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path)
                .Where(l => !l.StartsWith(row.HostPort + " ", StringComparison.OrdinalIgnoreCase))
                .ToList();
            File.WriteAllLines(path, lines);
            RefreshKnownHosts();
        }
        catch (Exception ex)
        {
            Notifications.Show("Known hosts", ex.Message);
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ResetKeybinding(KeybindingRow? row)
    {
        if (row is null || Keybindings is null) return;
        Keybindings.Reset(row.CommandId);
        row.Gesture = Keybindings.GetGesture(row.CommandId);
        _ = PersistAsync();
    }

    [RelayCommand]
    private void ResetAllKeybindings()
    {
        var confirm = MessageBox.Show(
            "Restore all keyboard shortcuts to their defaults?",
            "Restore defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        Keybindings?.ResetAll();
        RefreshKeybindingRows();
        SyncCommandShortcuts();
        _ = PersistAsync();
    }

    private void SaveNamedWorkspace()
    {
        var name = PromptText("Save workspace", "Workspace name:", "My workspace");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var doc = BuildWorkspaceDocumentFromUi(name.Trim());
            var entry = new Core.Models.Ui.WorkspaceProfileEntry { Name = name.Trim() };
            _ = _workspaceProfiles.SaveProfileAsync(entry, doc).ContinueWith(t =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (t.IsFaulted)
                        MessageBox.Show(t.Exception?.GetBaseException().Message ?? "Save failed", "Save workspace", MessageBoxButton.OK, MessageBoxImage.Warning);
                    else
                        MessageBox.Show($"Saved workspace “{name.Trim()}”.", "Workspace", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save workspace", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task OpenNamedWorkspaceAsync()
    {
        var catalog = await _workspaceProfiles.LoadCatalogAsync();
        if (catalog.Profiles.Count == 0)
        {
            MessageBox.Show("No named workspaces saved yet.", "Open workspace", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dirtyEditors = Tabs.OfType<EditorTabViewModel>().Any(e => e.IsDirty);
        var active = Tabs.OfType<TerminalGroupTabViewModel>()
            .SelectMany(g => g.AllPanes)
            .Any(p => p.ConnectionState is ConnectionState.Connected or ConnectionState.Connecting);
        if (dirtyEditors || active)
        {
            var warn = MessageBox.Show(
                "Opening a workspace replaces the current layout.\n\n" +
                (dirtyEditors ? "• Unsaved editor changes may be lost if you continue without saving.\n" : "") +
                (active ? "• Active terminal sessions will be closed.\n" : "") +
                "\nContinue?",
                "Replace workspace",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (warn != MessageBoxResult.Yes) return;
        }

        var names = string.Join(Environment.NewLine, catalog.Profiles.Select(p => $"• {p.Name}"));
        var pick = PromptText(
            "Open workspace",
            $"Enter the exact workspace name to open:\n\n{names}",
            catalog.Profiles[0].Name);
        if (string.IsNullOrWhiteSpace(pick)) return;
        var entry = catalog.Profiles.FirstOrDefault(p => p.Name.Equals(pick.Trim(), StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            MessageBox.Show("Workspace not found.", "Open workspace", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var doc = await _workspaceProfiles.LoadProfileAsync(entry.Id);
        if (doc is null)
        {
            MessageBox.Show("Workspace file missing or corrupt.", "Open workspace", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var tab in Tabs.ToList())
            await CloseTabAsync(tab);

        await _workspaceLayout.SaveAsync(doc);
        var restored = await TryRestoreWorkspaceAsync(CurrentSettings);
        if (!restored)
            MessageBox.Show("Workspace opened but no tabs could be restored.", "Open workspace", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task RestorePreviousWorkspaceAsync()
    {
        var restored = await TryRestoreWorkspaceAsync(CurrentSettings);
        MessageBox.Show(
            restored
                ? "Previous workspace layout restored (new shell processes; SSH only if reconnect is enabled)."
                : (_lastWorkspaceRecoveryNote ?? "No previous workspace layout was available."),
            "Restore workspace",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static string? PromptText(string title, string prompt, string defaultValue)
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var box = new TextBox { Text = defaultValue, Padding = new Thickness(6, 4, 6, 4) };
        panel.Children.Add(box);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        string? result = null;
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => { result = box.Text; win.DialogResult = true; };
        cancel.Click += (_, _) => { win.DialogResult = false; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        win.Content = panel;
        return win.ShowDialog() == true ? result : null;
    }

    private async Task PersistAsync()
    {
        if (_suppressPersist) return;
        try { await _settingsStore.SaveAsync(BuildSettings()); }
        catch { /* ignore */ }
    }

    private void BuildActivities()
    {
        Activities.Add(new ActivityItemViewModel { Section = ActivitySection.Terminal, Glyph = "⌘", Label = "Terminal", ToolTip = "Terminal", IsSelected = true });
        Activities.Add(new ActivityItemViewModel { Section = ActivitySection.Sessions, Glyph = "⧉", Label = "Sessions", ToolTip = "SSH sessions" });
        Activities.Add(new ActivityItemViewModel { Section = ActivitySection.Sftp, Glyph = "⇅", Label = "SFTP", ToolTip = "SFTP file workspace" });
        Activities.Add(new ActivityItemViewModel { Section = ActivitySection.Files, Glyph = "▤", Label = "Files", ToolTip = "Local files explorer" });
        Activities.Add(new ActivityItemViewModel { Section = ActivitySection.Editor, Glyph = "✎", Label = "Editor", ToolTip = "Open editor tab" });
        Activities.Add(new ActivityItemViewModel { Section = ActivitySection.PortForwarding, Glyph = "⇄", Label = "Ports", ToolTip = "Port forwarding" });
        Activities.Add(new ActivityItemViewModel { Section = ActivitySection.Snippets, Glyph = "♯", Label = "Snippets", ToolTip = "Command snippets" });
        Activities.Add(new ActivityItemViewModel { Section = ActivitySection.History, Glyph = "◷", Label = "History", ToolTip = "Session history" });
    }

    private void RefreshActivitySelection()
    {
        foreach (var a in Activities) a.IsSelected = a.Section == ActiveSection;
        NotifyViewFlags();
    }

    private void NotifyViewFlags()
    {
        OnPropertyChanged(nameof(IsSettingsView));
        OnPropertyChanged(nameof(IsTerminalView));
        OnPropertyChanged(nameof(IsSessionsView));
        OnPropertyChanged(nameof(IsSftpView));
        OnPropertyChanged(nameof(IsFilesView));
        OnPropertyChanged(nameof(IsPortsView));
        OnPropertyChanged(nameof(IsSnippetsView));
        OnPropertyChanged(nameof(IsHistoryView));
        OnPropertyChanged(nameof(IsComingSoonView));
        OnPropertyChanged(nameof(ShowCancelConnect));
        OnPropertyChanged(nameof(ShowReconnect));
        UpdateStatus();
    }

    private void NotifyTabSelection()
    {
        OnPropertyChanged(nameof(SelectedTerminalGroup));
        OnPropertyChanged(nameof(SelectedTerminalTab));
        OnPropertyChanged(nameof(SelectedEditorTab));
        OnPropertyChanged(nameof(ShowCancelConnect));
        OnPropertyChanged(nameof(ShowReconnect));
        OnPropertyChanged(nameof(IsTerminalTabSelected));
        OnPropertyChanged(nameof(IsEditorTabSelected));
    }

    [RelayCommand]
    private void PinTab(IWorkspaceTab? tab)
    {
        tab ??= SelectedTab;
        if (tab is null) return;
        tab.IsPinned = !tab.IsPinned;
        Tabs.Remove(tab);
        InsertTabOrdered(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private async Task DuplicateTabAsync(IWorkspaceTab? tab)
    {
        tab ??= SelectedTab;
        switch (tab)
        {
            case TerminalGroupTabViewModel g when g.ActivePane is not null:
                await OpenSessionCoreAsync(Clone(g.ActivePane.Definition));
                break;
            case EditorTabViewModel e when e.Kind == EditorDocumentKind.Local && e.LocalPath is not null:
                await OpenLocalPathAsync(e.LocalPath);
                break;
            case EditorTabViewModel:
                NewUntitledEditor();
                break;
        }
    }

    [RelayCommand]
    private async Task ReopenClosedTabAsync()
    {
        if (RecentlyClosed.Count == 0)
        {
            await NewPowerShellAsync();
            return;
        }

        var record = RecentlyClosed[0];
        RecentlyClosed.RemoveAt(0);
        switch (record.Kind)
        {
            case ClosedTabKind.LocalShell when record.Session is not null:
            case ClosedTabKind.Ssh when record.Session is not null:
                await OpenSessionCoreAsync(Clone(record.Session));
                break;
            case ClosedTabKind.EditorLocal when record.LocalPath is not null && File.Exists(record.LocalPath):
                await OpenLocalPathAsync(record.LocalPath);
                break;
            default:
                await NewPowerShellAsync();
                break;
        }
    }

    [RelayCommand]
    private async Task CloseOtherTabsAsync(IWorkspaceTab? keep)
    {
        keep ??= SelectedTab;
        if (keep is null) return;
        foreach (var tab in Tabs.Where(t => !ReferenceEquals(t, keep) && !t.IsPinned).ToList())
            await CloseTabAsync(tab);
    }

    [RelayCommand]
    private async Task CloseTabsToRightAsync(IWorkspaceTab? from)
    {
        from ??= SelectedTab;
        if (from is null) return;
        var idx = Tabs.IndexOf(from);
        if (idx < 0) return;
        foreach (var tab in Tabs.Skip(idx + 1).Where(t => !t.IsPinned).ToList())
            await CloseTabAsync(tab);
    }

    [RelayCommand]
    private async Task CloseUnpinnedTabsAsync()
    {
        foreach (var tab in Tabs.Where(t => !t.IsPinned).ToList())
            await CloseTabAsync(tab);
    }

    [RelayCommand]
    private void RenameTab(IWorkspaceTab? tab)
    {
        tab ??= SelectedTab;
        if (tab is null) return;
        var window = new Views.InputPromptWindow("Rename tab", tab.Title.TrimStart('•', ' ').Trim())
        {
            Owner = Application.Current?.MainWindow
        };
        if (window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.Value))
        {
            tab.Title = window.Value.Trim();
            if (tab is TerminalGroupTabViewModel g && g.ActivePane is not null)
                g.ActivePane.Title = tab.Title;
        }
    }

    [RelayCommand]
    private async Task SplitRightAsync()
    {
        if (SelectedTerminalGroup is null) return;
        await SelectedTerminalGroup.SplitAsync(Orientation.Vertical);
        NotifyTabSelection();
        UpdateStatus();
    }

    [RelayCommand]
    private async Task SplitDownAsync()
    {
        if (SelectedTerminalGroup is null) return;
        await SelectedTerminalGroup.SplitAsync(Orientation.Horizontal);
        NotifyTabSelection();
        UpdateStatus();
    }

    [RelayCommand]
    private async Task CloseActivePaneAsync()
    {
        if (SelectedTerminalGroup is null) return;
        if (SelectedTerminalGroup.PaneCount <= 1)
        {
            await CloseTabAsync(SelectedTerminalGroup);
            return;
        }

        var paneId = SelectedTerminalGroup.ActivePane?.Id;
        await SelectedTerminalGroup.CloseActivePaneAsync();
        if (paneId is Guid id)
            HistoryTracker.ForgetTab(id);
        NotifyTabSelection();
        UpdateStatus();
    }

    [RelayCommand]
    private void ToggleBroadcast()
    {
        if (SelectedTerminalGroup is null) return;
        SelectedTerminalGroup.SetBroadcast(!SelectedTerminalGroup.BroadcastEnabled);
        UpdateStatus();
    }

    [RelayCommand]
    private void TogglePrivateInput()
    {
        SelectedTerminalTab?.TogglePrivateInput();
        UpdateStatus();
    }

    private void ShowShellIntegrationStatus()
    {
        var pane = SelectedTerminalTab;
        if (pane is null) return;
        MessageBox.Show(
            $"Pane: {pane.Title}\n" +
            $"Integration: {pane.ShellIntegrationStatus}\n" +
            $"Enabled setting: {CurrentSettings.Advanced.EnableShellIntegration}\n" +
            $"Working directory: {pane.TrackedWorkingDirectory ?? pane.Definition.WorkingDirectory ?? "N/A"}\n" +
            $"Last exit: {pane.LastExitCode?.ToString() ?? "N/A"}\n" +
            $"Last duration: {pane.LastCommandDurationLabel ?? "N/A"}\n\n" +
            "Metadata is advisory. Hooks are never installed automatically.",
            "Shell integration",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ShowCurrentWorkingDirectory()
    {
        var pane = SelectedTerminalTab;
        if (pane is null) return;
        var cwd = pane.TrackedWorkingDirectory ?? pane.Definition.WorkingDirectory ?? "N/A";
        MessageBox.Show(
            cwd,
            "Current working directory",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CopyShellHookSnippet(string shell)
    {
        var confirm = MessageBox.Show(
            $"Copy the optional {shell} shell-integration snippet to the clipboard?\n\n" +
            "X-Terminal will not edit remote shell configuration files. " +
            "Paste the snippet yourself only if you approve the change.",
            "Shell integration snippet",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            Clipboard.SetText(ShellIntegrationHooks.GetSnippet(shell));
        }
        catch
        {
            MessageBox.Show("Could not access the clipboard.", "Shell integration", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ToggleSftpDirectoryFollow()
    {
        CurrentSettings.Advanced.FollowTerminalDirectoryInSftp =
            !CurrentSettings.Advanced.FollowTerminalDirectoryInSftp;
        Sftp.SetFollowTerminalDirectory(CurrentSettings.Advanced.FollowTerminalDirectoryInSftp);
        FlushNestedSettings();
        MessageBox.Show(
            CurrentSettings.Advanced.FollowTerminalDirectoryInSftp
                ? "SFTP will follow the terminal working directory when shell integration reports it (same SSH session only)."
                : "SFTP directory following is disabled. Manual browsing is unchanged.",
            "SFTP directory following",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SyncSftpToTerminalDirectory()
    {
        var pane = SelectedTerminalTab;
        if (pane is null || !Sftp.IsConnected) return;
        if (pane.Definition.SshSessionId is not Guid sid)
        {
            MessageBox.Show("The active pane is not an SSH session.", "Synchronize SFTP", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var path = pane.TrackedWorkingDirectory ?? pane.Definition.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show(
                "No reliable working directory is available. Enable shell integration and ensure the shell emits OSC 7 / OSC 633 Cwd.",
                "Synchronize SFTP",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Sftp.ActiveSessionId != sid)
        {
            MessageBox.Show(
                "Terminal and SFTP sessions do not match.",
                "Synchronize SFTP",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var normalized = SftpPathSafety.NormalizeRemote(path);
            _ = Sftp.NavigateToRemoteDirectoryAsync(normalized);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Synchronize SFTP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void OpenCommandPalette()
    {
        IsQuickSwitcherOpen = false;
        IsCommandPaletteOpen = true;
        PaletteQuery = string.Empty;
    }

    [RelayCommand]
    private void OpenQuickSwitcher()
    {
        IsCommandPaletteOpen = false;
        IsQuickSwitcherOpen = true;
        SwitcherQuery = string.Empty;
    }

    [RelayCommand]
    private void CloseOverlays()
    {
        IsCommandPaletteOpen = false;
        IsQuickSwitcherOpen = false;
    }

    [RelayCommand]
    private async Task QuickConnectAsync()
    {
        var ssh = CurrentSettings.Ssh;
        var window = new Views.QuickConnectWindow(ssh) { Owner = Application.Current?.MainWindow };
        if (window.ShowDialog() != true || window.Result is null) return;
        var r = window.Result;
        var secrets = new WindowsCredentialStore();
        var sessionId = Guid.NewGuid();
        var record = new SshSessionRecord
        {
            Id = sessionId,
            Name = $"{r.Username}@{r.Host}",
            Host = r.Host,
            Port = r.Port,
            Username = r.Username,
            AuthMethod = SshAuthMethod.Password,
            ConnectionTimeoutSeconds = ssh.ConnectionTimeoutSeconds,
            KeepAliveIntervalSeconds = ssh.KeepAliveIntervalSeconds,
            AutoReconnect = ssh.AutoReconnect,
            MaxReconnectAttempts = ssh.MaxReconnectAttempts,
            StrictHostKeyChecking = ssh.StrictHostKeyChecking
        };

        if (!string.IsNullOrEmpty(r.Password))
            await secrets.SaveSecretAsync(SshSecretKeys.Password(sessionId), r.Password);

        if (r.SaveSession)
        {
            var editor = new SshSessionEditorViewModel(secrets, new SshHostKeyPolicy(new KnownHostsStore(), new Services.WpfHostKeyPrompt()), record)
            {
                Password = r.Remember ? (r.Password ?? string.Empty) : string.Empty
            };
            await Sessions.SaveEditorAsync(editor, isNew: true);
            record = Sessions.FindSession(sessionId) ?? record;
            if (r.Remember && !string.IsNullOrEmpty(r.Password))
                await secrets.SaveSecretAsync(SshSecretKeys.Password(sessionId), r.Password);
        }
        else if (!r.Remember && !string.IsNullOrEmpty(r.Password))
        {
            // One-shot secret for this connect; remove after connect attempt begins.
            // Kept until connect so AuthFactory can read it; cleaned in finally below.
        }

        try
        {
            await ConnectSshAsync(record);
        }
        finally
        {
            // Remember Credentials is independent of Save Session.
            if (!r.Remember)
                await secrets.DeleteSecretAsync(SshSecretKeys.Password(sessionId));
        }
    }

    private static ShellKind ResolveDefaultShellKind(AppSettings settings)
    {
        var profile = settings.Profiles?.FirstOrDefault(p => p.IsDefault)
                      ?? settings.Profiles?.FirstOrDefault();
        var name = profile?.Shell ?? settings.Terminal.DefaultShell;
        return name?.Equals("Cmd", StringComparison.OrdinalIgnoreCase) == true ? ShellKind.Cmd
            : name?.Equals("Wsl", StringComparison.OrdinalIgnoreCase) == true ? ShellKind.Wsl
            : ShellKind.PowerShell;
    }

    private void RegisterCommands()
    {
        Commands.RegisterMany(new[]
        {
            new AppCommand { Id = "term.ps", Title = "New PowerShell", Category = CommandCategory.Terminal, Execute = () => _ = NewPowerShellAsync() },
            new AppCommand { Id = "term.cmd", Title = "New CMD", Category = CommandCategory.Terminal, Execute = () => _ = NewCmdAsync() },
            new AppCommand { Id = "term.wsl", Title = "New WSL", Category = CommandCategory.Terminal, Execute = () => _ = NewWslAsync() },
            new AppCommand { Id = "term.splitRight", Title = "Split Right", Category = CommandCategory.Terminal, Shortcut = "Alt+Shift+Right", Execute = () => _ = SplitRightAsync(), CanExecute = () => SelectedTerminalGroup is not null },
            new AppCommand { Id = "term.splitDown", Title = "Split Down", Category = CommandCategory.Terminal, Shortcut = "Alt+Shift+Down", Execute = () => _ = SplitDownAsync(), CanExecute = () => SelectedTerminalGroup is not null },
            new AppCommand { Id = "term.rename", Title = "Rename Tab", Category = CommandCategory.Terminal, Execute = () => RenameTab(SelectedTab) },
            new AppCommand { Id = "term.broadcast", Title = "Toggle Broadcast Input", Category = CommandCategory.Terminal, Execute = ToggleBroadcast, CanExecute = () => SelectedTerminalGroup is not null },
            new AppCommand { Id = "term.broadcastAll", Title = "Broadcast: All Panes", Category = CommandCategory.Terminal, Execute = () => SelectedTerminalGroup?.SetBroadcastMode(BroadcastMode.AllPanes), CanExecute = () => SelectedTerminalGroup is not null },
            new AppCommand { Id = "term.broadcastSelected", Title = "Broadcast: Selected Panes", Category = CommandCategory.Terminal, Execute = () => SelectedTerminalGroup?.SetBroadcastMode(BroadcastMode.SelectedPanes), CanExecute = () => SelectedTerminalGroup is not null },
            new AppCommand { Id = "term.broadcastOff", Title = "Broadcast: Off", Category = CommandCategory.Terminal, Execute = () => SelectedTerminalGroup?.SetBroadcastMode(BroadcastMode.Off), CanExecute = () => SelectedTerminalGroup is not null },
            new AppCommand { Id = "term.private", Title = "Toggle Private Input Mode", Category = CommandCategory.Terminal, Execute = TogglePrivateInput, CanExecute = () => SelectedTerminalTab is not null },
            new AppCommand { Id = "term.clear", Title = "Clear Terminal", Category = CommandCategory.Terminal, Execute = () => TerminalActions.Clear?.Invoke(), CanExecute = () => SelectedTerminalTab is not null },
            new AppCommand { Id = "term.find", Title = "Find in Terminal", Category = CommandCategory.Terminal, Execute = () => TerminalActions.Find?.Invoke(), CanExecute = () => SelectedTerminalTab is not null },
            new AppCommand { Id = "term.copy", Title = "Copy", Category = CommandCategory.Terminal, Shortcut = "Ctrl+Shift+C", Execute = () => TerminalActions.Copy?.Invoke(), CanExecute = () => SelectedTerminalTab is not null },
            new AppCommand { Id = "term.paste", Title = "Paste", Category = CommandCategory.Terminal, Shortcut = "Ctrl+Shift+V", Execute = () => TerminalActions.Paste?.Invoke(), CanExecute = () => SelectedTerminalTab is not null },
            new AppCommand { Id = "term.selectAll", Title = "Select All", Category = CommandCategory.Terminal, Execute = () => TerminalActions.SelectAll?.Invoke(), CanExecute = () => SelectedTerminalTab is not null },
            new AppCommand { Id = "term.resetZoom", Title = "Reset Zoom", Category = CommandCategory.Terminal, Execute = () => SelectedTerminalTab?.ResetZoom(), CanExecute = () => SelectedTerminalTab is not null },
            new AppCommand { Id = "term.maximize", Title = "Maximize / Restore Pane", Category = CommandCategory.Terminal, Execute = () => SelectedTerminalGroup?.ToggleMaximize(), CanExecute = () => SelectedTerminalGroup is not null },
            new AppCommand { Id = "pane.close", Title = "Close Pane", Category = CommandCategory.Terminal, Shortcut = "Ctrl+Shift+W", Execute = () => _ = SelectedTerminalGroup?.CloseActivePaneAsync(), CanExecute = () => SelectedTerminalGroup is not null },
            new AppCommand { Id = "tab.close", Title = "Close Tab", Category = CommandCategory.Application, Shortcut = "Ctrl+W", Execute = () => _ = CloseTabAsync(SelectedTab), CanExecute = () => SelectedTab is not null },
            new AppCommand { Id = "tab.reopen", Title = "Reopen Closed Tab", Category = CommandCategory.Application, Shortcut = "Ctrl+Shift+T", Execute = () => _ = ReopenClosedTabAsync(), CanExecute = () => RecentlyClosed.Count > 0 },
            new AppCommand { Id = "ssh.quick", Title = "Quick Connect", Category = CommandCategory.Ssh, Execute = () => _ = QuickConnectAsync() },
            new AppCommand { Id = "ssh.sessions", Title = "Open Sessions", Category = CommandCategory.Sessions, Execute = () => SelectActivity(Activities.First(a => a.Section == ActivitySection.Sessions)) },
            new AppCommand { Id = "ssh.sftp", Title = "Open SFTP", Category = CommandCategory.Sftp, Execute = () => _ = OpenSftpAsync(null) },
            new AppCommand { Id = "ssh.reconnect", Title = "Reconnect", Category = CommandCategory.Ssh, Execute = () => _ = SelectedTerminalTab?.ReconnectCommand.ExecuteAsync(null), CanExecute = () => SelectedTerminalTab is not null },
            new AppCommand { Id = "ssh.export", Title = "Export Sessions", Category = CommandCategory.Sessions, Execute = () => _ = Sessions.ExportSessionsCommand.ExecuteAsync(null) },
            new AppCommand { Id = "ssh.import", Title = "Import Sessions", Category = CommandCategory.Sessions, Execute = () => _ = Sessions.ImportSessionsCommand.ExecuteAsync(null) },
            new AppCommand { Id = "files.open", Title = "Open Files", Category = CommandCategory.Files, Execute = () => SelectActivity(Activities.First(a => a.Section == ActivitySection.Files)) },
            new AppCommand { Id = "editor.open", Title = "Open File", Category = CommandCategory.Editor, Shortcut = "Ctrl+O", Execute = () => _ = OpenLocalFileAsync() },
            new AppCommand { Id = "editor.save", Title = "Save", Category = CommandCategory.Editor, Shortcut = "Ctrl+S", Execute = () => _ = SaveEditorAsync() },
            new AppCommand { Id = "editor.saveAs", Title = "Save As", Category = CommandCategory.Editor, Shortcut = "Ctrl+Shift+S", Execute = () => _ = SaveEditorAsAsync() },
            new AppCommand { Id = "workspace.save", Title = "Save Workspace", Category = CommandCategory.Application, Execute = SaveNamedWorkspace },
            new AppCommand { Id = "workspace.open", Title = "Open Workspace", Category = CommandCategory.Application, Execute = () => _ = OpenNamedWorkspaceAsync() },
            new AppCommand { Id = "workspace.restore", Title = "Restore Previous Workspace", Category = CommandCategory.Application, Execute = () => _ = RestorePreviousWorkspaceAsync() },
            new AppCommand { Id = "workspace.manage", Title = "Manage Workspaces", Category = CommandCategory.Application, Execute = () => { OpenSettings(); SettingsCategory = "Advanced"; } },
            new AppCommand { Id = "shell.integrationStatus", Title = "Show Shell Integration Status", Category = CommandCategory.Terminal, Execute = ShowShellIntegrationStatus, CanExecute = () => SelectedTerminalTab is not null },
            new AppCommand { Id = "shell.showCwd", Title = "Show Current Working Directory", Category = CommandCategory.Terminal, Execute = ShowCurrentWorkingDirectory, CanExecute = () => SelectedTerminalTab is not null },
            new AppCommand { Id = "shell.copyBashHooks", Title = "Copy Bash Shell Integration Snippet", Category = CommandCategory.Terminal, Execute = () => CopyShellHookSnippet("bash") },
            new AppCommand { Id = "shell.copyPwshHooks", Title = "Copy PowerShell Shell Integration Snippet", Category = CommandCategory.Terminal, Execute = () => CopyShellHookSnippet("pwsh") },
            new AppCommand { Id = "shell.copyZshHooks", Title = "Copy Zsh Shell Integration Snippet", Category = CommandCategory.Terminal, Execute = () => CopyShellHookSnippet("zsh") },
            new AppCommand { Id = "ssh.diagnostics", Title = "Show Connection Diagnostics", Category = CommandCategory.Ssh, Execute = () => _ = Sessions.DiagnoseSessionCommand.ExecuteAsync(Sessions.SelectedSession), CanExecute = () => Sessions.SelectedSession is not null },
            new AppCommand { Id = "sftp.syncDirectory", Title = "Synchronize SFTP Directory", Category = CommandCategory.Sftp, Execute = SyncSftpToTerminalDirectory, CanExecute = () => SelectedTerminalTab is not null && Sftp.IsConnected },
            new AppCommand { Id = "sftp.toggleFollow", Title = "Toggle SFTP Directory Following", Category = CommandCategory.Sftp, Execute = ToggleSftpDirectoryFollow },
            new AppCommand { Id = "ports.new", Title = "Create Port Forward", Category = CommandCategory.Ports, Execute = () => _ = Ports.NewForwardCommand.ExecuteAsync(null) },
            new AppCommand { Id = "ports.start", Title = "Start Selected Tunnel", Category = CommandCategory.Ports, Execute = () => _ = Ports.StartCommand.ExecuteAsync(Ports.SelectedForward), CanExecute = () => Ports.SelectedForward is not null },
            new AppCommand { Id = "ports.stop", Title = "Stop Selected Tunnel", Category = CommandCategory.Ports, Execute = () => _ = Ports.StopCommand.ExecuteAsync(Ports.SelectedForward), CanExecute = () => Ports.SelectedForward is not null },
            new AppCommand { Id = "ports.restart", Title = "Restart Selected Tunnel", Category = CommandCategory.Ports, Execute = () => _ = Ports.RestartCommand.ExecuteAsync(Ports.SelectedForward), CanExecute = () => Ports.SelectedForward is not null },
            new AppCommand { Id = "snippets.insert", Title = "Insert Snippet", Category = CommandCategory.Snippets, Shortcut = "Ctrl+Shift+I", Execute = () => SelectActivity(Activities.First(a => a.Section == ActivitySection.Snippets)) },
            new AppCommand { Id = "history.search", Title = "Search History", Category = CommandCategory.History, Execute = () => SelectActivity(Activities.First(a => a.Section == ActivitySection.History)) },
            new AppCommand { Id = "history.clear", Title = "Clear Command History", Category = CommandCategory.History, Execute = () => _ = History.ClearCommand.ExecuteAsync(null) },
            new AppCommand { Id = "settings.open", Title = "Open Settings", Category = CommandCategory.Settings, Shortcut = "Ctrl+,", Execute = OpenSettings },
            new AppCommand { Id = "settings.general", Title = "Settings: General", Category = CommandCategory.Settings, Execute = () => { OpenSettings(); SettingsCategory = "General"; } },
            new AppCommand { Id = "settings.appearance", Title = "Settings: Appearance", Category = CommandCategory.Settings, Execute = () => { OpenSettings(); SettingsCategory = "Appearance"; } },
            new AppCommand { Id = "settings.terminal", Title = "Settings: Terminal", Category = CommandCategory.Settings, Execute = () => { OpenSettings(); SettingsCategory = "Terminal"; } },
            new AppCommand { Id = "settings.ssh", Title = "Settings: SSH", Category = CommandCategory.Settings, Execute = () => { OpenSettings(); SettingsCategory = "SSH"; } },
            new AppCommand { Id = "settings.security", Title = "Settings: Security", Category = CommandCategory.Settings, Execute = () => { OpenSettings(); SettingsCategory = "Security"; } },
            new AppCommand { Id = "settings.keybindings", Title = "Settings: Keybindings", Category = CommandCategory.Settings, Execute = () => { OpenSettings(); SettingsCategory = "Keybindings"; } },
            new AppCommand { Id = "app.utility", Title = "Toggle Utility Panel", Category = CommandCategory.Application, Execute = ToggleUtilityPanel },
            new AppCommand { Id = "app.palette", Title = "Command Palette", Category = CommandCategory.Application, Shortcut = "Ctrl+Shift+P", Execute = OpenCommandPalette },
            new AppCommand { Id = "app.switcher", Title = "Quick Switcher", Category = CommandCategory.Application, Shortcut = "Ctrl+P", Execute = OpenQuickSwitcher },
        });
        SyncCommandShortcuts();
    }

    /// <summary>Hooks filled by MainWindow so palette/commands reach the live TerminalWorkspaceHost.</summary>
    public static class TerminalActions
    {
        public static Action? Clear { get; set; }
        public static Action? Find { get; set; }
        public static Action? Copy { get; set; }
        public static Action? Paste { get; set; }
        public static Action? SelectAll { get; set; }
    }

    private void BuildFonts()
    {
        foreach (var font in new[]
                 {
                     "Cascadia Mono", "Cascadia Code", "Consolas", "Courier New",
                     "JetBrains Mono", "Fira Code", "Source Code Pro", "Segoe UI Mono"
                 })
        {
            FontFamilies.Add(font);
        }
    }

    private void BuildSchemes()
    {
        foreach (var scheme in ColorSchemeCatalog.All)
        {
            SchemeTiles.Add(new SchemeTileViewModel
            {
                Scheme = scheme,
                IsSelected = scheme.Id == SchemeId
            });
        }
    }

    private void RebuildGroups(IEnumerable<SessionDefinition> sessions)
    {
        var list = sessions.ToList();
        SessionGroups.Clear();
        foreach (var group in list.GroupBy(s => s.Group ?? "Sessions").OrderBy(g => g.Key))
        {
            SessionGroups.Add(new SessionGroupViewModel(group.Key, group.OrderBy(s => s.Name)));
        }

        // Persist local shell profile catalog (ssh sessions live in ssh-sessions.json).
        _ = _sessionStore.SaveAsync(list.Where(s => s.Kind == SessionKind.LocalShell));
    }

    private void FilterGroups(string query)
    {
        foreach (var group in SessionGroups) group.ApplyFilter(query);
    }

    private static SessionDefinition Clone(SessionDefinition source) => new()
    {
        Id = Guid.NewGuid(),
        Name = source.Name,
        Kind = source.Kind,
        Host = source.Host,
        Port = source.Port,
        Username = source.Username,
        Group = source.Group,
        Shell = source.Shell,
        CustomCommand = source.CustomCommand,
        WorkingDirectory = source.WorkingDirectory,
        SerialPort = source.SerialPort,
        BaudRate = source.BaudRate,
        Extra = new Dictionary<string, string>(source.Extra),
        SshSessionId = source.SshSessionId
    };
}

public sealed class KeybindingRow : ObservableObject
{
    public string CommandId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string DefaultGesture { get; set; } = "";
    private string _gesture = "";
    public string Gesture
    {
        get => _gesture;
        set => SetProperty(ref _gesture, value);
    }
    private string _conflictWith = "";
    public string ConflictWith
    {
        get => _conflictWith;
        set => SetProperty(ref _conflictWith, value);
    }
}

public sealed class KnownHostRow
{
    public string HostPort { get; set; } = "";
    public string Fingerprint { get; set; } = "";
}

public partial class SessionGroupViewModel : ObservableObject
{
    private readonly List<SessionDefinition> _all;
    public string Name { get; }
    public ObservableCollection<SessionDefinition> Sessions { get; } = new();

    public SessionGroupViewModel(string name, IEnumerable<SessionDefinition> sessions)
    {
        Name = name;
        _all = sessions.ToList();
        foreach (var session in _all) Sessions.Add(session);
    }

    public void ApplyFilter(string query)
    {
        Sessions.Clear();
        var q = query?.Trim() ?? string.Empty;
        foreach (var session in _all)
        {
            if (string.IsNullOrEmpty(q) ||
                session.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (session.Host?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                Sessions.Add(session);
            }
        }
    }
}
