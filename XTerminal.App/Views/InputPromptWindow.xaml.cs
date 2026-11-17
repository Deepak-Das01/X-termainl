using System.Windows;

namespace XTerminal.App.Views;

public partial class InputPromptWindow : Window
{
    public string? Value { get; private set; }

    public InputPromptWindow(string prompt, string defaultValue = "", string? details = null)
    {
        InitializeComponent();
        Title = prompt;
        PromptText.Text = string.IsNullOrWhiteSpace(details) ? prompt : $"{prompt}\n{details}";
        InputBox.Text = defaultValue;
        if (!string.IsNullOrWhiteSpace(details)) Height = 200;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        Value = InputBox.Text;
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
