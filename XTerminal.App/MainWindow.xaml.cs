using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using XTerminal.App.Helpers;
using XTerminal.App.Services;
using XTerminal.App.ViewModels;
using XTerminal.App.Views;
using XTerminal.Core.Models;
using XTerminal.Core.Security;
using XTerminal.Core.Services;
using XTerminal.Core.Settings;
using ActivitySection = XTerminal.Core.Settings.ActivitySection;
using XTerminal.Protocols;
using XTerminal.Protocols.Ssh;

namespace XTerminal.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ThemeService _themeService = new();
    private readonly ISecretStore _secrets = new WindowsCredentialStore();
    private readonly ISshSessionLibrary _sshLibrary = new JsonSshSessionLibrary();
    private readonly KnownHostsStore _knownHosts = new();
    private SshHostKeyPolicy? _hostKeyPolicy;
    private SshConnectionManager? _connectionManager;

    public MainWindow()
    {
        InitializeComponent();
        WindowBackdrop.ApplyMica(this);

        var connectionManager = new SshConnectionManager();
        var hostKeyPolicy = new SshHostKeyPolicy(_knownHosts, new WpfHostKeyPrompt());
        var hostFactory = new SessionHostFactory(_secrets, _sshLibrary, hostKeyPolicy, connectionManager);
        var uiState = new JsonUiStateStore();
        var sessionsVm = new SessionsViewModel(_sshLibrary, _secrets, hostKeyPolicy, uiState);
        sessionsVm.EditRequested += OnEditSessionRequested;

        var transportFactory = new SshTransportFactory(_secrets, hostKeyPolicy);
        var sftpService = new SftpSessionService(_sshLibrary, transportFactory);
        var sftpVm = new SftpWorkspaceViewModel(sftpService);
        var portRuntime = new PortForwardRuntimeService(transportFactory, connectionManager);
        _hostKeyPolicy = hostKeyPolicy;
        _connectionManager = connectionManager;

        _viewModel = new MainViewModel(
            new JsonSessionStore(),
            new JsonSettingsStore(),
            hostFactory,
            sessionsVm,
            sftpVm,
            new FilesExplorerViewModel(uiState),
            new PortForwardingViewModel(new JsonPortForwardStore(), _sshLibrary, portRuntime),
            new SnippetsViewModel(new JsonSnippetStore()),
            new HistoryViewModel(new JsonHistoryStore()));
        DataContext = _viewModel;

        _viewModel.ThemeChanged += OnThemeChanged;
        _viewModel.AppearanceChanged += OnAppearanceChanged;
        _viewModel.EditorAppearanceChanged += OnEditorAppearanceChanged;

        StateChanged += (_, _) => ApplyWindowStateLayout();
        SizeChanged += (_, _) =>
        {
            ApplyWindowStateLayout();
            TerminalView?.RequestFit();
        };

        EditorViewHost.SaveRequested += () => _ = _viewModel.SaveEditorCommand.ExecuteAsync(null);
        TerminalView.InputReceived += OnTerminalInput;
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        MainViewModel.TerminalActions.Clear = () => TerminalView.ClearActive();
        MainViewModel.TerminalActions.Find = () => TerminalView.RunSearch();
        MainViewModel.TerminalActions.Copy = () => TerminalView.CopyActive();
        MainViewModel.TerminalActions.Paste = () => TerminalView.PasteActive();
        MainViewModel.TerminalActions.SelectAll = () => TerminalView.SelectAllActive();
        Closing += OnWindowClosing;

        Loaded += async (_, _) =>
        {
            AppLog.Info(LogCategory.App, "Startup begin");
            ApplyWindowStateLayout();
            await _viewModel.InitializeAsync();
            OnThemeChanged();
            OnAppearanceChanged();
            OnEditorAppearanceChanged();
            TerminalView?.RequestFit();
            AppLog.Info(LogCategory.App, "Startup complete");
        };

        Closed += async (_, _) =>
        {
            _viewModel.ThemeChanged -= OnThemeChanged;
            _viewModel.AppearanceChanged -= OnAppearanceChanged;
            _viewModel.EditorAppearanceChanged -= OnEditorAppearanceChanged;
            TerminalView.InputReceived -= OnTerminalInput;
            await sftpService.DisposeAsync();
            await portRuntime.DisposeAsync();
            foreach (var tab in _viewModel.Tabs.ToList())
            {
                if (tab is IAsyncDisposable d) await d.DisposeAsync();
            }
            if (_connectionManager is not null)
                await _connectionManager.DisposeAsync();
        };
    }

    private void OnTerminalInput(TerminalTabViewModel tab, string data) =>
        _viewModel.HistoryTracker.OnTerminalInput(tab, data);

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_viewModel.CurrentSettings.General.ConfirmExitWithActiveSessions)
            return;

        var active = _viewModel.Tabs.OfType<TerminalGroupTabViewModel>()
            .SelectMany(g => g.AllPanes)
            .Count(p => p.ConnectionState is ConnectionState.Connected or ConnectionState.Connecting);
        if (active <= 0) return;

        var result = MessageBox.Show(
            $"There {(active == 1 ? "is" : "are")} {active} active terminal session{(active == 1 ? "" : "s")}. Exit anyway?",
            "Confirm exit",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    private async void OnEditSessionRequested(SshSessionEditorViewModel editor, bool isNew)
    {
        var policy = _hostKeyPolicy ?? new SshHostKeyPolicy(_knownHosts, new WpfHostKeyPrompt());
        var modalEditor = new SshSessionEditorViewModel(
            _secrets,
            policy,
            isNew ? null : editor.ToRecord());
        if (isNew)
            modalEditor.ApplyAppSshDefaults(_viewModel.CurrentSettings.Ssh);
        modalEditor.SetFolderOptions(_viewModel.Sessions.GetFolderOptions());

        var window = new NewSshSessionWindow(modalEditor) { Owner = this };
        if (window.ShowDialog() == true && window.Saved)
        {
            await _viewModel.Sessions.SaveEditorAsync(modalEditor, isNew);
            if (window.ConnectAfterSave)
            {
                var record = _viewModel.Sessions.FindSession(modalEditor.SessionId) ?? modalEditor.ToRecord();
                await _viewModel.ConnectSshCommand.ExecuteAsync(record);
            }
        }
    }

    private void OnThemeChanged() => _themeService.Apply(this, _viewModel.SnapshotSettings());

    private void OnAppearanceChanged() =>
        TerminalView?.ApplyAppearance(_viewModel.SnapshotSettings().Terminal);

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsCommandPaletteOpen) or nameof(MainViewModel.PaletteQuery))
        {
            if (_viewModel.IsCommandPaletteOpen)
            {
                RefreshPaletteList();
                Dispatcher.BeginInvoke(() => { PaletteInput.Focus(); PaletteInput.SelectAll(); });
            }
        }

        if (e.PropertyName is nameof(MainViewModel.IsQuickSwitcherOpen) or nameof(MainViewModel.SwitcherQuery))
        {
            if (_viewModel.IsQuickSwitcherOpen)
            {
                RefreshSwitcherList();
                Dispatcher.BeginInvoke(() => { SwitcherInput.Focus(); SwitcherInput.SelectAll(); });
            }
        }
    }

    private void OnEditorAppearanceChanged() =>
        EditorViewHost?.ApplyOptions(_viewModel.SnapshotSettings().Editor);

    private void ApplyWindowStateLayout()
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        if (WindowState == WindowState.Maximized)
        {
            var border = SystemParameters.WindowResizeBorderThickness;
            RootLayout.Margin = new Thickness(Math.Max(border.Left, 6), Math.Max(border.Top, 4),
                Math.Max(border.Right, 6), Math.Max(border.Bottom, 6));
            TitleBarRow.Height = new GridLength(44);
            if (chrome is not null) chrome.CaptionHeight = 44;
            TitleBarContent.Margin = new Thickness(4, 0, 180, 0);
        }
        else
        {
            RootLayout.Margin = new Thickness(0);
            TitleBarRow.Height = new GridLength(40);
            if (chrome is not null) chrome.CaptionHeight = 40;
            TitleBarContent.Margin = new Thickness(0, 0, 168, 0);
        }

        TerminalView?.RequestFit();
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            var point = PointToScreen(e.GetPosition(this));
            var ratio = e.GetPosition(this).X / Math.Max(ActualWidth, 1);
            WindowState = WindowState.Normal;
            Left = point.X - (Width * ratio);
            Top = Math.Max(point.Y - 20, 0);
        }

        try { DragMove(); e.Handled = true; } catch { /* ignore */ }
    }

    private void Tab_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null) return;
        if (sender is FrameworkElement { DataContext: IWorkspaceTab tab })
        {
            _viewModel.SelectedTab = tab;
            _draggingTab = tab;
            _tabDragStart = e.GetPosition(null);
            e.Handled = true;
        }
    }

    private void Tab_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        if (sender is FrameworkElement { DataContext: IWorkspaceTab tab })
        {
            _ = _viewModel.CloseTabCommand.ExecuteAsync(tab);
            e.Handled = true;
        }
    }

    private async void CloseTabMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResolveTabFromMenu(sender) is { } tab)
            await _viewModel.CloseTabCommand.ExecuteAsync(tab);
    }

    private async void CloseOtherTabs_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResolveTabFromMenu(sender) is not { } keep) return;
        foreach (var tab in _viewModel.Tabs.Where(t => !ReferenceEquals(t, keep)).ToList())
            await _viewModel.CloseTabCommand.ExecuteAsync(tab);
    }

    private static IWorkspaceTab? ResolveTabFromMenu(object sender)
    {
        if (sender is not MenuItem { Parent: ContextMenu menu }) return null;
        return menu.PlacementTarget is FrameworkElement { DataContext: IWorkspaceTab tab } ? tab : null;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void ProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        ProfilePopup.IsOpen = !ProfilePopup.IsOpen;
        e.Handled = true;
    }

    private async void ProfileItem_OnClick(object sender, RoutedEventArgs e)
    {
        ProfilePopup.IsOpen = false;
        if (sender is Button { Tag: ProfileItem profile } &&
            _viewModel.OpenProfileCommand.CanExecute(profile))
        {
            await _viewModel.OpenProfileCommand.ExecuteAsync(profile);
        }
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.IsRecordingKeybinding)
            return;

        if (e.Key == Key.Escape)
        {
            ProfilePopup.IsOpen = false;
            if (_viewModel.IsCommandPaletteOpen || _viewModel.IsQuickSwitcherOpen)
            {
                _viewModel.CloseOverlaysCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        if (_viewModel.Keybindings?.TryMatch(e.Key, Keyboard.Modifiers, out var commandId) == true &&
            _viewModel.Commands.Commands.FirstOrDefault(c => c.Id == commandId) is { } matched &&
            (matched.CanExecute?.Invoke() ?? true))
        {
            matched.Execute();
            _viewModel.Commands.MarkUsed(commandId);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _viewModel.OpenCommandPaletteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F1)
        {
            _viewModel.OpenCommandPaletteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.OpenQuickSwitcherCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.T && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _ = _viewModel.ReopenClosedTabCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _ = _viewModel.CloseTabCommand.ExecuteAsync(_viewModel.SelectedTab);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.W && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _ = _viewModel.CloseActivePaneCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right && Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift))
        {
            _ = _viewModel.SplitRightCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift))
        {
            _ = _viewModel.SplitDownCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Alt &&
            e.Key is Key.Left or Key.Right or Key.Up or Key.Down &&
            _viewModel.IsTerminalView &&
            !_viewModel.IsEditorTabSelected &&
            _viewModel.SelectedTerminalGroup is { PaneCount: > 1 } group)
        {
            group.FocusNextPane(reverse: e.Key is Key.Left or Key.Up);
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.OemPlus || e.Key == Key.Add) && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ZoomTerminal(1);
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.OemMinus || e.Key == Key.Subtract) && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ZoomTerminal(-1);
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.D0 || e.Key == Key.NumPad0) && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ZoomTerminal(0, reset: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_viewModel.IsFilesView)
            {
                // Let FilesView.PreviewKeyDown focus the filter box.
                return;
            }

            if (_viewModel.IsHistoryView)
            {
                // Let HistoryView.PreviewKeyDown focus search.
                return;
            }

            if (_viewModel.IsEditorTabSelected)
            {
                EditorViewHost.RunCommand("find");
                e.Handled = true;
                return;
            }

            if (_viewModel.IsTerminalTabSelected && _viewModel.IsTerminalView)
            {
                TerminalView.RunSearch();
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.N && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _viewModel.NewUntitledEditorCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _ = _viewModel.OpenLocalFileCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _ = _viewModel.SaveEditorCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _ = _viewModel.SaveEditorAsCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        if (_viewModel.IsEditorTabSelected)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                EditorViewHost.RunCommand("find");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
            {
                EditorViewHost.RunCommand("replace");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
            {
                EditorViewHost.RunCommand("gotoLine");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                EditorViewHost.RunCommand("toggleWordWrap");
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CycleTab(reverse: false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            CycleTab(reverse: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.OpenSettingsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.I && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            var item = _viewModel.Activities.FirstOrDefault(a => a.Section == ActivitySection.Snippets);
            if (item is not null) _viewModel.SelectActivityCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void CycleTab(bool reverse)
    {
        if (_viewModel.Tabs.Count == 0) return;
        var idx = _viewModel.SelectedTab is null ? 0 : _viewModel.Tabs.IndexOf(_viewModel.SelectedTab);
        if (idx < 0) idx = 0;
        idx = reverse
            ? (idx - 1 + _viewModel.Tabs.Count) % _viewModel.Tabs.Count
            : (idx + 1) % _viewModel.Tabs.Count;
        _viewModel.SelectedTab = _viewModel.Tabs[idx];
        _viewModel.ActiveSection = ActivitySection.Terminal;
    }

    private async void ZoomTerminal(double delta, bool reset = false)
    {
        var pane = _viewModel.SelectedTerminalTab;
        if (pane is null) return;
        if (reset) pane.ResetZoom();
        else pane.AdjustZoom(delta);
        var size = pane.ZoomFontSize <= 0 ? _viewModel.FontSize : pane.ZoomFontSize;
        TerminalView.ApplyAppearance(new Core.Settings.TerminalAppearanceSettings
        {
            FontFamily = _viewModel.FontFamily,
            FontSize = size,
            SchemeId = _viewModel.SchemeId,
            CursorStyle = _viewModel.CursorStyle,
            CursorBlink = _viewModel.CursorBlink
        });
        // Apply to active host zoom message as well
        _viewModel.ZoomToast = $"Terminal zoom: {size:0}px";
        _viewModel.ShowZoomToast = true;
        await Task.Delay(1200);
        _viewModel.ShowZoomToast = false;
    }
}
