using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XTerminal.App.ViewModels;
using XTerminal.Core.Models;
using XTerminal.Core.Settings;

namespace XTerminal.App.Controls;

public partial class TerminalWorkspaceHost : UserControl
{
    private TerminalGroupTabViewModel? _group;
    private TerminalAppearanceSettings? _appearance;
    private readonly Dictionary<Guid, HostEntry> _hosts = new();
    private readonly Dictionary<Guid, PropertyChangedRelay> _borderRelays = new();

    public event Action<TerminalTabViewModel, string>? InputReceived;

    public TerminalWorkspaceHost()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindGroup(DataContext as TerminalGroupTabViewModel);
        Unloaded += (_, _) => DisposeAllHosts();
    }

    public void ApplyAppearance(TerminalAppearanceSettings settings)
    {
        _appearance = settings;
        foreach (var entry in _hosts.Values)
            entry.Host.ApplyAppearance(settings);
    }

    public void RequestFit()
    {
        foreach (var entry in _hosts.Values)
            entry.Host.RequestFit();
    }

    public void FocusActive()
    {
        if (_group?.ActivePane is null) return;
        FocusPaneHost(_group.ActivePane);
    }

    public void RunSearch()
    {
        var host = FindHost(_group?.ActivePane);
        host?.RunSearch();
    }

    public void ClearActive() => FindHost(_group?.ActivePane)?.Clear();
    public void CopyActive() => FindHost(_group?.ActivePane)?.CopySelection();
    public void PasteActive() => FindHost(_group?.ActivePane)?.PasteText();
    public void SelectAllActive() => FindHost(_group?.ActivePane)?.SelectAll();

    private void BindGroup(TerminalGroupTabViewModel? group)
    {
        if (_group is not null)
        {
            _group.LayoutChanged -= Rebuild;
            _group.PropertyChanged -= Group_OnPropertyChanged;
        }

        // Switching workspace tabs: detach hosts from visual tree but keep them for same group remap.
        DetachAllFromVisualTree();
        if (!ReferenceEquals(_group, group))
        {
            // Different group — dispose previous hosts (sessions live on the other VM).
            DisposeAllHosts();
        }

        _group = group;
        if (_group is null)
        {
            RootHost.Content = null;
            BroadcastBanner.Visibility = Visibility.Collapsed;
            return;
        }

        _group.LayoutChanged += Rebuild;
        _group.PropertyChanged += Group_OnPropertyChanged;
        Rebuild();
        UpdateBroadcastBanner();
    }

    private void Group_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TerminalGroupTabViewModel.BroadcastEnabled)
            or nameof(TerminalGroupTabViewModel.BroadcastCount))
        {
            UpdateBroadcastBanner();
            return;
        }

        if (e.PropertyName == nameof(TerminalGroupTabViewModel.ActivePane))
        {
            RefreshActiveBorders();
            if (_group?.IsMaximized == true)
                Rebuild();
            return;
        }

        if (e.PropertyName == nameof(TerminalGroupTabViewModel.IsMaximized))
            Rebuild();
    }

    private void Rebuild()
    {
        if (_group is null) return;

        // Detach existing hosts so they can be re-parented (WPF allows one parent).
        DetachAllFromVisualTree();

        // Drop hosts for panes that no longer exist.
        var liveIds = new HashSet<Guid>(_group.AllPanes.Select(p => p.Id));
        foreach (var id in _hosts.Keys.ToList())
        {
            if (!liveIds.Contains(id))
                DisposeHost(id);
        }

        var node = _group.IsMaximized && _group.MaximizedLeaf is not null
            ? _group.MaximizedLeaf
            : _group.Root;
        RootHost.Content = BuildNode(node);
        if (_appearance is not null) ApplyAppearance(_appearance);
        Dispatcher.BeginInvoke(RequestFit, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private FrameworkElement BuildNode(SplitNode node)
    {
        if (node is LeafNode leaf)
        {
            var entry = GetOrCreateHost(leaf.Session);
            var border = new Border
            {
                Child = entry.Host,
                BorderThickness = new Thickness(1),
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Tag = leaf.Session,
                ContextMenu = BuildPaneContextMenu(leaf.Session)
            };
            UpdateActiveBorder(border, leaf.Session);

            // Replace any prior border relay for this pane.
            if (_borderRelays.TryGetValue(leaf.Session.Id, out var oldRelay) && _group is not null)
            {
                _group.PropertyChanged -= oldRelay.Handler;
                _borderRelays.Remove(leaf.Session.Id);
            }

            if (_group is not null)
            {
                PropertyChangedEventHandler handler = (_, e) =>
                {
                    if (e.PropertyName == nameof(TerminalGroupTabViewModel.ActivePane))
                        UpdateActiveBorder(border, leaf.Session);
                };
                _group.PropertyChanged += handler;
                _borderRelays[leaf.Session.Id] = new PropertyChangedRelay(handler);
            }

            return border;
        }

        if (node is BranchNode branch)
        {
            var grid = new Grid();
            if (branch.Orientation == Orientation.Vertical)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(branch.Ratio, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - branch.Ratio, GridUnitType.Star) });
                var first = BuildNode(branch.First);
                var second = BuildNode(branch.Second);
                Grid.SetColumn(first, 0);
                var splitter = new GridSplitter
                {
                    Width = 4,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Background = (System.Windows.Media.Brush)FindResource("BrushBorderSoft")
                };
                Grid.SetColumn(splitter, 1);
                Grid.SetColumn(second, 2);
                grid.Children.Add(first);
                grid.Children.Add(splitter);
                grid.Children.Add(second);
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(branch.Ratio, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - branch.Ratio, GridUnitType.Star) });
                var first = BuildNode(branch.First);
                var second = BuildNode(branch.Second);
                Grid.SetRow(first, 0);
                var splitter = new GridSplitter
                {
                    Height = 4,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = (System.Windows.Media.Brush)FindResource("BrushBorderSoft")
                };
                Grid.SetRow(splitter, 1);
                Grid.SetRow(second, 2);
                grid.Children.Add(first);
                grid.Children.Add(splitter);
                grid.Children.Add(second);
            }

            return grid;
        }

        return new TextBlock { Text = "Invalid layout" };
    }

    private HostEntry GetOrCreateHost(TerminalTabViewModel pane)
    {
        if (_hosts.TryGetValue(pane.Id, out var existing))
        {
            existing.Host.DataContext = pane;
            return existing;
        }

        var host = new TerminalHost { DataContext = pane, SuppressAutoWrite = true };
        Action<TerminalTabViewModel, string> inputHandler = (tab, data) =>
        {
            if (_group is null) return;
            _group.ActivePane = tab;
            InputReceived?.Invoke(tab, data);
            if (_group.BroadcastEnabled)
                _group.WriteToActiveOrBroadcast(data);
            else
                tab.WriteInput(data);
        };
        MouseButtonEventHandler mouseHandler = (_, _) =>
        {
            if (_group is not null) _group.ActivePane = pane;
        };
        host.InputReceived += inputHandler;
        host.PreviewMouseLeftButtonDown += mouseHandler;
        if (_appearance is not null)
            host.ApplyAppearance(_appearance);

        var entry = new HostEntry(host, inputHandler, mouseHandler);
        _hosts[pane.Id] = entry;
        return entry;
    }

    private void DetachAllFromVisualTree()
    {
        foreach (var entry in _hosts.Values)
        {
            if (entry.Host.Parent is Border border)
                border.Child = null;
            else if (entry.Host.Parent is Panel panel)
                panel.Children.Remove(entry.Host);
            else if (entry.Host.Parent is ContentControl cc)
                cc.Content = null;
        }

        RootHost.Content = null;

        if (_group is not null)
        {
            foreach (var relay in _borderRelays.Values)
                _group.PropertyChanged -= relay.Handler;
        }

        _borderRelays.Clear();
    }

    private void DisposeHost(Guid id)
    {
        if (!_hosts.Remove(id, out var entry)) return;
        entry.Host.InputReceived -= entry.InputHandler;
        entry.Host.PreviewMouseLeftButtonDown -= entry.MouseHandler;
        if (entry.Host.Parent is Border b) b.Child = null;
        // TerminalHost Unloaded will unbind; force unload by removing from tree then dispose WebView if needed.
        entry.Host.DataContext = null;
    }

    private void DisposeAllHosts()
    {
        foreach (var id in _hosts.Keys.ToList())
            DisposeHost(id);
        _hosts.Clear();
        RootHost.Content = null;
    }

    private void RefreshActiveBorders()
    {
        void Walk(object? content)
        {
            switch (content)
            {
                case Border { Tag: TerminalTabViewModel pane } border:
                    UpdateActiveBorder(border, pane);
                    break;
                case Panel panel:
                    foreach (var c in panel.Children) Walk(c);
                    break;
            }
        }

        Walk(RootHost.Content);
    }

    private void UpdateActiveBorder(Border border, TerminalTabViewModel session)
    {
        var active = _group?.ActivePane;
        if (_group?.BroadcastMode == BroadcastMode.SelectedPanes && session.IsBroadcastTarget)
            border.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xD2, 0x99, 0x22));
        else if (ReferenceEquals(active, session))
            border.BorderBrush = (System.Windows.Media.Brush)FindResource("BrushAccent");
        else
            border.BorderBrush = System.Windows.Media.Brushes.Transparent;
    }

    private ContextMenu BuildPaneContextMenu(TerminalTabViewModel pane)
    {
        var menu = new ContextMenu();
        void Add(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) =>
            {
                if (_group is not null) _group.ActivePane = pane;
                action();
            };
            menu.Items.Add(item);
        }

        Add("Split Right", () => _ = _group?.SplitAsync(System.Windows.Controls.Orientation.Vertical));
        Add("Split Down", () => _ = _group?.SplitAsync(System.Windows.Controls.Orientation.Horizontal));
        Add("Duplicate Pane", () => _ = _group?.DuplicateActivePaneAsync());
        menu.Items.Add(new Separator());
        Add(_group?.IsMaximized == true ? "Restore Panes" : "Maximize Pane", () => _group?.ToggleMaximize());
        Add("Close Pane", () => _ = _group?.CloseActivePaneAsync(), enabled: (_group?.PaneCount ?? 0) > 1);
        menu.Items.Add(new Separator());
        Add("Rename", () =>
        {
            var dlg = new Views.InputPromptWindow("Rename pane", pane.Title)
            {
                Owner = Window.GetWindow(this)
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Value))
                pane.Title = dlg.Value.Trim();
        });
        Add("Reconnect", () => _ = pane.ReconnectCommand.ExecuteAsync(null),
            enabled: pane.ConnectionState != ConnectionState.Connected);
        menu.Items.Add(new Separator());
        Add("Copy", () => FindHost(pane)?.CopySelection());
        Add("Paste", () => FindHost(pane)?.PasteText());
        Add("Select All", () => FindHost(pane)?.SelectAll());
        Add("Find", () => FindHost(pane)?.RunSearch());
        Add("Clear Terminal", () => FindHost(pane)?.Clear());
        Add("Reset Zoom", () =>
        {
            pane.ResetZoom();
            if (_appearance is not null) FindHost(pane)?.ApplyAppearance(_appearance);
        });
        menu.Items.Add(new Separator());
        Add("Broadcast: All Panes", () => _group?.SetBroadcastMode(BroadcastMode.AllPanes));
        Add("Broadcast: Selected…", () =>
        {
            _group?.SetBroadcastMode(BroadcastMode.SelectedPanes);
            if (_group is not null && !pane.IsBroadcastTarget)
                _group.ToggleBroadcastTarget(pane);
        });
        Add("Toggle Broadcast Target", () => _group?.ToggleBroadcastTarget(pane),
            enabled: _group?.BroadcastMode == BroadcastMode.SelectedPanes);
        Add("Broadcast: Off", () => _group?.SetBroadcastMode(BroadcastMode.Off));

        return menu;
    }

    private void UpdateBroadcastBanner()
    {
        if (_group?.BroadcastEnabled == true && _group.BroadcastCount > 0)
        {
            BroadcastBanner.Visibility = Visibility.Visible;
            BroadcastText.Text = $"Broadcast Input: {_group.BroadcastCount} panes";
            RootHost.Margin = new Thickness(0, 22, 0, 0);
        }
        else
        {
            BroadcastBanner.Visibility = Visibility.Collapsed;
            RootHost.Margin = new Thickness(0);
        }
    }

    private TerminalHost? FindHost(TerminalTabViewModel? pane)
    {
        if (pane is null) return null;
        return _hosts.TryGetValue(pane.Id, out var e) ? e.Host : null;
    }

    private void FocusPaneHost(TerminalTabViewModel pane) => FindHost(pane)?.Focus();

    private sealed class HostEntry
    {
        public HostEntry(
            TerminalHost host,
            Action<TerminalTabViewModel, string> inputHandler,
            MouseButtonEventHandler mouseHandler)
        {
            Host = host;
            InputHandler = inputHandler;
            MouseHandler = mouseHandler;
        }

        public TerminalHost Host { get; }
        public Action<TerminalTabViewModel, string> InputHandler { get; }
        public MouseButtonEventHandler MouseHandler { get; }
    }

    private sealed class PropertyChangedRelay
    {
        public PropertyChangedRelay(PropertyChangedEventHandler handler) => Handler = handler;
        public PropertyChangedEventHandler Handler { get; }
    }
}
