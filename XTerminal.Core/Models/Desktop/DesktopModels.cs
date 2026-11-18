namespace XTerminal.Core.Models.Desktop;

public enum VncAuthenticationMode
{
    /// <summary>Classic VNC password authentication (challenge-response). Not transport encryption.</summary>
    VncAuth = 0,
    /// <summary>Security type None — requires explicit acknowledgment.</summary>
    None = 1
}

public enum DesktopScalingMode
{
    FitWindow = 0,
    Original = 1
}

public sealed class RdpSessionProfile
{
    public string Name { get; set; } = "RDP";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3389;
    public string Username { get; set; } = "";
    public string? Domain { get; set; }
    public int ConnectionTimeoutSeconds { get; set; } = 30;
    public int DesktopWidth { get; set; } = 1280;
    public int DesktopHeight { get; set; } = 720;
    public int ColorDepth { get; set; } = 32;
    public bool SmartSizing { get; set; } = true;
    public bool Fullscreen { get; set; }
    public bool RequireNla { get; set; } = true;
    /// <summary>0=no auth warning, 1=prompt, 2=require auth (default).</summary>
    public int AuthenticationLevel { get; set; } = 2;
    public bool RedirectClipboard { get; set; }
    public bool RedirectDrives { get; set; }
    public bool RedirectPrinters { get; set; }
    public bool RedirectAudio { get; set; }
    public bool AutoReconnect { get; set; }
}

public sealed class VncSessionProfile
{
    public string Name { get; set; } = "VNC";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 5900;
    public VncAuthenticationMode AuthenticationMode { get; set; } = VncAuthenticationMode.VncAuth;
    public bool ReadOnly { get; set; }
    public DesktopScalingMode Scaling { get; set; } = DesktopScalingMode.FitWindow;
    public int ConnectionTimeoutSeconds { get; set; } = 20;
    public bool Fullscreen { get; set; }
    public bool ShareClipboard { get; set; }
    public bool AutoReconnect { get; set; }
    /// <summary>User acknowledged plaintext/legacy VNC for this host.</summary>
    public bool AllowUnencrypted { get; set; }
}
