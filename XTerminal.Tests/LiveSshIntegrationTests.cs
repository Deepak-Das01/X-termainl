using XTerminal.Core.Services;

namespace XTerminal.Tests;

/// <summary>
/// Unit-facing gate checks always run. Protocol live tests are Category=LiveIntegration
/// and are excluded from default CI via filter.
/// </summary>
public class LiveSshIntegrationTests
{
    [Fact]
    public void LiveIntegrationGate_disabled_by_default_in_ci()
    {
        // In CI and developer machines without explicit enable, live suite must stay off.
        if (string.Equals(Environment.GetEnvironmentVariable("XTERMINAL_IT_ENABLE"), "1", StringComparison.Ordinal))
            return; // environment explicitly opted in — do not fail the unit suite

        Assert.False(LiveIntegrationGate.IsFullyConfigured);
        Assert.Contains("disabled", LiveIntegrationGate.DescribeConfiguration(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "LiveIntegration")]
    public async Task Live_tcp_and_dns_probe_authorized_host()
    {
        if (!LiveIntegrationGate.IsFullyConfigured)
            throw new InvalidOperationException(
                "Live integration not configured: " + LiveIntegrationGate.DescribeConfiguration());

        var report = new SshConnectionDiagnosticReport
        {
            SessionName = "live-it",
            Host = LiveIntegrationGate.Host!,
            Port = LiveIntegrationGate.Port,
            Username = LiveIntegrationGate.User!,
            AuthMethod = LiveIntegrationGate.AuthMethod
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await SshConnectionProbe.ProbeEndpointAsync(
            report,
            LiveIntegrationGate.Host!,
            LiveIntegrationGate.Port,
            timeoutSeconds: 15,
            cts.Token);

        Assert.Contains(report.Steps, s => s.Phase == "DNS");
        Assert.True(
            report.FailureKind is SshFailureKind.None or SshFailureKind.Network or SshFailureKind.Timeout
                or SshFailureKind.ConnectionRefused or SshFailureKind.Dns,
            $"Unexpected failure kind {report.FailureKind}");
    }
}
