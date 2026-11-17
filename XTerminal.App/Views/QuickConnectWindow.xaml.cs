using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using XTerminal.Core.Settings;

namespace XTerminal.App.Views;

public partial class QuickConnectWindow : Window
{
    public QuickConnectResult? Result { get; private set; }

    public QuickConnectWindow() : this(null)
    {
    }

    public QuickConnectWindow(SshAppSettings? sshDefaults)
    {
        InitializeComponent();
        if (sshDefaults is null) return;
        if (sshDefaults.DefaultPort is >= 1 and <= 65535)
            PortBox.Text = sshDefaults.DefaultPort.ToString();
        if (!string.IsNullOrWhiteSpace(sshDefaults.DefaultUsername))
            UserBox.Text = sshDefaults.DefaultUsername;
    }

    private void TargetBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var text = TargetBox.Text.Trim();
        var m = Regex.Match(text, @"^(?:(?<user>[^@\s]+)@)?(?<host>[^:\s]+)(?::(?<port>\d+))?$");
        if (!m.Success) return;
        if (m.Groups["user"].Success) UserBox.Text = m.Groups["user"].Value;
        if (m.Groups["host"].Success && !text.Contains('@') == false || m.Groups["host"].Success)
            TargetBox.Tag = m.Groups["host"].Value;
        if (m.Groups["port"].Success) PortBox.Text = m.Groups["port"].Value;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Connect_OnClick(object sender, RoutedEventArgs e)
    {
        var host = (TargetBox.Tag as string) ?? TargetBox.Text.Trim();
        var at = host.LastIndexOf('@');
        if (at >= 0) host = host[(at + 1)..];
        var colon = host.LastIndexOf(':');
        if (colon >= 0) host = host[..colon];

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(UserBox.Text))
        {
            ErrorText.Text = "Host and username are required.";
            return;
        }

        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            ErrorText.Text = "Port must be 1–65535.";
            return;
        }

        Result = new QuickConnectResult
        {
            Host = host.Trim(),
            Username = UserBox.Text.Trim(),
            Port = port,
            Password = string.IsNullOrEmpty(PasswordBox.Password) ? null : PasswordBox.Password,
            SaveSession = SaveBox.IsChecked == true,
            Remember = RememberBox.IsChecked == true
        };
        DialogResult = true;
        Close();
    }
}

public sealed class QuickConnectResult
{
    public string Host { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public int Port { get; init; } = 22;
    public string? Password { get; init; }
    public bool SaveSession { get; init; }
    public bool Remember { get; init; }
}
