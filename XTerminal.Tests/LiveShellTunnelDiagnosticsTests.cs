using System.Net;
using System.Net.Sockets;
using System.Text;
using XTerminal.Core.Models;
using XTerminal.Core.Models.PortForwards;
using XTerminal.Core.Services;
using XTerminal.Protocols.Ssh;

namespace XTerminal.Tests;

/// <summary>
/// Opt-in shell, local port-forward, and diagnostics checks against production services.
/// </summary>
public class LiveShellTunnelDiagnosticsTests
{
    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Live_shell_success_and_failure_commands_with_session_local_osc()
    {
        var config = LiveIntegrationRequire.ConfigOrFail();
        await using var harness = LiveIntegrationHarness.Create(config);
        await using var host = harness.CreateSessionHost();

        var buffer = new StringBuilder();
        var parser = new ShellIntegrationParser { Enabled = true };
        var oscEvents = new List<ShellIntegrationEvent>();
        var markerOk = "XTOK-" + Guid.NewGuid().ToString("N");
        var markerFail = "XTFAIL-" + Guid.NewGuid().ToString("N");
        var cwdToken = "/tmp/xterminal-it-" + Guid.NewGuid().ToString("N");

        var sawOk = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawFail = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawDir = new TaskCompletionSource<ShellIntegrationEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawExit0 = new TaskCompletionSource<ShellIntegrationEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawExit1 = new TaskCompletionSource<ShellIntegrationEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Subscribe before connect so no output is missed.
        host.OutputReceived += bytes =>
        {
            var text = Encoding.UTF8.GetString(bytes);
            buffer.Append(text);
            foreach (var ev in parser.Push(text))
            {
                oscEvents.Add(ev);
                if (ev.Kind == ShellIntegrationEventKind.Directory &&
                    ev.WorkingDirectory is not null &&
                    ev.WorkingDirectory.Contains(cwdToken, StringComparison.Ordinal))
                    sawDir.TrySetResult(ev);
                if (ev.Kind == ShellIntegrationEventKind.CommandFinished && ev.ExitCode == 0)
                    sawExit0.TrySetResult(ev);
                if (ev.Kind == ShellIntegrationEventKind.CommandFinished && ev.ExitCode == 1)
                    sawExit1.TrySetResult(ev);
            }

            var all = buffer.ToString();
            // Prefer marker lines that look like command output (after newline), not only echo.
            if (all.Contains(markerOk + "\n", StringComparison.Ordinal) ||
                all.Contains(markerOk + "\r", StringComparison.Ordinal))
                sawOk.TrySetResult(true);
            if (all.Contains(markerFail + "\n", StringComparison.Ordinal) ||
                all.Contains(markerFail + "\r", StringComparison.Ordinal))
                sawFail.TrySetResult(true);
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await host.ConnectAsync(new SessionDefinition
        {
            Name = "live-shell-osc",
            Kind = SessionKind.Ssh,
            SshSessionId = harness.Session.Id,
            Host = harness.Session.Host,
            Port = harness.Session.Port,
            Username = harness.Session.Username
        }, cts.Token);

        Assert.True(host.IsConnected);
        Assert.Equal(ConnectionState.Connected, host.State);

        // Session-local OSC only (does not modify shell startup files).
        // Use printf hex escapes (\x1b / \x07) so the remote printf emits real ESC/BEL
        // regardless of shell quoting rules for \033.
        // Also emit OSC 133 D;<code> for exit-status events without installing hooks.
        host.Write(Encoding.UTF8.GetBytes(
            $"printf '%s\\n' '{markerOk}'; " +
            $"printf '\\x1b]7;file://localhost{cwdToken}\\x07'; " +
            $"printf '\\x1b]133;D;0\\x07'; " +
            $"true\n"));

        Assert.True(
            await WaitAsync(sawOk.Task, TimeSpan.FromSeconds(25)),
            "Successful command marker was not observed in shell output.");

        Assert.True(
            await WaitAsync(sawDir.Task, TimeSpan.FromSeconds(25)),
            "Session-local OSC 7 directory event was not parsed from remote output.");
        var dirEv = await sawDir.Task;
        Assert.Contains(cwdToken, dirEv.WorkingDirectory ?? "", StringComparison.Ordinal);

        Assert.True(
            await WaitAsync(sawExit0.Task, TimeSpan.FromSeconds(25)),
            "Session-local OSC 133 exit=0 event was not parsed from remote output.");
        var exit0 = await sawExit0.Task;
        Assert.Equal(0, exit0.ExitCode);

        host.Write(Encoding.UTF8.GetBytes(
            $"printf '%s\\n' '{markerFail}'; " +
            $"printf '\\x1b]133;D;1\\x07'; " +
            $"false\n"));

        Assert.True(
            await WaitAsync(sawFail.Task, TimeSpan.FromSeconds(25)),
            "Failed command marker was not observed in shell output.");
        Assert.True(
            await WaitAsync(sawExit1.Task, TimeSpan.FromSeconds(25)),
            "Session-local OSC 133 exit=1 event was not parsed from remote output.");
        var exit1 = await sawExit1.Task;
        Assert.Equal(1, exit1.ExitCode);

        Assert.Contains(oscEvents, e => e.Kind == ShellIntegrationEventKind.Directory);
        Assert.Contains(oscEvents, e => e.Kind == ShellIntegrationEventKind.CommandFinished && e.ExitCode == 0);
        Assert.Contains(oscEvents, e => e.Kind == ShellIntegrationEventKind.CommandFinished && e.ExitCode == 1);

        // Explicit: this validates session-local OSC emission + parser, not full Bash/Zsh hooks.
        await host.DisposeAsync();
        Assert.False(host.IsConnected);
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Live_local_port_forward_loopback_end_to_end()
    {
        var config = LiveIntegrationRequire.ConfigOrFail();
        await using var harness = LiveIntegrationHarness.Create(config);
        await using var forwards = harness.CreatePortForwards();

        var listenPort = GetFreeLoopbackPort();
        var def = new PortForwardDefinition
        {
            Id = Guid.NewGuid(),
            Name = "live-it-forward",
            SshSessionId = harness.Session.Id,
            Type = SshForwardType.Local,
            BindAddress = "127.0.0.1",
            ListenPort = listenPort,
            DestinationHost = "127.0.0.1",
            DestinationPort = 22,
            AllowLan = false
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await forwards.StartAsync(def, harness.Session, cts.Token);

        Assert.Equal(PortForwardRuntimeState.Running, forwards.GetState(def.Id));
        Assert.Null(forwards.GetError(def.Id));

        using (var client = new TcpClient())
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            connectCts.CancelAfter(TimeSpan.FromSeconds(15));
            await client.ConnectAsync(IPAddress.Loopback, listenPort, connectCts.Token);
            Assert.True(client.Connected);
        }

        await forwards.StopAsync(def.Id);
        Assert.Equal(PortForwardRuntimeState.Stopped, forwards.GetState(def.Id));

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = new TcpClient();
            using var failCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(IPAddress.Loopback, listenPort, failCts.Token);
        });
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> WaitAsync(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        return completed == task && task.IsCompletedSuccessfully;
    }
}
