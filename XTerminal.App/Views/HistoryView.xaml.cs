using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XTerminal.App.ViewModels;
using XTerminal.Core.Models.History;

namespace XTerminal.App.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    private HistoryViewModel? Vm => DataContext as HistoryViewModel;

    private void Grid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        Vm?.InsertCommand.Execute(Vm.SelectedEntry);

    private void Grid_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null && dep is not DataGridRow)
            dep = VisualTreeHelper.GetParent(dep);
        if (dep is DataGridRow { Item: HistoryEntry item })
        {
            grid.SelectedItem = item;
            if (Vm is not null) Vm.SelectedEntry = item;
        }
    }

    private void View_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (Vm?.SelectedEntry is null) return;
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Vm.RunAgainCommand.Execute(Vm.SelectedEntry);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Vm.InsertCommand.Execute(Vm.SelectedEntry);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            _ = Vm.DeleteCommand.ExecuteAsync(Vm.SelectedEntry);
            e.Handled = true;
        }
    }

    private void CtxCopy_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.CopyCommand.Execute(Vm.SelectedEntry);
    private void CtxInsert_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.InsertCommand.Execute(Vm.SelectedEntry);
    private void CtxRun_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.RunAgainCommand.Execute(Vm.SelectedEntry);
    private void CtxSnippet_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.FavoriteAsSnippetCommand.Execute(Vm.SelectedEntry);
    private void CtxDelete_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.DeleteCommand.ExecuteAsync(Vm.SelectedEntry);
}
