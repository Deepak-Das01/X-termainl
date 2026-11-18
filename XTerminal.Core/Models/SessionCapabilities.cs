namespace XTerminal.Core.Models;

/// <summary>
/// Protocol capability matrix. Unsupported ≠ temporary failure — gate UI actions accordingly.
/// </summary>
[Flags]
public enum SessionCapability
{
    None = 0,
    InteractiveInput = 1 << 0,
    TerminalOutput = 1 << 1,
    TerminalResize = 1 << 2,
    Reconnect = 1 << 3,
    Sftp = 1 << 4,
    PortForwarding = 1 << 5,
    ShellIntegration = 1 << 6,
    ConnectionDiagnostics = 1 << 7,
    BinaryData = 1 << 8,
    LocalEchoOptional = 1 << 9,
    ControlSignals = 1 << 10,
    PlaintextNetwork = 1 << 11
}

public static class SessionCapabilities
{
    public static SessionCapability For(SessionKind kind) => kind switch
    {
        SessionKind.LocalShell =>
            SessionCapability.InteractiveInput |
            SessionCapability.TerminalOutput |
            SessionCapability.TerminalResize |
            SessionCapability.Reconnect |
            SessionCapability.ShellIntegration |
            SessionCapability.ConnectionDiagnostics,
        SessionKind.Ssh =>
            SessionCapability.InteractiveInput |
            SessionCapability.TerminalOutput |
            SessionCapability.TerminalResize |
            SessionCapability.Reconnect |
            SessionCapability.Sftp |
            SessionCapability.PortForwarding |
            SessionCapability.ShellIntegration |
            SessionCapability.ConnectionDiagnostics,
        SessionKind.Serial =>
            SessionCapability.InteractiveInput |
            SessionCapability.TerminalOutput |
            SessionCapability.Reconnect |
            SessionCapability.ConnectionDiagnostics |
            SessionCapability.BinaryData |
            SessionCapability.LocalEchoOptional |
            SessionCapability.ControlSignals,
        SessionKind.Telnet =>
            SessionCapability.InteractiveInput |
            SessionCapability.TerminalOutput |
            SessionCapability.TerminalResize |
            SessionCapability.Reconnect |
            SessionCapability.ConnectionDiagnostics |
            SessionCapability.LocalEchoOptional |
            SessionCapability.PlaintextNetwork,
        _ => SessionCapability.None
    };

    public static bool Has(SessionKind kind, SessionCapability capability) =>
        (For(kind) & capability) == capability;

    public static string UnsupportedReason(SessionKind kind, SessionCapability capability) =>
        capability switch
        {
            SessionCapability.Sftp => $"{kind} sessions do not support SFTP file transfers.",
            SessionCapability.PortForwarding => $"{kind} sessions do not support SSH port forwarding.",
            SessionCapability.ShellIntegration => $"{kind} sessions do not provide shell-integration OSC hooks.",
            SessionCapability.TerminalResize =>
                kind == SessionKind.Serial
                    ? "Serial devices do not receive terminal resize (NAWS). Local display size may still change."
                    : $"{kind} does not support remote resize.",
            _ => $"{kind} does not support {capability}."
        };
}
