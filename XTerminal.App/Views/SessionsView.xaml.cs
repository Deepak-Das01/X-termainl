using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XTerminal.App.ViewModels;
using XTerminal.Core.Models.Ssh;

namespace XTerminal.App.Views;

public partial class SessionsView : UserControl
{
    public SessionsView()
    {
        InitializeComponent();
    }

    private SessionsViewModel? Vm => DataContext as SessionsViewModel;

    private void Tree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Vm is null) return;
        var node = e.NewValue as SessionTreeNode;
        Vm.SelectedSession = node?.Session;
        Vm.SelectedFolder = node?.Folder;
    }

    private void Tree_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => ConnectSelected();

    private void Grid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => ConnectSelected();

    private void ConnectSelected()
    {
        if (Vm?.SelectedSession is null) return;
        if (Vm.ConnectCommand.CanExecute(Vm.SelectedSession))
        {
            Vm.ConnectCommand.Execute(Vm.SelectedSession);
        }
    }

    private async void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedSession is null) return;
        var name = Vm.SelectedSession.Name;
        var result = MessageBox.Show(
            $"Delete SSH session '{name}'?\n\nStored credentials for this session will also be removed.",
            "Confirm delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        await Vm.DeleteSessionCommand.ExecuteAsync(Vm.SelectedSession);
    }

    private void View_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is null) return;

        if (e.Key == Key.Enter && Vm.SelectedSession is not null)
        {
            ConnectSelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2 && Vm.SelectedSession is not null)
        {
            if (Vm.RenameSessionCommand.CanExecute(Vm.SelectedSession))
            {
                _ = Vm.RenameSessionCommand.ExecuteAsync(Vm.SelectedSession);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && Vm.SelectedSession is not null)
        {
            Delete_OnClick(sender, e);
            e.Handled = true;
        }
    }

    private void Export_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.ExportSessionsCommand.ExecuteAsync(null);

    private void Import_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.ImportSessionsCommand.ExecuteAsync(null);

    private void Grid_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null && dep is not DataGridRow)
            dep = VisualTreeHelper.GetParent(dep);
        if (dep is DataGridRow { Item: SshSessionRecord session })
        {
            grid.SelectedItem = session;
            if (Vm is not null) Vm.SelectedSession = session;
        }
    }

    private void CtxConnect_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.ConnectCommand.Execute(Vm.SelectedSession);
    private void CtxSftp_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.OpenSftpCommand.Execute(Vm.SelectedSession);
    private void CtxEdit_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.EditSessionCommand.Execute(Vm.SelectedSession);
    private void CtxDup_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.DuplicateSessionCommand.ExecuteAsync(Vm.SelectedSession);
    private void CtxRename_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.RenameSessionCommand.ExecuteAsync(Vm.SelectedSession);
    private void CtxFav_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.ToggleFavoriteCommand.ExecuteAsync(Vm.SelectedSession);
    private void CtxMove_OnClick(object sender, RoutedEventArgs e) =>
        _ = Vm?.MoveToFolderCommand.ExecuteAsync(Vm.SelectedSession);
    private void CtxCopy_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.CopyHostCommand.Execute(Vm.SelectedSession);
}
