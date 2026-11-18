using XTerminal.Core.Models.Desktop;

namespace XTerminal.Core.Services;

public static class RdpProfileValidator
{
    public static bool TryValidate(RdpSessionProfile profile, out string error)
    {
        error = "";
        if (profile is null) { error = "RDP profile is required."; return false; }
        if (string.IsNullOrWhiteSpace(profile.Host)) { error = "Host is required."; return false; }
        if (profile.Host.Contains('@')) { error = "Use Username/Domain fields — do not embed credentials in the host."; return false; }
        if (profile.Port is < 1 or > 65535) { error = "Port must be 1–65535."; return false; }
        if (profile.ConnectionTimeoutSeconds is < 5 or > 120) { error = "Timeout must be 5–120 seconds."; return false; }
        if (profile.DesktopWidth is < 640 or > 8192 || profile.DesktopHeight is < 480 or > 8192)
        {
            error = "Desktop resolution must be between 640×480 and 8192×8192.";
            return false;
        }
        if (profile.ColorDepth is not (8 or 15 or 16 or 24 or 32))
        {
            error = "Color depth must be 8, 15, 16, 24, or 32.";
            return false;
        }
        if (profile.AuthenticationLevel is < 0 or > 2)
        {
            error = "Authentication level must be 0–2.";
            return false;
        }
        if (!profile.RequireNla && profile.AuthenticationLevel < 2)
        {
            error = "Disabling NLA while lowering authentication level is not allowed by policy.";
            return false;
        }
        return true;
    }
}
