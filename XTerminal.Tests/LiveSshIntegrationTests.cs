using System.Diagnostics;
using System.Text;
using XTerminal.Core.Models;
using XTerminal.Core.Services;
using XTerminal.Protocols.Ssh;

namespace XTerminal.Tests;

/// <summary>
/// Opt-in live SSH tests against an authorized private server (Category=LiveIntegration).
/// Excluded from default CI. Use tools/run-live-integration.ps1.
/// TCP connectivity alone does not prove SSH authentication.
/// </summary>
[Collection("LiveIntegration")]
public class LiveSshIntegrationTests
{
    [Fact]
    public void LiveIntegrationGate_disabled_by_default_in_ci()
    {
        if (LiveIntegrationGate.IsProcessEnabled)
            return;

        Assert.False(LiveIntegrationGate.IsFullyConfigured);
        Assert.Contains("disabled", LiveIntegrationGate.DescribeConfiguration(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Live_endpoint_probe_requires_successful_tcp()
    {
        var config = LiveIntegrationRequire.ConfigOrFail();
        var report = new SshConnectionDiagnosticReport
        {
            SessionName = "live-it-probe",
            Host = config.Host,
            Port = config.Port,
            Username = config.Username,
            AuthMethod = config.AuthMethod.ToString()
        };

        var sw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await SshConnectionProbe.ProbeEndpointAsync(
            report, config.Host, config.Port, timeoutSeconds: 15, cts.Token);
        sw.Stop();
        report.Duration = sw.Elapsed;

        Assert.Equal(SshFailureKind.None, report.FailureKind);
        Assert.Contains(report.Steps, s => s.Phase == "DNS" && s.Success);
        Assert.Contains(report.Steps, s => s.Phase == "TCP" && s.Success);
        Assert.False(string.IsNullOrWhiteSpace(report.ResolvedAddress));
        Assert.True(report.Duration.HasValue && report.Duration.Value > TimeSpan.Zero,
            $"Probe duration missing or zero: {report.Duration}");

        // Explicit: this test does not authenticate.
        Assert.DoesNotContain(report.Steps, s =>
            s.Phase.Contains("Auth", StringComparison.OrdinalIgnoreCase) && s.Success);
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Live_ssh_authentication_succeeds_with_host_key_match()
    {
        var config = LiveIntegrationRequire.ConfigOrFail();
        await using var harness = LiveIntegrationHarness.Create(config);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var owned = await harness.Transport.OpenSshClientAsync(harness.Session, cts.Token);
        try
        {
            Assert.True(owned.Client.IsConnected);
            owned.Client.Disconnect();
            Assert.False(owned.Client.IsConnected);
        }
        finally
        {
            try { if (owned.Client.IsConnected) owned.Client.Disconnect(); } catch { /* ignore */ }
            try { owned.Client.Dispose(); } catch { /* ignore */ }
            try { owned.Ownership?.Dispose(); } catch { /* ignore */ }
        }
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Live_ssh_remote_command_returns_unique_marker()
    {
        var config = LiveIntegrationRequire.ConfigOrFail();
        await using var harness = LiveIntegrationHarness.Create(config);
        var marker = "XTIT-" + Guid.NewGuid().ToString("N");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var owned = await harness.Transport.OpenSshClientAsync(harness.Session, cts.Token);
        try
        {
            Assert.True(owned.Client.IsConnected);

            // Harmless: print marker only. Quote-safe via printf %s.
            var cmd = owned.Client.CreateCommand($"printf '%s\\n' '{marker}'");
            cmd.CommandTimeout = TimeSpan.FromSeconds(20);
            var output = await Task.Run(() => cmd.Execute(), cts.Token);

            Assert.Contains(marker, output, StringComparison.Ordinal);
            Assert.True(owned.Client.IsConnected);
            owned.Client.Disconnect();
        }
        finally
        {
            try { if (owned.Client.IsConnected) owned.Client.Disconnect(); } catch { /* ignore */ }
            try { owned.Client.Dispose(); } catch { /* ignore */ }
            try { owned.Ownership?.Dispose(); } catch { /* ignore */ }
        }
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Live_ssh_interactive_shell_echoes_marker()
    {
        var config = LiveIntegrationRequire.ConfigOrFail();
        await using var harness = LiveIntegrationHarness.Create(config);
        var marker = "XTSH-" + Guid.NewGuid().ToString("N");

        await using var host = harness.CreateSessionHost();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var buffer = new StringBuilder();
        host.OutputReceived += bytes =>
        {
            buffer.Append(Encoding.UTF8.GetString(bytes));
            if (buffer.ToString().Contains(marker, StringComparison.Ordinal))
                tcs.TrySetResult(true);
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await host.ConnectAsync(new SessionDefinition
        {
            Name = "live-shell",
            Kind = SessionKind.Ssh,
            SshSessionId = harness.Session.Id,
            Host = harness.Session.Host,
            Port = harness.Session.Port,
            Username = harness.Session.Username
        }, cts.Token);

        Assert.Equal(ConnectionState.Connected, host.State);
        Assert.True(host.IsConnected);

        var cmd = Encoding.UTF8.GetBytes($"printf '%s\\n' '{marker}'\n");
        host.Write(cmd);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(25), cts.Token));
        Assert.True(completed == tcs.Task && tcs.Task.IsCompletedSuccessfully,
            "Expected marker was not received from the interactive shell within timeout.");

        await host.DisposeAsync();
        Assert.False(host.IsConnected);
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Live_ssh_host_key_mismatch_rejects_authentication()
    {
        var config = LiveIntegrationRequire.ConfigOrFail();
        // Intentionally wrong fingerprint in an isolated known_hosts store.
        var wrong = new string('0', 64);
        Assert.NotEqual(config.ExpectedHostKeyFingerprintSha256Hex, wrong, StringComparer.OrdinalIgnoreCase);

        await using var harness = LiveIntegrationHarness.Create(
            config, fingerprintHexOverride: wrong, trustFingerprint: true);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var owned = await harness.Transport.OpenSshClientAsync(harness.Session, cts.Token);
            try { owned.Client.Dispose(); } catch { /* ignore */ }
            try { owned.Ownership?.Dispose(); } catch { /* ignore */ }
        });

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        // Do not assert secret or infrastructure specifics.
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Live_diagnostics_pipeline_authenticates()
    {
        var config = LiveIntegrationRequire.ConfigOrFail();
        await using var harness = LiveIntegrationHarness.Create(config);
        var tester = harness.CreateTester();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var report = await tester.DiagnoseAsync(harness.Session, cts.Token);

        Assert.Equal(SshFailureKind.None, report.FailureKind);
        Assert.Contains(report.Steps, s => s.Success &&
            (s.Phase.Contains("Auth", StringComparison.OrdinalIgnoreCase) ||
             s.Message.Contains("Authentication", StringComparison.OrdinalIgnoreCase) ||
             s.Phase.Contains("SSH", StringComparison.OrdinalIgnoreCase)));

        var sanitized = report.ToClipboardText(DiagnosticExportMode.SanitizedPublic);
        // Auth method labels are not secrets and may appear (e.g. "Auth: Password").
        Assert.Contains("Auth:", sanitized, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(report.AuthMethod))
            Assert.Contains(report.AuthMethod, sanitized, StringComparison.Ordinal);

        // Private endpoints must not appear in the sanitized public export.
        Assert.DoesNotContain(config.Host, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(config.Username, sanitized, StringComparison.Ordinal);
        Assert.Contains("[redacted-host]", sanitized, StringComparison.Ordinal);

        // Actual credential values must never appear — assert without dumping the export.
        if (config.Password is not null)
        {
            Assert.False(
                report.ExportContainsSecret(config.Password, DiagnosticExportMode.SanitizedPublic),
                "Sanitized diagnostic export contained the credential value.");
            Assert.False(
                report.ExportContainsSecret(config.Password, DiagnosticExportMode.DetailedLocal),
                "Detailed diagnostic export contained the credential value.");
        }
    }
}
