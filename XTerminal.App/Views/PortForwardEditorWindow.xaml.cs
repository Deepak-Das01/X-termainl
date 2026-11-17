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
        if (editor.AllowLan)
        {
            var confirm = MessageBox.Show(
                "Allow LAN access binds the listener on all interfaces (0.0.0.0), not just loopback.\n\n" +
                "Only continue if you intentionally need remote machines on your network to use this tunnel.",
                "Confirm non-loopback bind",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
            editor.BindAddress = "0.0.0.0";
        }
        else if (string.IsNullOrWhiteSpace(editor.BindAddress) ||
                 editor.BindAddress is "0.0.0.0" or "::" or "*")
        {
            editor.BindAddress = "127.0.0.1";
        }

        Saved = true;
        DialogResult = true;
        Close();
    }
}
