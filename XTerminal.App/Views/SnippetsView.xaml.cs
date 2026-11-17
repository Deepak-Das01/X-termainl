using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XTerminal.App.ViewModels;
using XTerminal.Core.Models.Snippets;

namespace XTerminal.App.Views;

public partial class SnippetsView : UserControl
{
    public SnippetsView() => InitializeComponent();

    private SnippetsViewModel? Vm => DataContext as SnippetsViewModel;

    private void Tree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SnippetTreeNode node)
            Vm?.SelectTreeNode(node);
    }

    private void Grid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        Vm?.InsertSnippetCommand.Execute(Vm.SelectedSnippet);

    private void Grid_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null && dep is not DataGridRow)
            dep = VisualTreeHelper.GetParent(dep);
        if (dep is DataGridRow { Item: SnippetRecord item })
        {
            grid.SelectedItem = item;
            if (Vm is not null) Vm.SelectedSnippet = item;
        }
    }

    private void View_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm?.SelectedSnippet is null) return;
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Vm.RunSnippetCommand.Execute(Vm.SelectedSnippet);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Vm.InsertSnippetCommand.Execute(Vm.SelectedSnippet);
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            _ = Vm.EditSnippetCommand.ExecuteAsync(Vm.SelectedSnippet);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            _ = Vm.DeleteSnippetCommand.ExecuteAsync(Vm.SelectedSnippet);
            e.Handled = true;
        }
    }

    private void CtxInsert_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.InsertSnippetCommand.Execute(Vm.SelectedSnippet);
    private void CtxRun_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.RunSnippetCommand.Execute(Vm.SelectedSnippet);
    private void CtxEdit_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.EditSnippetCommand.ExecuteAsync(Vm.SelectedSnippet);
    private void CtxDup_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.DuplicateSnippetCommand.ExecuteAsync(Vm.SelectedSnippet);
    private void CtxFav_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.ToggleFavoriteCommand.ExecuteAsync(Vm.SelectedSnippet);
    private void CtxDel_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.DeleteSnippetCommand.ExecuteAsync(Vm.SelectedSnippet);
}
