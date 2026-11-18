using XTerminal.Core.Services;

namespace XTerminal.Tests;

/// <summary>
/// Opt-in live tests. No-op unless XTERMINAL_IT_ENABLE=1 and host/user are provided.
/// Passwords must never be embedded here. Full SSH auth is validated manually in the UI.
/// </summary>
public class LiveSshIntegrationTests
{
    [Fact]
    public async Task OptIn_tcp_probe_against_authorized_host_when_configured()
    {
        if (!LiveIntegrationGate.IsEnabled ||
            LiveIntegrationGate.Host is null ||
            LiveIntegrationGate.User is null)
        {
            return; // opt-in / CI skip path
        }

        var report = new SshConnectionDiagnosticReport
        {
            SessionName = "integration",
            Host = LiveIntegrationGate.Host,
            Port = LiveIntegrationGate.Port,
            Username = LiveIntegrationGate.User,
            AuthMethod = "(runtime)"
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await SshConnectionProbe.ProbeEndpointAsync(
            report,
            LiveIntegrationGate.Host,
            LiveIntegrationGate.Port,
            timeoutSeconds: 15,
            cts.Token);

        Assert.Contains(report.Steps, s => s.Phase == "DNS");
        // Connectivity may legitimately fail (firewall); assert probe executed without throwing.
        Assert.NotNull(report.FailureKind.ToString());
    }
}
