using XTerminal.Core.Models.X11;

namespace XTerminal.Core.Services.X11;

public static class X11ProfileValidator
{
    public static bool TryValidate(X11SessionSettings settings, out string error)
    {
        error = "";
        if (settings is null)
        {
            error = "X11 settings are required.";
            return false;
        }

        if (settings.DisplayNumber is < 0 or > 63)
        {
            error = "Display number must be 0–63 when set.";
            return false;
        }

        if (settings.ForwardingTimeoutSeconds is < 5 or > 120)
        {
            error = "X11 forwarding timeout must be 5–120 seconds.";
            return false;
        }

        if (settings.TrustMode == X11TrustMode.Trusted && !settings.TrustedForwardingAcknowledged)
        {
            error = "Trusted X11 forwarding requires explicit acknowledgment of the security risk.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(settings.XServerExecutablePath))
        {
            var path = settings.XServerExecutablePath.Trim();
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                error = "X server path contains invalid characters.";
                return false;
            }

            // Do not require the file to exist here — availability is reported separately.
            if (!Path.IsPathRooted(path))
            {
                error = "X server executable path must be absolute.";
                return false;
            }
        }

        if (settings.EnableForwarding && !X11BackendCapability.IsForwardingAvailable)
        {
            // Preferences may be saved, but enabling cannot succeed until a backend exists.
            // Validation still succeeds so the UI can persist intent; runtime gate blocks use.
        }

        return true;
    }

    public static string TrustedWarningBody =>
        "Trusted X11 forwarding grants remote clients broad access to your local X server " +
        "(keystrokes, window contents, and input injection risks). Prefer untrusted forwarding. " +
        "Trusted mode is never enabled automatically.";
}
