namespace XTerminal.Core.Services;

/// <summary>
/// Telnet is plaintext. Connections require explicit per-host acknowledgment.
/// </summary>
public static class TelnetSecurityPolicy
{
    public const string WarningTitle = "Telnet is not encrypted";

    public const string WarningBody =
        "Telnet transmits terminal data — including usernames and passwords — in cleartext. " +
        "Network observers can read the session. Prefer SSH when available.\n\n" +
        "Use Telnet only on authorized legacy equipment, lab networks, or other environments where plaintext is acceptable.";

    public static string HostKey(string host, int port) =>
        $"{host.Trim().ToLowerInvariant()}:{(port <= 0 ? 23 : port)}";
}

/// <summary>Persists per-host Telnet acknowledgments (not credentials).</summary>
public interface ITelnetAcknowledgmentStore
{
    bool IsAcknowledged(string host, int port);
    void Acknowledge(string host, int port);
}
