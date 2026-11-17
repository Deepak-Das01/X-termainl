using XTerminal.Core.Services;

namespace XTerminal.Tests;

/// <summary>
/// Cross-cutting Phase 3 tests for milestones after 3A that are not yet checkpointed alone.
/// Phase 3B owns <see cref="Phase3BFunctionalTests"/>; Phase 3A owns <see cref="Phase3AFunctionalTests"/>.
/// </summary>
public class Phase3FunctionalTests
{
    [Fact]
    public void ShellIntegrationParser_reads_osc133_exit_and_ignores_when_disabled()
    {
        var parser = new ShellIntegrationParser { Enabled = true };
        var events = parser.Push("\u001b]133;D;0\u0007");
        Assert.Contains(events, e => e.Kind == ShellIntegrationEventKind.CommandFinished && e.ExitCode == 0);
        parser.Enabled = false;
        Assert.Empty(parser.Push("\u001b]133;D;1\u0007"));
    }

    [Fact]
    public void WorkspaceLayout_roundtrips_without_buffers()
    {
        var doc = new XTerminal.Core.Models.Ui.WorkspaceLayoutDocument
        {
            RestoreEnabled = true,
            AutoReconnectRemote = false,
            Tabs =
            {
                new XTerminal.Core.Models.Ui.WorkspaceTabLayout
                {
                    Kind = "terminal",
                    Title = "pwsh",
                    LocalShell = "PowerShell",
                    WorkingDirectory = @"C:\Projects"
                }
            }
        };
        var json = System.Text.Json.JsonSerializer.Serialize(doc);
        Assert.DoesNotContain("buffer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        var back = System.Text.Json.JsonSerializer.Deserialize<XTerminal.Core.Models.Ui.WorkspaceLayoutDocument>(json);
        Assert.NotNull(back);
        Assert.False(back!.AutoReconnectRemote);
        Assert.Single(back.Tabs);
    }

    [Fact]
    public void DiagnosticReport_clipboard_sanitizes_failure_detail()
    {
        var report = new SshConnectionDiagnosticReport
        {
            SessionName = "demo",
            Host = "example.com",
            Username = "ops",
            AuthMethod = "PrivateKey",
            FailureKind = SshFailureKind.Authentication,
            FailureMessage = SshConnectionDiagnosticReport.Sanitize("bad password value")
        };
        report.Add("Auth", "permission denied (password)");
        Assert.DoesNotContain("password", report.FailureMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.All(report.Steps, s => Assert.DoesNotContain("password", s.Message, StringComparison.OrdinalIgnoreCase));
        var text = report.ToClipboardText();
        Assert.Contains("********", text);
        Assert.DoesNotContain("bad password", text, StringComparison.OrdinalIgnoreCase);
    }
}
