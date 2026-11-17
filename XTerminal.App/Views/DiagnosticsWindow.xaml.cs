using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Security;
using XTerminal.Core.Services;
using XTerminal.Protocols.Ssh;

namespace XTerminal.App.Views;

public partial class DiagnosticsWindow : Window
{
    private readonly DiagnosticsViewModel _vm;

    public DiagnosticsWindow(SshSessionRecord session, ISecretStore secrets, SshHostKeyPolicy hostKeys)
    {
        InitializeComponent();
        _vm = new DiagnosticsViewModel(session, secrets, hostKeys);
        DataContext = _vm;
        Loaded += async (_, _) => await _vm.RunAsync();
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.ClipboardText)) return;
        var preview = MessageBox.Show(
            "Copy the sanitized diagnostic report to the clipboard?\n\n" +
            "The report includes connection topology (hostnames, ports, jump hops) but not passwords or keys.",
            "Copy diagnostics",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (preview != MessageBoxResult.Yes) return;
        try { Clipboard.SetText(_vm.ClipboardText); }
        catch { /* ignore */ }
    }

    private async void Retry_OnClick(object sender, RoutedEventArgs e) => await _vm.RunAsync();

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => _vm.Cancel();

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _vm.Cancel();
        base.OnClosed(e);
    }
}

public sealed class DiagnosticsViewModel : INotifyPropertyChanged
{
    private readonly SshSessionRecord _session;
    private readonly ISecretStore _secrets;
    private readonly SshHostKeyPolicy _hostKeys;
    private CancellationTokenSource? _cts;

    public DiagnosticsViewModel(SshSessionRecord session, ISecretStore secrets, SshHostKeyPolicy hostKeys)
    {
        _session = session;
        _secrets = secrets;
        _hostKeys = hostKeys;
        Title = $"Diagnostics — {session.Name}";
        Subtitle = $"{session.Username}@{session.Host}:{session.Port} · Auth: {session.AuthMethod}";
    }

    public string Title { get; }
    public string Subtitle { get; }
    public ObservableCollection<DiagnosticStepRow> Steps { get; } = new();

    private string _resultSummary = "Starting…";
    public string ResultSummary
    {
        get => _resultSummary;
        set { _resultSummary = value; OnPropertyChanged(); }
    }

    private string _durationText = "";
    public string DurationText
    {
        get => _durationText;
        set { _durationText = value; OnPropertyChanged(); }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRetry)); }
    }

    public bool CanRetry => !IsRunning;
    public string ClipboardText { get; private set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Cancel() => _cts?.Cancel();

    public async Task RunAsync()
    {
        Cancel();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        Steps.Clear();
        ResultSummary = "Running diagnostics…";
        DurationText = "";
        ClipboardText = "";

        try
        {
            var tester = new SshConnectionTester(_secrets, _hostKeys);
            var report = await tester.DiagnoseAsync(_session, _cts.Token);
            foreach (var s in report.Steps)
            {
                Steps.Add(new DiagnosticStepRow
                {
                    Status = s.Success ? "OK" : "FAIL",
                    Phase = s.Phase,
                    Message = s.Message
                });
            }

            ResultSummary = report.FailureKind == SshFailureKind.None
                ? "Result: OK"
                : $"Result: {report.FailureKind} — {report.FailureMessage}";
            DurationText = report.Duration is { } d
                ? $"Total duration: {d.TotalSeconds:0.##}s (TCP connectivity ≠ SSH RTT)"
                : "";
            ClipboardText = report.ToClipboardText();
        }
        catch (OperationCanceledException)
        {
            Steps.Add(new DiagnosticStepRow { Status = "CANCEL", Phase = "User", Message = "Diagnostics cancelled." });
            ResultSummary = "Result: Cancelled";
        }
        catch (Exception ex)
        {
            Steps.Add(new DiagnosticStepRow
            {
                Status = "FAIL",
                Phase = "Diagnostics",
                Message = SshConnectionDiagnosticReport.Sanitize(ex.Message)
            });
            ResultSummary = "Result: Failed";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class DiagnosticStepRow
{
    public string Status { get; set; } = "";
    public string Phase { get; set; } = "";
    public string Message { get; set; } = "";
}
