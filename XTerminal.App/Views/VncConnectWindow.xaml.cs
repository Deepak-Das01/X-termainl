using System.Windows;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Desktop;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Services;

namespace XTerminal.App.Views;

public partial class VncConnectWindow : Window
{
    public SessionDefinition? Result { get; private set; }
    public string? Password { get; private set; }
    public bool SaveProfile => SaveBox.IsChecked == true;
    public bool RememberPassword => RememberBox.IsChecked == true;

    public VncConnectWindow(IEnumerable<SshSessionRecord>? sshSessions = null)
    {
        InitializeComponent();
        AuthBox.Items.Add(nameof(VncAuthenticationMode.VncAuth));
        AuthBox.Items.Add(nameof(VncAuthenticationMode.None));
        AuthBox.SelectedItem = nameof(VncAuthenticationMode.VncAuth);
        if (sshSessions is not null)
        {
            foreach (var s in sshSessions.OrderBy(s => s.Name))
                SshSessionBox.Items.Add(s);
        }

        UpdateTunnelEnabled();
    }

    private void TunnelBox_OnChanged(object sender, RoutedEventArgs e) => UpdateTunnelEnabled();

    private void UpdateTunnelEnabled()
    {
        var on = TunnelBox.IsChecked == true;
        SshSessionBox.IsEnabled = on;
        TunnelHostBox.IsEnabled = on;
        TunnelPortBox.IsEnabled = on;
        HostBox.IsEnabled = !on;
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

        var useTunnel = TunnelBox.IsChecked == true;
        Guid? sshId = null;
        string tunnelHost = "127.0.0.1";
        var tunnelPort = 5900;
        if (useTunnel)
        {
            if (SshSessionBox.SelectedItem is not SshSessionRecord ssh)
            {
                ErrorText.Text = "Select an SSH profile for the tunnel.";
                return;
            }

            sshId = ssh.Id;
            tunnelHost = string.IsNullOrWhiteSpace(TunnelHostBox.Text) ? "127.0.0.1" : TunnelHostBox.Text.Trim();
            if (!int.TryParse(TunnelPortBox.Text, out tunnelPort))
            {
                ErrorText.Text = "Invalid tunnel remote port.";
                return;
            }
        }

        var auth = AuthBox.SelectedItem?.ToString() ?? nameof(VncAuthenticationMode.VncAuth);
        var host = useTunnel ? "127.0.0.1" : HostBox.Text?.Trim();
        var def = new SessionDefinition
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text)
                ? (useTunnel ? $"VNC · SSH tunnel · {tunnelHost}:{tunnelPort}" : $"VNC · {host}")
                : NameBox.Text.Trim(),
            Kind = SessionKind.Vnc,
            Group = "VNC",
            Host = host,
            Port = useTunnel ? tunnelPort : port,
            VncAuthMode = auth,
            VncReadOnly = ReadOnlyBox.IsChecked == true,
            AllowUnencryptedVnc = AllowPlainBox.IsChecked == true,
            ShareClipboard = ClipboardBox.IsChecked == true,
            AutoReconnect = AutoReconnectBox.IsChecked == true,
            DesktopScaling = nameof(DesktopScalingMode.FitWindow),
            PersistCredential = RememberBox.IsChecked == true,
            ConnectionTimeoutSeconds = 20,
            UseSshTunnel = useTunnel,
            SshSessionId = sshId,
            VncTunnelRemoteHost = tunnelHost,
            VncTunnelRemotePort = tunnelPort
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
