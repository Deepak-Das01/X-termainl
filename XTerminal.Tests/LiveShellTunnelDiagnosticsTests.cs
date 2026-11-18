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
        var sawOk = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawFail = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        host.OutputReceived += bytes =>
        {
            var text = Encoding.UTF8.GetString(bytes);
            buffer.Append(text);
            foreach (var ev in parser.Push(text))
                oscEvents.Add(ev);
            var all = buffer.ToString();
            if (all.Contains(markerOk, StringComparison.Ordinal))
                sawOk.TrySetResult(true);
            if (all.Contains(markerFail, StringComparison.Ordinal))
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

        // Successful command + session-local OSC 7 (does not modify shell startup files).
        var cwdProbe = $"/tmp/xterminal-it-{Guid.NewGuid():N}";
        host.Write(Encoding.UTF8.GetBytes(
            $"printf '%s\\n' '{markerOk}'; " +
            $"printf '\\033]7;file://localhost{cwdProbe}\\007'; " +
            $"true\n"));

        Assert.True(
            await WaitAsync(sawOk.Task, TimeSpan.FromSeconds(25)),
            "Successful command marker was not observed.");

        // Failed command (non-zero exit) — marker still printed.
        host.Write(Encoding.UTF8.GetBytes(
            $"printf '%s\\n' '{markerFail}'; false\n"));
        Assert.True(
            await WaitAsync(sawFail.Task, TimeSpan.FromSeconds(25)),
            "Failed command marker was not observed.");

        // OSC events from session-local printf (not from persistent shell integration hooks).
        Assert.Contains(oscEvents, e => e.Kind == ShellIntegrationEventKind.Directory);

        // Limitation note (asserted as documentation via comment): persistent shell-integration
        // OSC 133 exit-status hooks require remote shell startup changes, which live tests must not do.
        // Directory OSC above is session-local only.

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

        // End-to-end: TCP connect through the local listener reaches remote loopback:22.
        using (var client = new TcpClient())
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            connectCts.CancelAfter(TimeSpan.FromSeconds(15));
            await client.ConnectAsync(IPAddress.Loopback, listenPort, connectCts.Token);
            Assert.True(client.Connected);
        }

        await forwards.StopAsync(def.Id);
        Assert.Equal(PortForwardRuntimeState.Stopped, forwards.GetState(def.Id));

        // Listener should be gone.
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
