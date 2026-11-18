using System.Net;
using System.Net.Sockets;
using System.Text;
using XTerminal.Core.Models;
using XTerminal.Core.Models.PortForwards;
using XTerminal.Core.Services;
using XTerminal.Protocols.Ssh;

namespace XTerminal.Tests;

/// <summary>
/// Opt-in shell and local port-forward checks against production services.
/// Session-local OSC only — not full Bash/Zsh hook validation.
/// </summary>
[Collection("LiveIntegration")]
public class LiveShellTunnelDiagnosticsTests
{
    [Fact]
    [Trait("Category", "LiveIntegration")]
    [Trait("LiveFilter", "ShellOsc")]
    public async Task Live_shell_success_and_failure_commands_with_session_local_osc()
    {
        var config = LiveIntegrationRequire.ConfigOrFail();
        await using var harness = LiveIntegrationHarness.Create(config);
        var host = harness.CreateSessionHost();

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

        void OnOutput(byte[] bytes)
        {
            var text = Encoding.UTF8.GetString(bytes);
            buffer.Append(text);
            foreach (var ev in parser.Push(text))
            {
                lock (oscEvents)
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
            if (ShellOutputMarker.ContainsUnquotedLine(all, markerOk))
                sawOk.TrySetResult(true);
            if (ShellOutputMarker.ContainsUnquotedLine(all, markerFail))
                sawFail.TrySetResult(true);
        }

        host.OutputReceived += OnOutput;
        using var overall = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        try
        {
            await LiveIntegrationWait.WithTimeoutAsync(
                async ct =>
                {
                    await host.ConnectAsync(new SessionDefinition
                    {
                        Name = "live-shell-osc",
                        Kind = SessionKind.Ssh,
                        SshSessionId = harness.Session.Id,
                        Host = harness.Session.Host,
                        Port = harness.Session.Port,
                        Username = harness.Session.Username
                    }, ct);
                },
                TimeSpan.FromSeconds(35),
                "SSH shell connect timed out.");

            Assert.True(host.IsConnected);
            Assert.Equal(ConnectionState.Connected, host.State);

            // One command per write — compound lines with \x escapes proved brittle on interactive shells.
            // Same pattern as Live_ssh_interactive_shell_echoes_marker (which already passes live).
            host.Write(Encoding.UTF8.GetBytes($"printf '%s\\n' '{markerOk}'\n"));
            await LiveIntegrationWait.ExpectAsync(
                sawOk, TimeSpan.FromSeconds(20),
                TimeoutDetail("successful-command marker", buffer),
                overall.Token);

            // Session-local OSC via portable octal printf escapes (no remote rc changes).
            host.Write(Encoding.UTF8.GetBytes(
                $"printf '\\033]7;file://localhost{cwdToken}\\007'\n"));
            var dirEv = await LiveIntegrationWait.ExpectAsync(
                sawDir, TimeSpan.FromSeconds(20),
                TimeoutDetail("session-local OSC 7 directory event", buffer),
                overall.Token);
            Assert.Contains(cwdToken, dirEv.WorkingDirectory ?? "", StringComparison.Ordinal);

            host.Write(Encoding.UTF8.GetBytes("printf '\\033]133;D;0\\007'\n"));
            var exit0 = await LiveIntegrationWait.ExpectAsync(
                sawExit0, TimeSpan.FromSeconds(20),
                TimeoutDetail("session-local OSC 133 exit=0", buffer),
                overall.Token);
            Assert.Equal(0, exit0.ExitCode);

            host.Write(Encoding.UTF8.GetBytes($"printf '%s\\n' '{markerFail}'\n"));
            await LiveIntegrationWait.ExpectAsync(
                sawFail, TimeSpan.FromSeconds(20),
                TimeoutDetail("failed-command marker", buffer),
                overall.Token);

            host.Write(Encoding.UTF8.GetBytes("printf '\\033]133;D;1\\007'\n"));
            var exit1 = await LiveIntegrationWait.ExpectAsync(
                sawExit1, TimeSpan.FromSeconds(20),
                TimeoutDetail("session-local OSC 133 exit=1", buffer),
                overall.Token);
            Assert.Equal(1, exit1.ExitCode);

            lock (oscEvents)
            {
                Assert.Contains(oscEvents, e => e.Kind == ShellIntegrationEventKind.Directory);
                Assert.Contains(oscEvents, e => e.Kind == ShellIntegrationEventKind.CommandFinished && e.ExitCode == 0);
                Assert.Contains(oscEvents, e => e.Kind == ShellIntegrationEventKind.CommandFinished && e.ExitCode == 1);
            }
        }
        finally
        {
            host.OutputReceived -= OnOutput;
            try
            {
                await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                Assert.Fail("SSH shell DisposeAsync timed out (possible disconnect hang).");
            }

            Assert.False(host.IsConnected);
        }
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    [Trait("LiveFilter", "PortForward")]
    public async Task Live_local_port_forward_loopback_end_to_end()
    {
        var config = LiveIntegrationRequire.ConfigOrFail();
        await using var harness = LiveIntegrationHarness.Create(config);
        var forwards = harness.CreatePortForwards();

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

        try
        {
            await LiveIntegrationWait.WithTimeoutAsync(
                async ct => await forwards.StartAsync(def, harness.Session, ct),
                TimeSpan.FromSeconds(40),
                "Port forward StartAsync timed out.");

            Assert.Equal(PortForwardRuntimeState.Running, forwards.GetState(def.Id));
            Assert.Null(forwards.GetError(def.Id));

            using (var client = new TcpClient())
            {
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                await client.ConnectAsync(IPAddress.Loopback, listenPort, connectCts.Token);
                Assert.True(client.Connected);
            }

            await forwards.StopAsync(def.Id).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(PortForwardRuntimeState.Stopped, forwards.GetState(def.Id));

            using var failCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, listenPort, failCts.Token);
            });
        }
        finally
        {
            try
            {
                await forwards.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                Assert.Fail("PortForwardRuntimeService DisposeAsync timed out.");
            }
        }
    }

    /// <summary>Sanitized timeout detail — never includes remote host, user, or buffer contents.</summary>
    private static string TimeoutDetail(string what, StringBuilder buffer)
    {
        var text = buffer.ToString();
        var esc = text.Contains('\u001b');
        var bel = text.Contains('\u0007');
        return $"Timed out waiting for {what} (recvChars={text.Length}, escSeen={esc}, belSeen={bel}).";
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
