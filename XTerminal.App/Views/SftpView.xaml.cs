using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XTerminal.App.ViewModels;

namespace XTerminal.App.Views;

public partial class SftpView : UserControl
{
    public SftpView()
    {
        InitializeComponent();
    }

    private SftpWorkspaceViewModel? Vm => DataContext as SftpWorkspaceViewModel;

    private void OpenSession_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.RequestPickSessionCommand.Execute(null);

    private async void Disconnect_OnClick(object sender, RoutedEventArgs e)
    {
        if (Vm?.DisconnectCommand.CanExecute(null) == true)
        {
            await Vm.DisconnectCommand.ExecuteAsync(null);
        }
    }

    private async void PathBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm is not null)
        {
            await Vm.NavigateTypedPathCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }

    private async void Tree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Vm is null || e.NewValue is not SftpTreeNode node) return;
        await Vm.OpenEntryCommand.ExecuteAsync(new SftpEntryViewModel
        {
            Name = node.Name,
            FullPath = node.FullPath,
            IsDirectory = true
        });
    }

    private async void Tree_OnExpanded(object sender, RoutedEventArgs e)
    {
        if (Vm is null || e.OriginalSource is not TreeViewItem { DataContext: SftpTreeNode node }) return;
        await Vm.ExpandTreeNodeAsync(node);
    }

    private async void Breadcrumb_OnClick(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button { Tag: string path }) return;
        await Vm.OpenEntryCommand.ExecuteAsync(new SftpEntryViewModel
        {
            Name = path,
            FullPath = path,
            IsDirectory = true
        });
    }

    private async void Grid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.SelectedEntry is null) return;
        if (Vm.SelectedEntry.IsDirectory)
        {
            await Vm.OpenEntryCommand.ExecuteAsync(Vm.SelectedEntry);
        }
        else if (Vm.OpenInEditorCommand.CanExecute(null))
        {
            Vm.OpenInEditorCommand.Execute(null);
        }
    }
}
