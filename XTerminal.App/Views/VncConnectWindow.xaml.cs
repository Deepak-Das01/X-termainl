using System.Windows;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Desktop;
using XTerminal.Core.Services;

namespace XTerminal.App.Views;

public partial class VncConnectWindow : Window
{
    public SessionDefinition? Result { get; private set; }
    public string? Password { get; private set; }
    public bool SaveProfile => SaveBox.IsChecked == true;
    public bool RememberPassword => RememberBox.IsChecked == true;

    public VncConnectWindow()
    {
        InitializeComponent();
        AuthBox.Items.Add(nameof(VncAuthenticationMode.VncAuth));
        AuthBox.Items.Add(nameof(VncAuthenticationMode.None));
        AuthBox.SelectedItem = nameof(VncAuthenticationMode.VncAuth);
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Connect_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port))
        {
            ErrorText.Text = "Invalid port.";
            return;
        }

        var auth = AuthBox.SelectedItem?.ToString() ?? nameof(VncAuthenticationMode.VncAuth);
        var def = new SessionDefinition
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? $"VNC · {HostBox.Text.Trim()}" : NameBox.Text.Trim(),
            Kind = SessionKind.Vnc,
            Group = "VNC",
            Host = HostBox.Text?.Trim(),
            Port = port,
            VncAuthMode = auth,
            VncReadOnly = ReadOnlyBox.IsChecked == true,
            AllowUnencryptedVnc = AllowPlainBox.IsChecked == true,
            ShareClipboard = ClipboardBox.IsChecked == true,
            AutoReconnect = AutoReconnectBox.IsChecked == true,
            DesktopScaling = nameof(DesktopScalingMode.FitWindow),
            PersistCredential = RememberBox.IsChecked == true,
            ConnectionTimeoutSeconds = 20
        };

        if (!VncProfileValidator.TryValidate(def.ToVncProfile(), out var error))
        {
            ErrorText.Text = error;
            return;
        }

        Password = string.IsNullOrEmpty(PasswordBox.Password) ? null : PasswordBox.Password;
        Result = def;
        DialogResult = true;
        Close();
    }
}
