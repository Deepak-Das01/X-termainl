using System.Net;
using System.Net.Sockets;

namespace XTerminal.Core.Services;

public static class TelnetProfileValidator
{
    public static bool TryValidate(string host, int port, int timeoutSeconds, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(host))
        {
            error = "Host is required.";
            return false;
        }

        var h = host.Trim();
        if (h.Contains(' ') || h.Contains('\n') || h.Contains('\r'))
        {
            error = "Host must not contain whitespace.";
            return false;
        }

        if (port is < 1 or > 65535)
        {
            error = "Port must be 1–65535.";
            return false;
        }

        if (timeoutSeconds is < 3 or > 120)
        {
            error = "Connection timeout must be 3–120 seconds.";
            return false;
        }

        // Reject accidental SSH credential-style targets without claiming resolution.
        if (h.Contains('@'))
        {
            error = "Telnet host must not include username@ — Telnet does not reuse SSH credentials.";
            return false;
        }

        if (IPAddress.TryParse(h, out var ip) &&
            ip.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            error = "Unsupported address family.";
            return false;
        }

        return true;
    }
}
