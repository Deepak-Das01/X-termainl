using System.Windows;
using XTerminal.App.ViewModels;

namespace XTerminal.App.Views;

public partial class SnippetEditorWindow : Window
{
    public bool Saved { get; private set; }

    public SnippetEditorWindow(SnippetEditorViewModel editor)
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
        if (DataContext is not SnippetEditorViewModel editor || !editor.Validate()) return;
        Saved = true;
        DialogResult = true;
        Close();
    }
}
