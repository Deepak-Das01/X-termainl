using System.Windows;
using XTerminal.Core.Models;
using XTerminal.Core.Services;

namespace XTerminal.App.Views;

public partial class TelnetConnectWindow : Window
{
    public SessionDefinition? Result { get; private set; }
    public bool SaveProfile => SaveBox.IsChecked == true;

    public TelnetConnectWindow()
    {
        InitializeComponent();
        TermTypeBox.Items.Add("xterm-256color");
        TermTypeBox.Items.Add("xterm");
        TermTypeBox.Items.Add("vt100");
        TermTypeBox.Text = "xterm-256color";
        EncodingBox.Items.Add("utf-8");
        EncodingBox.Items.Add("ascii");
        EncodingBox.Text = "utf-8";
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Connect_OnClick(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(host))
        {
            ErrorText.Text = "Host is required.";
            return;
        }

        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            ErrorText.Text = "Port must be 1–65535.";
            return;
        }

        if (!int.TryParse(TimeoutBox.Text, out var timeout) || timeout is < 3 or > 120)
        {
            ErrorText.Text = "Timeout must be 3–120 seconds.";
            return;
        }

        if (!TelnetProfileValidator.TryValidate(host, port, timeout, out var error))
        {
            ErrorText.Text = error;
            return;
        }

        Result = new SessionDefinition
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? $"Telnet · {host}" : NameBox.Text.Trim(),
            Kind = SessionKind.Telnet,
            Group = "Telnet",
            Host = host,
            Port = port,
            ConnectionTimeoutSeconds = timeout,
            TerminalType = string.IsNullOrWhiteSpace(TermTypeBox.Text) ? "xterm-256color" : TermTypeBox.Text.Trim(),
            TextEncoding = string.IsNullOrWhiteSpace(EncodingBox.Text) ? "utf-8" : EncodingBox.Text.Trim(),
            LocalEcho = LocalEchoBox.IsChecked == true,
            AutoReconnect = AutoReconnectBox.IsChecked == true,
            LineEnding = "CrLf"
        };
        DialogResult = true;
        Close();
    }
}
