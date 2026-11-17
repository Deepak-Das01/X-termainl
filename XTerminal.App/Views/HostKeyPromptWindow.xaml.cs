using System.Windows;
using XTerminal.Protocols.Ssh;

namespace XTerminal.App.Views;

public partial class HostKeyPromptWindow : Window
{
    public HostKeyPromptResult Result { get; private set; } = HostKeyPromptResult.Reject;

    public HostKeyPromptWindow(HostKeyPromptRequest request)
    {
        InitializeComponent();
        HostText.Text = $"{request.Host}:{request.Port}";
        AlgoText.Text = $"Key type: {request.KeyAlgorithm}";
        FingerprintBox.Text = FormatFingerprint(request.FingerprintSha256);

        if (request.Status == HostKeyStatus.Changed)
        {
            TitleText.Text = "WARNING: Host key has changed";
            SubtitleText.Text =
                "The server presented a different key than the one stored for this host. " +
                "This can indicate a man-in-the-middle attack or a legitimate server rebuild. " +
                "Only replace the stored key if you trust this change.";
            PreviousBox.Visibility = Visibility.Visible;
            PreviousBox.Text = "Previously trusted: " + FormatFingerprint(request.PreviousFingerprintSha256 ?? "");
            OnceBtn.Visibility = Visibility.Collapsed;
            SaveBtn.Content = "Replace stored key";
        }
        else
        {
            TitleText.Text = "Unknown SSH host key";
            SubtitleText.Text =
                "This host is not in your known_hosts file. Review the fingerprint carefully before trusting it.";
        }
    }

    private static string FormatFingerprint(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return "—";
        // Group for readability: AABBCC...
        var chars = hex.ToUpperInvariant().ToCharArray();
        return string.Join(":", Enumerable.Range(0, chars.Length / 2)
            .Select(i => $"{chars[i * 2]}{chars[i * 2 + 1]}"));
    }

    private void Reject_OnClick(object sender, RoutedEventArgs e)
    {
        Result = HostKeyPromptResult.Reject;
        DialogResult = false;
        Close();
    }

    private void Once_OnClick(object sender, RoutedEventArgs e)
    {
        Result = HostKeyPromptResult.TrustOnce;
        DialogResult = true;
        Close();
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        Result = SaveBtn.Content?.ToString()?.Contains("Replace", StringComparison.OrdinalIgnoreCase) == true
            ? HostKeyPromptResult.ReplaceAndSave
            : HostKeyPromptResult.TrustAndSave;
        DialogResult = true;
        Close();
    }
}
