using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace XTerminal.Core.Services;

/// <summary>
/// Non-intrusive DNS + TCP probe for a single SSH endpoint. Does not authenticate or scan networks.
/// </summary>
public static class SshConnectionProbe
{
    public static async Task ProbeEndpointAsync(
        SshConnectionDiagnosticReport report,
        string host,
        int port,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        port = port <= 0 ? 22 : port;
        timeoutSeconds = Math.Clamp(timeoutSeconds, 3, 120);
        var sw = Stopwatch.StartNew();

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            var primary = addresses.FirstOrDefault()?.ToString();
            report.ResolvedAddress = primary;
            report.Add("DNS", primary is null
                ? "No addresses returned"
                : $"Resolved {host} → {string.Join(", ", addresses.Take(4).Select(a => a.ToString()))}"
                  + (addresses.Length > 4 ? "…" : ""),
                success: primary is not null);
            if (primary is null)
            {
                report.FailureKind = SshFailureKind.Dns;
                report.FailureMessage = $"Could not resolve {host}";
                return;
            }
        }
        catch (Exception ex)
        {
            report.FailureKind = SshFailureKind.Dns;
            report.FailureMessage = SshConnectionDiagnosticReport.Sanitize(ex.Message);
            report.Add("DNS", report.FailureMessage, success: false);
            return;
        }
        finally
        {
            report.Duration = sw.Elapsed;
        }

        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            await client.ConnectAsync(host, port, cts.Token);
            report.Add("TCP", $"Connected to {host}:{port}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            report.FailureKind = SshFailureKind.Timeout;
            report.FailureMessage = $"TCP connect to {host}:{port} timed out after {timeoutSeconds}s";
            report.Add("TCP", report.FailureMessage, success: false);
        }
        catch (Exception ex)
        {
            report.FailureKind = SshConnectionDiagnosticReport.Classify(ex);
            if (report.FailureKind is SshFailureKind.None or SshFailureKind.Unknown)
                report.FailureKind = SshFailureKind.Network;
            report.FailureMessage = SshConnectionDiagnosticReport.Sanitize(ex.Message);
            report.Add("TCP", report.FailureMessage, success: false);
        }
        finally
        {
            report.Duration = sw.Elapsed;
        }
    }
}
