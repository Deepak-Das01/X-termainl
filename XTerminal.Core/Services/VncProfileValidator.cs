using XTerminal.Core.Models.Desktop;

namespace XTerminal.Core.Services;

public static class VncProfileValidator
{
    public static bool TryValidate(VncSessionProfile profile, out string error)
    {
        error = "";
        if (profile is null) { error = "VNC profile is required."; return false; }
        if (string.IsNullOrWhiteSpace(profile.Host)) { error = "Host is required."; return false; }
        if (profile.Host.Contains('@')) { error = "VNC host must not include username@ credentials."; return false; }
        if (profile.Port is < 1 or > 65535) { error = "Port must be 1–65535."; return false; }
        if (profile.ConnectionTimeoutSeconds is < 3 or > 120) { error = "Timeout must be 3–120 seconds."; return false; }
        if (!Enum.IsDefined(typeof(VncAuthenticationMode), profile.AuthenticationMode))
        {
            error = "Authentication mode is invalid.";
            return false;
        }
        if (profile.AuthenticationMode == VncAuthenticationMode.None && !profile.AllowUnencrypted)
        {
            error = "Unauthenticated VNC requires explicit AllowUnencrypted acknowledgment.";
            return false;
        }

        if (profile.UseSshTunnel)
        {
            if (profile.SshTunnelSessionId is null || profile.SshTunnelSessionId == Guid.Empty)
            {
                error = "VNC over SSH requires an explicitly selected SSH profile.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(profile.TunnelRemoteHost))
            {
                error = "Tunnel remote host is required.";
                return false;
            }

            if (profile.TunnelRemotePort is < 1 or > 65535)
            {
                error = "Tunnel remote port must be 1–65535.";
                return false;
            }
        }

        return true;
    }

    /// <summary>Maps display number N to TCP port 5900+N when callers pass display semantics.</summary>
    public static int ResolvePort(int portOrDisplay, bool treatAsDisplayNumber)
    {
        if (!treatAsDisplayNumber) return portOrDisplay;
        if (portOrDisplay is >= 0 and <= 99) return 5900 + portOrDisplay;
        return portOrDisplay;
    }
}
