using System.Windows.Controls;
using System.Windows.Input;
using XTerminal.App.ViewModels;

namespace XTerminal.App.Views;

public partial class EditorView : UserControl
{
    public EditorView()
    {
        InitializeComponent();
    }

    private void DocTab_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is EditorWorkspaceViewModel vm &&
            sender is Border { DataContext: EditorDocumentViewModel doc })
        {
            vm.SelectedDocument = doc;
        }
    }
}
