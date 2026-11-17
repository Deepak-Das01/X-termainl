using System.Diagnostics;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Security;
using XTerminal.Core.Services;

namespace XTerminal.Protocols.Ssh;

public sealed class SshConnectionTester
{
    private readonly ISecretStore _secrets;
    private readonly SshHostKeyPolicy _hostKeys;

    public SshConnectionTester(ISecretStore secrets, SshHostKeyPolicy hostKeys)
    {
        _secrets = secrets;
        _hostKeys = hostKeys;
    }

    public async Task<(bool Ok, string Message)> TestAsync(
        SshSessionRecord session,
        CancellationToken cancellationToken = default)
    {
        var report = await DiagnoseAsync(session, cancellationToken);
        var ok = report.FailureKind == SshFailureKind.None;
        var message = ok
            ? "Authentication succeeded."
            : $"[{report.FailureKind}] {report.FailureMessage ?? "failed"}";
        return (ok, message);
    }

    /// <summary>
    /// Runs DNS → TCP → (optional jump progress) → SSH auth for the selected session only.
    /// Never includes passwords, passphrases, or key material in the report.
    /// </summary>
    public async Task<SshConnectionDiagnosticReport> DiagnoseAsync(
        SshSessionRecord session,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var report = new SshConnectionDiagnosticReport
        {
            SessionName = session.Name,
            Host = session.Host,
            Port = session.Port <= 0 ? 22 : session.Port,
            Username = session.Username,
            AuthMethod = session.AuthMethod.ToString(),
            JumpHost = string.IsNullOrWhiteSpace(session.ProxyJumpHost)
                ? null
                : $"{session.ProxyJumpUsername ?? session.Username}@{session.ProxyJumpHost}:{ (session.ProxyJumpPort <= 0 ? 22 : session.ProxyJumpPort)}"
        };

        if (string.IsNullOrWhiteSpace(session.Host) || string.IsNullOrWhiteSpace(session.Username))
        {
            report.FailureKind = SshFailureKind.InvalidConfiguration;
            report.FailureMessage = "Host and username are required.";
            report.Add("Profile", report.FailureMessage, success: false);
            report.Duration = sw.Elapsed;
            return report;
        }

        report.Add("Profile", $"Validated profile “{session.Name}” ({session.AuthMethod}).");

        var hops = JumpHostChain.FromSession(session);
        var hopErrors = JumpHostChain.Validate(hops, report.Host, report.Port);
        if (hopErrors.Count > 0)
        {
            report.FailureKind = SshFailureKind.JumpHost;
            report.FailureMessage = string.Join(" ", hopErrors);
            report.Add("Jump", report.FailureMessage, success: false);
            report.Duration = sw.Elapsed;
            return report;
        }

        foreach (var hop in hops)
        {
            report.Add("Jump", $"Probing hop {hop.Username ?? "(default)"}@{hop.Host}:{hop.Port}");
            await SshConnectionProbe.ProbeEndpointAsync(
                report, hop.Host, hop.Port, session.ConnectionTimeoutSeconds, cancellationToken);
            if (report.FailureKind != SshFailureKind.None)
            {
                report.Duration = sw.Elapsed;
                return report;
            }
        }

        await SshConnectionProbe.ProbeEndpointAsync(
            report, report.Host, report.Port, session.ConnectionTimeoutSeconds, cancellationToken);
        if (report.FailureKind != SshFailureKind.None && hops.Count == 0)
        {
            report.Duration = sw.Elapsed;
            return report;
        }

        // When jump hosts are configured, direct TCP to the target may fail from the workstation;
        // clear network failure from the direct probe and continue with tunneled auth.
        if (hops.Count > 0 && report.FailureKind is SshFailureKind.Network or SshFailureKind.Timeout or SshFailureKind.Dns)
        {
            report.Add("TCP", "Direct target reachability failed (expected behind bastion); continuing via jump.", success: true);
            report.FailureKind = SshFailureKind.None;
            report.FailureMessage = null;
        }
        else if (report.FailureKind != SshFailureKind.None)
        {
            report.Duration = sw.Elapsed;
            return report;
        }

        try
        {
            report.Add("SSH", $"Authenticating as {session.Username} ({session.AuthMethod})…");
            var factory = new SshTransportFactory(_secrets, _hostKeys);
            var owned = await factory.OpenSshClientAsync(session, cancellationToken);
            try
            {
                if (!owned.Client.IsConnected)
                {
                    report.FailureKind = SshFailureKind.Negotiation;
                    report.FailureMessage = "Connected but session was not active.";
                    report.Add("SSH", report.FailureMessage, success: false);
                }
                else
                {
                    report.Add("SSH", "Handshake and authentication succeeded.");
                    try
                    {
                        // Fingerprint already validated by host-key policy during Connect.
                        report.HostKeyFingerprint = "(verified via host-key policy)";
                        report.Add("HostKey", "Target host key accepted under strict checking.");
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            finally
            {
                try { if (owned.Client.IsConnected) owned.Client.Disconnect(); } catch { /* ignore */ }
                try { owned.Client.Dispose(); } catch { /* ignore */ }
                owned.Ownership?.Dispose();
            }
        }
        catch (Exception ex)
        {
            report.FailureKind = SshConnectionDiagnosticReport.Classify(ex);
            report.FailureMessage = SshConnectionDiagnosticReport.Sanitize(ex.Message);
            report.Add("SSH", report.FailureMessage, success: false);
        }

        report.Duration = sw.Elapsed;
        return report;
    }
}
