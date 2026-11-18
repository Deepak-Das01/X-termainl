using System.Text;
using XTerminal.Core.Models;
using XTerminal.Protocols.Local;

namespace XTerminal.Tests;

/// <summary>Protocol-level local shell smoke (not GUI). Requires Windows ConPTY.</summary>
public class LocalConPtySmokeTests
{
    [Fact]
    public async Task Cmd_conpty_connects_and_disposes_cleanly()
    {
        await using var host = new LocalShellSessionHost();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await host.ConnectAsync(new SessionDefinition
        {
            Name = "it-cmd",
            Kind = SessionKind.LocalShell,
            Shell = ShellKind.Cmd
        }, cts.Token);

        Assert.Equal(ConnectionState.Connected, host.State);
        Assert.True(host.IsConnected);
        host.Resize(100, 30);
        // Second dispose via await using must be safe.
    }

    /// <summary>
    /// Interactive echo can be environment-sensitive under agent/automation hosts.
    /// Excluded from default CI via Category filter; run manually when validating ConPTY I/O.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalConPtyInteractive")]
    public async Task Cmd_conpty_echoes_marker_when_interactive_io_available()
    {
        await using var host = new LocalShellSessionHost();
        var output = new StringBuilder();
        host.OutputReceived += chunk =>
        {
            lock (output) output.Append(Encoding.UTF8.GetString(chunk));
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await host.ConnectAsync(new SessionDefinition
        {
            Name = "it-cmd-io",
            Kind = SessionKind.LocalShell,
            Shell = ShellKind.Cmd
        }, cts.Token);

        await Task.Delay(800, cts.Token);
        host.Write(Encoding.UTF8.GetBytes("echo XTERM_IT_OK\r\n"));

        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            string snapshot;
            lock (output) snapshot = output.ToString();
            if (snapshot.Contains("XTERM_IT_OK", StringComparison.Ordinal))
                return;
            await Task.Delay(150, cts.Token);
        }

        string final;
        lock (output) final = output.ToString();
        Assert.Fail($"No marker in ConPTY output (length={final.Length}).");
    }
}
