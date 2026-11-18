using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using XTerminal.Core.Services;

namespace XTerminal.Tests;

/// <summary>
/// Deterministic probe failure classification. These are not positive live connectivity tests
/// and do not require an authorized private server.
/// </summary>
public class SshConnectionProbeClassificationTests
{
    [Fact]
    public async Task Dns_failure_classified()
    {
        var report = new SshConnectionDiagnosticReport { Host = "no-such-host.invalid", Port = 22 };
        var sw = Stopwatch.StartNew();
        await SshConnectionProbe.ProbeEndpointAsync(report, "no-such-host.invalid", 22, timeoutSeconds: 5);
        sw.Stop();
        report.Duration ??= sw.Elapsed;

        Assert.Equal(SshFailureKind.Dns, report.FailureKind);
        Assert.Contains(report.Steps, s => s.Phase == "DNS" && !s.Success);
        Assert.True(report.Duration.HasValue);
    }

    [Fact]
    public async Task Connection_refused_classified()
    {
        // Bind then close to learn a free port, then probe the closed port on loopback.
        int port;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var report = new SshConnectionDiagnosticReport { Host = "127.0.0.1", Port = port };
        var sw = Stopwatch.StartNew();
        await SshConnectionProbe.ProbeEndpointAsync(report, "127.0.0.1", port, timeoutSeconds: 5);
        sw.Stop();
        report.Duration ??= sw.Elapsed;

        Assert.Equal(SshFailureKind.ConnectionRefused, report.FailureKind);
        Assert.Contains(report.Steps, s => s.Phase == "TCP" && !s.Success);
    }

    [Fact]
    public async Task Timeout_classified()
    {
        // TEST-NET-3 documentation address — typically blackholed (no RST).
        var report = new SshConnectionDiagnosticReport { Host = "203.0.113.1", Port = 22 };
        var sw = Stopwatch.StartNew();
        await SshConnectionProbe.ProbeEndpointAsync(report, "203.0.113.1", 22, timeoutSeconds: 3);
        sw.Stop();
        report.Duration ??= sw.Elapsed;

        Assert.True(
            report.FailureKind is SshFailureKind.Timeout or SshFailureKind.Network or SshFailureKind.NetworkUnreachable,
            $"Expected timeout/network classification, got {report.FailureKind}");
        Assert.NotEqual(SshFailureKind.None, report.FailureKind);
    }

    [Fact]
    public void Network_failure_classified_via_mapper()
    {
        var ex = new SocketException((int)SocketError.NetworkUnreachable);
        Assert.Equal(SshFailureKind.NetworkUnreachable, SshConnectionDiagnosticReport.Classify(ex));

        var net = new SocketException((int)SocketError.NetworkDown);
        Assert.Equal(SshFailureKind.Network, SshConnectionDiagnosticReport.Classify(net));
    }
}
