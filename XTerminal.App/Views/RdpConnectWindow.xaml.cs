using System.Windows;
using XTerminal.Core.Models;
using XTerminal.Core.Services;

namespace XTerminal.App.Views;

public partial class RdpConnectWindow : Window
{
    public SessionDefinition? Result { get; private set; }
    public string? Password { get; private set; }
    public bool SaveProfile => SaveBox.IsChecked == true;
    public bool RememberPassword => RememberBox.IsChecked == true;

    public RdpConnectWindow()
    {
        InitializeComponent();
        foreach (var d in new[] { "32", "24", "16", "15", "8" })
            DepthBox.Items.Add(d);
        DepthBox.SelectedItem = "32";
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Connect_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port)) { ErrorText.Text = "Invalid port."; return; }
        if (!int.TryParse(WidthBox.Text, out var w) || !int.TryParse(HeightBox.Text, out var h))
        {
            ErrorText.Text = "Invalid resolution.";
            return;
        }
        if (!int.TryParse(DepthBox.SelectedItem?.ToString() ?? DepthBox.Text, out var depth))
        {
            ErrorText.Text = "Invalid color depth.";
            return;
        }

        var def = new SessionDefinition
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? $"RDP · {HostBox.Text.Trim()}" : NameBox.Text.Trim(),
            Kind = SessionKind.Rdp,
            Group = "RDP",
            Host = HostBox.Text?.Trim(),
            Port = port,
            Username = UserBox.Text?.Trim(),
            Domain = string.IsNullOrWhiteSpace(DomainBox.Text) ? null : DomainBox.Text.Trim(),
            DesktopWidth = w,
            DesktopHeight = h,
            ColorDepth = depth,
            RequireNla = NlaBox.IsChecked == true,
            AuthenticationLevel = 2,
            SmartSizing = SmartSizeBox.IsChecked == true,
            RedirectClipboard = ClipBox.IsChecked == true,
            RedirectDrives = DriveBox.IsChecked == true,
            RedirectPrinters = PrinterBox.IsChecked == true,
            RedirectAudio = AudioBox.IsChecked == true,
            AutoReconnect = false,
            PersistCredential = RememberBox.IsChecked == true
        };

        if (!RdpProfileValidator.TryValidate(def.ToRdpProfile(), out var error))
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
