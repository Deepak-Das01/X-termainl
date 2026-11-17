using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XTerminal.App.Services;
using XTerminal.App.ViewModels;

namespace XTerminal.App;

public partial class MainWindow
{
    private Point _tabDragStart;
    private IWorkspaceTab? _draggingTab;

    private void PinTab_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResolveTabFromMenu(sender) is { } tab)
            _viewModel.PinTabCommand.Execute(tab);
    }

    private void DuplicateTab_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResolveTabFromMenu(sender) is { } tab)
            _ = _viewModel.DuplicateTabCommand.ExecuteAsync(tab);
    }

    private void RenameTab_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResolveTabFromMenu(sender) is { } tab)
            _viewModel.RenameTabCommand.Execute(tab);
    }

    private void CloseTabsToRight_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResolveTabFromMenu(sender) is { } tab)
            _ = _viewModel.CloseTabsToRightCommand.ExecuteAsync(tab);
    }

    private void CloseUnpinned_OnClick(object sender, RoutedEventArgs e) =>
        _ = _viewModel.CloseUnpinnedTabsCommand.ExecuteAsync(null);

    private void Tab_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggingTab is null) return;
        if (sender is not FrameworkElement fe) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _tabDragStart.X) < 4 && Math.Abs(pos.Y - _tabDragStart.Y) < 4) return;
        DragDrop.DoDragDrop(fe, _draggingTab, DragDropEffects.Move);
    }

    private void Tab_OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(IWorkspaceTab)) is not IWorkspaceTab source) return;
        if (sender is not FrameworkElement { DataContext: IWorkspaceTab target }) return;
        if (ReferenceEquals(source, target)) return;

        var from = _viewModel.Tabs.IndexOf(source);
        var to = _viewModel.Tabs.IndexOf(target);
        if (from < 0 || to < 0) return;

        // Keep pinned tabs before unpinned.
        if (source.IsPinned != target.IsPinned) return;

        _viewModel.Tabs.Move(from, to);
        _viewModel.SelectedTab = source;
    }

    private void TabListButton_OnClick(object sender, RoutedEventArgs e)
    {
        TabListPopup.PlacementTarget = sender as UIElement;
        RefreshTabList(TabListSearch.Text);
        TabListPopup.IsOpen = true;
        TabListSearch.Focus();
    }

    private void TabListSearch_OnTextChanged(object sender, TextChangedEventArgs e) =>
        RefreshTabList(TabListSearch.Text);

    private void RefreshTabList(string? query)
    {
        query ??= string.Empty;
        TabListBox.Items.Clear();
        foreach (var tab in _viewModel.Tabs)
        {
            if (!string.IsNullOrWhiteSpace(query) &&
                !tab.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            TabListBox.Items.Add(tab);
        }

        TabListBox.DisplayMemberPath = nameof(IWorkspaceTab.Title);
    }

    private void TabListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TabListBox.SelectedItem is IWorkspaceTab tab)
        {
            _viewModel.SelectedTab = tab;
            TabListPopup.IsOpen = false;
        }
    }

    private void OverlayBackdrop_OnClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.CloseOverlaysCommand.Execute(null);
        e.Handled = true;
    }

    private void OverlayPanel_OnMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void RefreshPaletteList()
    {
        PaletteList.Items.Clear();
        foreach (var cmd in _viewModel.Commands.Search(_viewModel.PaletteQuery))
        {
            PaletteList.Items.Add($"{cmd.Category}: {cmd.Title}" +
                                  (string.IsNullOrEmpty(cmd.Shortcut) ? "" : $"    {cmd.Shortcut}"));
            // store command via Tag on container — use parallel list
        }

        _paletteCommands = _viewModel.Commands.Search(_viewModel.PaletteQuery).ToList();
        PaletteList.Items.Clear();
        foreach (var cmd in _paletteCommands)
            PaletteList.Items.Add($"{cmd.Category}: {cmd.Title}" +
                                  (string.IsNullOrEmpty(cmd.Shortcut) ? "" : $"\t{cmd.Shortcut}"));
        if (PaletteList.Items.Count > 0) PaletteList.SelectedIndex = 0;
    }

    private List<AppCommand> _paletteCommands = new();
    private List<QuickSwitchItem> _switcherItems = new();

    private void PaletteInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.CloseOverlaysCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            if (PaletteList.SelectedIndex < PaletteList.Items.Count - 1)
                PaletteList.SelectedIndex++;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            if (PaletteList.SelectedIndex > 0) PaletteList.SelectedIndex--;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            ExecuteSelectedPaletteCommand();
            e.Handled = true;
        }
    }

    private void PaletteList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        ExecuteSelectedPaletteCommand();

    private void ExecuteSelectedPaletteCommand()
    {
        var idx = PaletteList.SelectedIndex;
        if (idx < 0 || idx >= _paletteCommands.Count) return;
        var cmd = _paletteCommands[idx];
        _viewModel.Commands.MarkUsed(cmd.Id);
        _viewModel.CloseOverlaysCommand.Execute(null);
        cmd.Execute();
    }

    private void RefreshSwitcherList()
    {
        _switcherItems = BuildSwitcherItems(_viewModel.SwitcherQuery).ToList();
        SwitcherList.Items.Clear();
        foreach (var item in _switcherItems)
            SwitcherList.Items.Add($"{item.Kind,-12}  {item.Title}    {item.Subtitle}");
        if (SwitcherList.Items.Count > 0) SwitcherList.SelectedIndex = 0;
    }

    private IEnumerable<QuickSwitchItem> BuildSwitcherItems(string query)
    {
        query = (query ?? string.Empty).Trim().ToLowerInvariant();
        var items = new List<QuickSwitchItem>();

        foreach (var tab in _viewModel.Tabs)
        {
            items.Add(new QuickSwitchItem
            {
                Kind = "Open Tab",
                Title = tab.Title,
                Subtitle = tab.IsEditor ? "Editor" : "Terminal",
                Activate = () => _viewModel.SelectedTab = tab
            });
        }

        foreach (var s in _viewModel.Sessions.FlatSessions)
        {
            var session = s;
            items.Add(new QuickSwitchItem
            {
                Kind = "SSH Session",
                Title = session.Name,
                Subtitle = $"{session.Username}@{session.Host}",
                Activate = () => _ = _viewModel.ConnectSshCommand.ExecuteAsync(session)
            });
        }

        foreach (var sn in _viewModel.Snippets.FlatSnippets)
        {
            var snippet = sn;
            items.Add(new QuickSwitchItem
            {
                Kind = "Snippet",
                Title = snippet.Name,
                Subtitle = snippet.Command,
                Activate = () => _viewModel.Snippets.InsertSnippetCommand.Execute(snippet)
            });
        }

        if (string.IsNullOrEmpty(query)) return items.Take(40);
        return items.Where(i => i.SearchText.Contains(query)).Take(40);
    }

    private void SwitcherInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.CloseOverlaysCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            if (SwitcherList.SelectedIndex < SwitcherList.Items.Count - 1)
                SwitcherList.SelectedIndex++;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            if (SwitcherList.SelectedIndex > 0) SwitcherList.SelectedIndex--;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            ExecuteSelectedSwitcher();
            e.Handled = true;
        }
    }

    private void SwitcherList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        ExecuteSelectedSwitcher();

    private void ExecuteSelectedSwitcher()
    {
        var idx = SwitcherList.SelectedIndex;
        if (idx < 0 || idx >= _switcherItems.Count) return;
        var item = _switcherItems[idx];
        _viewModel.CloseOverlaysCommand.Execute(null);
        item.Activate();
    }
}
