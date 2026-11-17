using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XTerminal.App.ViewModels;

namespace XTerminal.App.Views;

public partial class PortsView : UserControl
{
    public PortsView() => InitializeComponent();

    private PortForwardingViewModel? Vm => DataContext as PortForwardingViewModel;

    private void Grid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        _ = Vm?.EditForwardCommand.ExecuteAsync(Vm.SelectedForward);

    private void Grid_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null && dep is not DataGridRow)
            dep = VisualTreeHelper.GetParent(dep);
        if (dep is DataGridRow { Item: PortForwardRow row })
        {
            grid.SelectedItem = row;
            if (Vm is not null) Vm.SelectedForward = row;
        }
    }

    private void View_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm?.SelectedForward is null) return;
        if (e.Key == Key.Enter)
        {
            _ = Vm.StartCommand.ExecuteAsync(Vm.SelectedForward);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            _ = Vm.DeleteCommand.ExecuteAsync(Vm.SelectedForward);
            e.Handled = true;
        }
    }

    private void CtxStart_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.StartCommand.ExecuteAsync(Vm.SelectedForward);
    private void CtxStop_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.StopCommand.ExecuteAsync(Vm.SelectedForward);
    private void CtxRestart_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.RestartCommand.ExecuteAsync(Vm.SelectedForward);
    private void CtxEdit_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.EditForwardCommand.ExecuteAsync(Vm.SelectedForward);
    private void CtxSession_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.OpenSessionCommand.ExecuteAsync(Vm.SelectedForward);
    private void CtxDelete_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.DeleteCommand.ExecuteAsync(Vm.SelectedForward);
}
