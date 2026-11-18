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
        // Unique markers unlikely to appear only as echoed command text without a trailing newline
        // from printf — we require a line that is exactly the marker (or marker + CR).
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

            if (ContainsStandaloneMarker(buffer, markerOk))
                sawOk.TrySetResult(true);
            if (ContainsStandaloneMarker(buffer, markerFail))
                sawFail.TrySetResult(true);
        }

        host.OutputReceived += OnOutput;
        using var overall = new CancellationTokenSource(TimeSpan.FromSeconds(75));

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

            // Portable printf escapes; session-local OSC only (no remote rc changes).
            host.Write(Encoding.UTF8.GetBytes(
                $"printf '%s\\n' '{markerOk}'; " +
                $"printf '\\x1b]7;file://localhost{cwdToken}\\x07'; " +
                $"printf '\\x1b]133;D;0\\x07'\n"));

            await LiveIntegrationWait.ExpectAsync(
                sawOk, TimeSpan.FromSeconds(20),
                "Timed out waiting for successful-command marker (not echo-only).",
                overall.Token);
            var dirEv = await LiveIntegrationWait.ExpectAsync(
                sawDir, TimeSpan.FromSeconds(20),
                "Timed out waiting for session-local OSC 7 directory event.",
                overall.Token);
            Assert.Contains(cwdToken, dirEv.WorkingDirectory ?? "", StringComparison.Ordinal);
            var exit0 = await LiveIntegrationWait.ExpectAsync(
                sawExit0, TimeSpan.FromSeconds(20),
                "Timed out waiting for session-local OSC 133 exit=0.",
                overall.Token);
            Assert.Equal(0, exit0.ExitCode);

            host.Write(Encoding.UTF8.GetBytes(
                $"printf '%s\\n' '{markerFail}'; " +
                $"printf '\\x1b]133;D;1\\x07'\n"));

            await LiveIntegrationWait.ExpectAsync(
                sawFail, TimeSpan.FromSeconds(20),
                "Timed out waiting for failed-command marker.",
                overall.Token);
            var exit1 = await LiveIntegrationWait.ExpectAsync(
                sawExit1, TimeSpan.FromSeconds(20),
                "Timed out waiting for session-local OSC 133 exit=1.",
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

    /// <summary>
    /// Requires the marker as its own line so a quoted echo of the printf command alone is insufficient.
    /// </summary>
    private static bool ContainsStandaloneMarker(StringBuilder buffer, string marker)
    {
        var text = buffer.ToString();
        // printf output ends with newline: "...\nMARKER\n" or "\r\nMARKER\r\n"
        return text.Contains("\n" + marker + "\n", StringComparison.Ordinal) ||
               text.Contains("\n" + marker + "\r", StringComparison.Ordinal) ||
               text.Contains("\r" + marker + "\n", StringComparison.Ordinal) ||
               text.StartsWith(marker + "\n", StringComparison.Ordinal) ||
               text.StartsWith(marker + "\r", StringComparison.Ordinal);
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
