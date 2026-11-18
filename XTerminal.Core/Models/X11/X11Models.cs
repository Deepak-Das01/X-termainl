namespace XTerminal.Core.Models.X11;

/// <summary>Known Windows X server products (bring-your-own; never auto-installed).</summary>
public enum XServerProduct
{
    Unknown = 0,
    VcXsrv = 1,
    Xming = 2,
    Custom = 3
}

/// <summary>Forwarding trust mode. Trusted is never the default and requires acknowledgment.</summary>
public enum X11TrustMode
{
    Untrusted = 0,
    Trusted = 1
}

/// <summary>Accurate UI / diagnostics states for X11 (no simulated success).</summary>
public enum X11ForwardingStatus
{
    Disabled = 0,
    BackendUnavailable = 1,
    XServerUnavailable = 2,
    Ready = 3,
    Requesting = 4,
    Accepted = 5,
    Rejected = 6,
    ActiveGraphical = 7,
    Disconnected = 8
}

/// <summary>
/// Persisted SSH-profile X11 preferences. Never stores cookies, authority files, or process handles.
/// </summary>
public sealed class X11SessionSettings
{
    /// <summary>User intent to enable X11 when a supported backend exists. Default false.</summary>
    public bool EnableForwarding { get; set; }

    public X11TrustMode TrustMode { get; set; } = X11TrustMode.Untrusted;

    /// <summary>Required before TrustMode=Trusted is accepted by validators.</summary>
    public bool TrustedForwardingAcknowledged { get; set; }

    /// <summary>Preferred display number (e.g. 0 → :0). Null = auto / server default.</summary>
    public int? DisplayNumber { get; set; }

    /// <summary>Optional absolute path to a BYO X server executable.</summary>
    public string? XServerExecutablePath { get; set; }

    public XServerProduct PreferredProduct { get; set; } = XServerProduct.Unknown;

    /// <summary>Seconds to wait for x11-req acceptance when a backend exists (5–120).</summary>
    public int ForwardingTimeoutSeconds { get; set; } = 20;

    /// <summary>Whether to re-request X11 after SSH reconnect (never auto-starts X server on restore).</summary>
    public bool ReconnectWithSession { get; set; }
}

/// <summary>Application-wide BYO X server preferences (Settings).</summary>
public sealed class X11AppSettings
{
    public string? DefaultExecutablePath { get; set; }
    public XServerProduct PreferredProduct { get; set; } = XServerProduct.Unknown;
    public int? DefaultDisplayNumber { get; set; }
    public bool PreferLoopbackOnly { get; set; } = true;
}

/// <summary>Sanitized diagnostic snapshot — never includes cookies or absolute PIDs in exports.</summary>
public sealed class X11DiagnosticSnapshot
{
    public bool BackendAvailable { get; init; }
    public string BackendName { get; init; } = "none";
    public string BackendBlocker { get; init; } = "";
    public bool XServerConfigured { get; init; }
    public bool XServerExecutableValid { get; init; }
    public bool XServerRunningOwned { get; init; }
    public bool AuthenticationConfigured { get; init; }
    public bool LocalTransportReady { get; init; }
    public X11ForwardingStatus ForwardingStatus { get; init; }
    public string Summary { get; init; } = "";
}
