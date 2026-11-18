namespace XTerminal.Core.Services.X11;

/// <summary>
/// Reports whether genuine SSH X11 forwarding can be performed with the installed stack.
/// Phase 4C: SSH.NET 2026.0.0 has no public X11 API — always unavailable.
/// </summary>
public static class X11BackendCapability
{
    public const string BackendId = "sshnet-2026";
    public const string BackendDisplayName = "SSH.NET 2026.0.0";

    /// <summary>True only when a supported public API can send x11-req and own inbound x11 channels.</summary>
    public static bool IsForwardingAvailable => false;

    public static string BlockerReason =>
        "SSH.NET 2026.0.0 exposes SendX11ForwardingRequest only on internal ChannelSession; " +
        "ShellStream never sends x11-req; inbound x11 channel handling is not publicly accessible. " +
        "TCP remote forwards are not genuine X11. See docs/adr/ADR-004C-X11-FORWARDING.md.";

    public static string Guidance =>
        "X11 preferences can be saved for a future backend. Install a local X server independently if desired. " +
        "Do not enable trusted forwarding until a supported backend exists.";
}
