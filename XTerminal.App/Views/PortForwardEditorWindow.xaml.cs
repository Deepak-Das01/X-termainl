using System.Windows;
using XTerminal.App.ViewModels;

namespace XTerminal.App.Views;

public partial class PortForwardEditorWindow : Window
{
    public bool Saved { get; private set; }

    public PortForwardEditorWindow(PortForwardEditorViewModel editor)
    {
        InitializeComponent();
        DataContext = editor;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        Saved = false;
        DialogResult = false;
        Close();
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PortForwardEditorViewModel editor || !editor.Validate()) return;
        Saved = true;
        DialogResult = true;
        Close();
    }
}
