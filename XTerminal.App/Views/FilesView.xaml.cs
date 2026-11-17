using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XTerminal.App.ViewModels;

namespace XTerminal.App.Views;

public partial class FilesView : UserControl
{
    public FilesView() => InitializeComponent();

    private FilesExplorerViewModel? Vm => DataContext as FilesExplorerViewModel;

    private void PathBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm is not null)
        {
            _ = Vm.NavigateTypedPathCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }

    private void SearchBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm is not null)
        {
            _ = Vm.SearchCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }

    private void Grid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.SelectedItem is not null)
            _ = Vm.OpenEntryCommand.ExecuteAsync(Vm.SelectedItem);
    }

    private async void Tree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Vm is null || e.NewValue is not FolderTreeNode { IsFolder: true, IsPlaceholder: false } node) return;
        if (string.IsNullOrEmpty(node.FullPath)) return;
        await Vm.NavigateToPathAsync(node.FullPath);
    }

    private async void TreeItem_OnExpanded(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not TreeViewItem { DataContext: FolderTreeNode node }) return;
        await Vm.ExpandFolderNodeAsync(node);
    }

    private void TreeItem_OnCollapsed(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not TreeViewItem { DataContext: FolderTreeNode node }) return;
        Vm.RememberCollapsed(node.FullPath);
    }

    private void Grid_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null && dep is not DataGridRow)
            dep = VisualTreeHelper.GetParent(dep);
        if (dep is DataGridRow { Item: LocalFileItem item })
        {
            grid.SelectedItem = item;
            if (Vm is not null) Vm.SelectedItem = item;
        }
    }

    private void View_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is null) return;

        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            PathBox.Focus();
            PathBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && !string.IsNullOrEmpty(Vm.QuickFilter))
        {
            Vm.QuickFilter = string.Empty;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            _ = Vm.GoBackCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            _ = Vm.GoForwardCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            _ = Vm.GoUpCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            _ = Vm.RenameCommand.ExecuteAsync(Vm.SelectedItem);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            _ = Vm.DeleteCommand.ExecuteAsync(Vm.SelectedItem);
            e.Handled = true;
        }
    }

    private void CtxOpen_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.OpenEntryCommand.ExecuteAsync(Vm.SelectedItem);
    private void CtxOpenEditor_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.OpenInEditorCommand.Execute(Vm.SelectedItem);
    private void CtxTerminal_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.OpenTerminalHereCommand.Execute(Vm.SelectedItem);
    private void CtxExplorer_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.RevealInExplorerCommand.Execute(Vm.SelectedItem);
    private void CtxCopyPath_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.CopyPathCommand.Execute(Vm.SelectedItem);
    private void CtxCopyName_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.CopyFileNameCommand.Execute(Vm.SelectedItem);
    private void CtxRename_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.RenameCommand.ExecuteAsync(Vm.SelectedItem);
    private void CtxDelete_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.DeleteCommand.ExecuteAsync(Vm.SelectedItem);
    private void CtxProperties_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.ShowPropertiesCommand.Execute(Vm.SelectedItem);
}
