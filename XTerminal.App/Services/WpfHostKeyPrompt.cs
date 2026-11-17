using System.Windows;
using XTerminal.App.Views;
using XTerminal.Protocols.Ssh;

namespace XTerminal.App.Services;

/// <summary>Thread-safe WPF host-key prompt for SSH.NET callbacks.</summary>
public sealed class WpfHostKeyPrompt : ISshHostKeyPrompt
{
    public HostKeyPromptResult Prompt(HostKeyPromptRequest request)
    {
        var app = Application.Current;
        if (app is null)
            return HostKeyPromptResult.Reject;

        if (!app.Dispatcher.CheckAccess())
            return app.Dispatcher.Invoke(() => Prompt(request));

        var owner = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? app.MainWindow;
        var dialog = new HostKeyPromptWindow(request) { Owner = owner };
        var ok = dialog.ShowDialog();
        return ok == true ? dialog.Result : HostKeyPromptResult.Reject;
    }
}
