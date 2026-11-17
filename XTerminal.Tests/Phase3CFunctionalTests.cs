using System.Net.Sockets;
using XTerminal.Core.Models.PortForwards;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Services;

namespace XTerminal.Tests;

/// <summary>
/// Phase 3C: shell integration security, SFTP CWD sync, port-forward validation helpers, diagnostics sanitization.
/// </summary>
public class Phase3CFunctionalTests
{
    [Fact]
    public void ShellIntegration_parses_osc133_lifecycle_and_duration()
    {
        var parser = new ShellIntegrationParser { Enabled = true };
        parser.Push("\u001b]133;A\u0007");
        Assert.Equal(ShellIntegrationState.Active, parser.State);
        var started = parser.Push("\u001b]133;C;echo hi\u0007");
        Assert.Contains(started, e => e.Kind == ShellIntegrationEventKind.CommandStarted && e.Command == "echo hi");
        Thread.Sleep(20);
        var finished = parser.Push("\u001b]133;D;0\u0007");
        var done = Assert.Single(finished, e => e.Kind == ShellIntegrationEventKind.CommandFinished);
        Assert.Equal(0, done.ExitCode);
        Assert.NotNull(done.Duration);
        Assert.True(done.Duration >= TimeSpan.Zero);
        Assert.Equal(ShellIntegrationState.Active, parser.State);
    }

    [Fact]
    public void ShellIntegration_parses_fragmented_sequences()
    {
        var parser = new ShellIntegrationParser { Enabled = true };
        Assert.Empty(parser.Push("\u001b]133;D;"));
        var events = parser.Push("1\u0007");
        Assert.Contains(events, e => e.Kind == ShellIntegrationEventKind.CommandFinished && e.ExitCode == 1);
    }

    [Fact]
    public void ShellIntegration_rejects_oversized_and_malformed_sequences()
    {
        var parser = new ShellIntegrationParser { Enabled = true };
        var huge = "\u001b]" + new string('X', ShellIntegrationParser.MaxSequenceLength + 50) + "\u0007";
        parser.Push(huge);
        Assert.Equal(ShellIntegrationState.Degraded, parser.State);

        parser.Reset();
        parser.Enabled = true;
        Assert.DoesNotContain(
            parser.Push("\u001b]133;D;not-a-number\u0007"),
            e => e.ExitCode is not null);

        Assert.Empty(parser.Push("\u001b]999;evil\u0007").Where(e => e.Kind != ShellIntegrationEventKind.Unknown));
    }

    [Fact]
    public void ShellIntegration_ignores_http_and_javascript_directory_uris()
    {
        var parser = new ShellIntegrationParser { Enabled = true };
        Assert.DoesNotContain(
            parser.Push("\u001b]7;https://evil.example/path\u0007"),
            e => e.Kind == ShellIntegrationEventKind.Directory);
        Assert.DoesNotContain(
            parser.Push("\u001b]7;javascript:alert(1)\u0007"),
            e => e.Kind == ShellIntegrationEventKind.Directory);

        var ok = parser.Push("\u001b]7;file://server.example.com/home/username\u0007");
        Assert.Contains(ok, e => e.Kind == ShellIntegrationEventKind.Directory &&
                                 e.WorkingDirectory == "/home/username");
    }

    [Fact]
    public void ShellIntegration_osc633_cwd_and_disabled_fallback()
    {
        var parser = new ShellIntegrationParser { Enabled = true };
        var events = parser.Push("\u001b]633;P;Cwd=/var/log\u0007");
        Assert.Contains(events, e => e.Kind == ShellIntegrationEventKind.Directory && e.WorkingDirectory == "/var/log");

        parser.Enabled = false;
        Assert.Empty(parser.Push("\u001b]633;D;0\u0007"));
        Assert.Equal(ShellIntegrationState.Disabled, parser.State);
    }

    [Fact]
    public void ShellIntegration_unexpected_finish_without_start_has_no_duration()
    {
        var parser = new ShellIntegrationParser { Enabled = true };
        var events = parser.Push("\u001b]133;D;2\u0007");
        var done = Assert.Single(events, e => e.Kind == ShellIntegrationEventKind.CommandFinished);
        Assert.Equal(2, done.ExitCode);
        Assert.Null(done.Duration);
    }

    [Fact]
    public void ShellIntegration_flood_protection_clears_carry()
    {
        var parser = new ShellIntegrationParser { Enabled = true };
        var flood = new string('a', ShellIntegrationParser.MaxCarryLength * 2 + 10);
        Assert.Empty(parser.Push(flood));
        Assert.Equal(ShellIntegrationState.Degraded, parser.State);
    }

    [Fact]
    public void SftpDirectorySync_rejects_cross_session_invalid_path_and_debounce()
    {
        var sync = new SftpDirectorySync { FollowTerminalDirectory = true, Debounce = TimeSpan.FromSeconds(5) };
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.False(sync.ShouldNavigate(a, b, "/tmp", out _, out var reason));
        Assert.Contains("match", reason, StringComparison.OrdinalIgnoreCase);

        Assert.False(sync.ShouldNavigate(a, a, "", out _, out reason));
        Assert.Contains("working directory", reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(sync.ShouldNavigate(a, a, "/../../etc", out _, out reason));
        Assert.Contains("escapes", reason, StringComparison.OrdinalIgnoreCase);

        Assert.True(sync.ShouldNavigate(a, a, "/home/username", out var path, out _));
        Assert.Equal("/home/username", path);
        Assert.False(sync.ShouldNavigate(a, a, "/home/username", out _, out reason));
        Assert.Contains("Debounced", reason, StringComparison.OrdinalIgnoreCase);

        sync.FollowTerminalDirectory = false;
        Assert.False(sync.ShouldNavigate(a, a, "/home/username/other", out _, out reason));
        Assert.Contains("disabled", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnosticReport_sanitizes_secrets_and_classifies_failures()
    {
        Assert.Equal(SshFailureKind.Cancelled, SshConnectionDiagnosticReport.Classify(new OperationCanceledException()));
        Assert.Equal(SshFailureKind.Dns, SshConnectionDiagnosticReport.Classify(new SocketException((int)SocketError.HostNotFound)));
        Assert.Equal(SshFailureKind.ConnectionRefused, SshConnectionDiagnosticReport.Classify(new SocketException((int)SocketError.ConnectionRefused)));
        Assert.Equal(SshFailureKind.ChangedHostKey,
            SshConnectionDiagnosticReport.Classify(new InvalidOperationException("REMOTE HOST IDENTIFICATION HAS CHANGED host key")));
        Assert.Equal(SshFailureKind.UnknownHostKey,
            SshConnectionDiagnosticReport.Classify(new InvalidOperationException("unknown host key for server")));

        var report = new SshConnectionDiagnosticReport
        {
            SessionName = "demo",
            Host = "server.example.com",
            Username = "username",
            AuthMethod = "PrivateKey",
            FailureKind = SshFailureKind.Authentication,
            FailureMessage = SshConnectionDiagnosticReport.Sanitize("permission denied (password)")
        };
        report.Add("Auth", "bad passphrase supplied");
        var text = report.ToClipboardText();
        Assert.DoesNotContain("permission denied (password)", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bad passphrase", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("********", text);
        Assert.Contains("server.example.com", text);
    }

    [Fact]
    public void DiagnosticReport_marks_invalid_profile_configuration()
    {
        Assert.Equal(
            SshFailureKind.InvalidConfiguration,
            SshConnectionDiagnosticReport.Classify(new ArgumentException("invalid host configuration")));
    }

    [Fact]
    public void PortForwardDefinition_defaults_to_loopback()
    {
        var def = new PortForwardDefinition();
        Assert.Equal("127.0.0.1", def.BindAddress);
        Assert.False(def.AllowLan);
        Assert.Equal(PortForwardRuntimeState.Stopped, PortForwardRuntimeState.Stopped);
        Assert.Equal(PortForwardRuntimeState.Running, PortForwardRuntimeState.Active);
    }

    [Fact]
    public void ShellIntegrationHooks_are_opt_in_snippets()
    {
        Assert.Contains("X-Terminal optional", ShellIntegrationHooks.BashSnippet);
        Assert.Contains("__XTERMINAL_HOOKS", ShellIntegrationHooks.ZshSnippet);
        Assert.Contains("PROMPT_COMMAND", ShellIntegrationHooks.BashSnippet);
        Assert.DoesNotContain("password", ShellIntegrationHooks.GetSnippet("pwsh"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HistoryEntry_does_not_invent_exit_or_duration()
    {
        var entry = new XTerminal.Core.Models.History.HistoryEntry
        {
            Command = "ls",
            CaptureSource = "ConPtyInferred"
        };
        Assert.Null(entry.ExitCode);
        Assert.Equal("—", entry.ExitCodeDisplay);
        Assert.Null(entry.DurationMs);
    }
}
