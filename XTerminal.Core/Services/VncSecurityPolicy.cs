namespace XTerminal.Core.Services;

public static class VncSecurityPolicy
{
    public const string WarningTitle = "VNC transport may be unencrypted";

    public const string WarningBody =
        "Classic VNC authentication proves a password but does not encrypt the desktop session. " +
        "Framebuffer pixels, keyboard, and mouse traffic may be visible to network observers.\n\n" +
        "Prefer an SSH tunnel or TLS-capable VNC where available. Use legacy VNC only on authorized " +
        "lab or management networks.";

    public static string HostKey(string host, int port) =>
        $"{host.Trim().ToLowerInvariant()}:{(port <= 0 ? 5900 : port)}";
}

public interface IVncAcknowledgmentStore
{
    bool IsAcknowledged(string host, int port);
    void Acknowledge(string host, int port);
}
