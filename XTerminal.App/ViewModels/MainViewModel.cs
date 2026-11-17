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
    private bool _suppressPersist;

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
    public ObservableCollection<ShellProfileSettings> ShellProfiles { get; } = new();
    public ObservableCollection<KnownHostRow> KnownHosts { get; } = new();
    public KeybindingService? Keybindings { get; private set; }
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

        if (settings.General.OpenDefaultShellOnStartup)
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

    private async Task OpenSessionCoreAsync(SessionDefinition session)
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
        group.StatusChanged += UpdateStatus;
        InsertTabOrdered(group);
        SelectedTab = group;
        await group.StartAsync();
        UpdateStatus();
        NotifyTabSelection();
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

            var session = await _remoteEditor.OpenAsync(Sftp.Service, remotePath);
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
                    var result = MessageBox.Show(
                        conflict.Message + "\n\nOverwrite the remote file?",
                        "Remote conflict",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes) return false;
                    await _remoteEditor.UploadAtomicAsync(Sftp.Service, tab.Remote, tab.Content, overwriteConflict: true);
                    tab.MarkRemoteSaved(tab.Remote);
                    return true;
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
            StatusDirectory = terminal.Definition.WorkingDirectory ?? Environment.CurrentDirectory;
            StatusEncoding = "UTF-8";
            StatusTerminalType = terminal.Definition.Kind == SessionKind.Ssh ? "ssh / xterm-256color" : "xterm-256color";
            StatusHost = terminal.RemoteHost ?? terminal.Definition.Host ?? "local";
            StatusUser = terminal.RemoteUser ?? terminal.Definition.Username ?? Environment.UserName;
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
        SyncCommandShortcuts();
        OnPropertyChanged(nameof(CurrentSettings));
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
        foreach (var cmd in Commands.Commands.OrderBy(c => c.Category).ThenBy(c => c.Title))
        {
            KeybindingRows.Add(new KeybindingRow
            {
                CommandId = cmd.Id,
                Title = cmd.Title,
                Category = cmd.Category.ToString(),
                Gesture = Keybindings.GetGesture(cmd.Id)
            });
        }
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
        var text =
            $"X-Terminal {AboutVersion}\n{AboutRuntime}\nAppData: {AppDataDirectory}\n" +
            $"Tabs: {Tabs.Count}\nTheme: {ThemeMode}\n";
        try { Clipboard.SetText(text); } catch { /* ignore */ }
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
        Keybindings?.ResetAll();
        RefreshKeybindingRows();
        _ = PersistAsync();
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
            new AppCommand { Id = "ports.new", Title = "New Forward", Category = CommandCategory.Ports, Execute = () => _ = Ports.NewForwardCommand.ExecuteAsync(null) },
            new AppCommand { Id = "ports.start", Title = "Start Selected Forward", Category = CommandCategory.Ports, Execute = () => _ = Ports.StartCommand.ExecuteAsync(Ports.SelectedForward), CanExecute = () => Ports.SelectedForward is not null },
            new AppCommand { Id = "ports.stop", Title = "Stop Selected Forward", Category = CommandCategory.Ports, Execute = () => _ = Ports.StopCommand.ExecuteAsync(Ports.SelectedForward), CanExecute = () => Ports.SelectedForward is not null },
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
    private string _gesture = "";
    public string Gesture
    {
        get => _gesture;
        set => SetProperty(ref _gesture, value);
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
