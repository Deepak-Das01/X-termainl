using XTerminal.Core.Services;

namespace XTerminal.Tests;

/// <summary>
/// Deterministic coverage for session-local OSC sequences (BEL/ST, fragmented, interleaved).
/// Does not claim full Bash/Zsh hook compatibility.
/// </summary>
public class ShellIntegrationSessionLocalOscTests
{
    [Fact]
    public void Parses_osc7_directory_with_bel_termination()
    {
        var parser = new ShellIntegrationParser { Enabled = true };
        var cwd = "/tmp/xterminal-it-abc";
        var chunk = $"hello\u001b]7;file://localhost{cwd}\u0007world";
        var events = parser.Push(chunk);
        Assert.Contains(events, e =>
            e.Kind == ShellIntegrationEventKind.Directory &&
            e.WorkingDirectory == cwd);
    }

    [Fact]
    public void Parses_osc133_exit_codes_with_st_termination()
    {
        var parser = new ShellIntegrationParser { Enabled = true };
        var chunk = "\u001b]133;D;0\u001b\\\u001b]133;D;1\u001b\\";
        var events = parser.Push(chunk);
        Assert.Contains(events, e => e.Kind == ShellIntegrationEventKind.CommandFinished && e.ExitCode == 0);
        Assert.Contains(events, e => e.Kind == ShellIntegrationEventKind.CommandFinished && e.ExitCode == 1);
    }

    [Fact]
    public void Parses_fragmented_osc_across_chunks()
    {
        var parser = new ShellIntegrationParser { Enabled = true };
        Assert.Empty(parser.Push("\u001b]7;file://localhost/tmp/x"));
        var events = parser.Push("terminal-frag\u0007");
        Assert.Contains(events, e =>
            e.Kind == ShellIntegrationEventKind.Directory &&
            e.WorkingDirectory == "/tmp/xterminal-frag");
    }

    [Fact]
    public void Parses_multiple_sequences_interleaved_with_shell_output()
    {
        var parser = new ShellIntegrationParser { Enabled = true };
        var marker = "XTOK-deadbeef";
        var chunk =
            $"prompt$ printf\n{marker}\n" +
            "\u001b]7;file://localhost/tmp/cwd\u0007" +
            "\u001b]133;D;0\u0007" +
            "more text\n";
        var events = parser.Push(chunk);
        Assert.Contains(events, e => e.Kind == ShellIntegrationEventKind.Directory);
        Assert.Contains(events, e => e.Kind == ShellIntegrationEventKind.CommandFinished && e.ExitCode == 0);
    }

    [Fact]
    public void Disabled_parser_emits_no_events()
    {
        var parser = new ShellIntegrationParser { Enabled = false };
        Assert.Empty(parser.Push("\u001b]7;file://localhost/tmp\u0007"));
    }
}
