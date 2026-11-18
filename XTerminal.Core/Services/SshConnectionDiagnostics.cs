using System.Text.RegularExpressions;

namespace XTerminal.Core.Services;

public enum SshFailureKind
{
    None,
    InvalidConfiguration,
    Dns,
    ConnectionRefused,
    NetworkUnreachable,
    Network,
    Timeout,
    Negotiation,
    UnsupportedAlgorithms,
    UnknownHostKey,
    ChangedHostKey,
    HostKey,
    Authentication,
    JumpHost,
    ServerChannelRejection,
    Cancelled,
    Unknown
}

/// <summary>
/// Clipboard / share export privacy. Authentication method labels (e.g. Password, PrivateKey)
/// are not secrets; credential values and private endpoints are.
/// </summary>
public enum DiagnosticExportMode
{
    /// <summary>
    /// Default shareable export: redacts usernames, hosts, resolved addresses and jump topology.
    /// Retains auth-method labels. Step messages remain credential-sanitized.
    /// </summary>
    SanitizedPublic,

    /// <summary>
    /// Local troubleshooting view: includes host/user/DNS for the operator.
    /// Still redacts credential values from messages.
    /// </summary>
    DetailedLocal
}

public sealed class SshDiagnosticStep
{
    public DateTimeOffset Utc { get; set; } = DateTimeOffset.UtcNow;
    public string Phase { get; set; } = "";
    public string Message { get; set; } = "";
    public bool Success { get; set; } = true;
}

public sealed class SshConnectionDiagnosticReport
{
    public string SessionName { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public string AuthMethod { get; set; } = "";
    public string? JumpHost { get; set; }
    public string? ResolvedAddress { get; set; }
    public string? HostKeyFingerprint { get; set; }
    public SshFailureKind FailureKind { get; set; } = SshFailureKind.None;
    public string? FailureMessage { get; set; }
    public TimeSpan? Duration { get; set; }
    public List<SshDiagnosticStep> Steps { get; set; } = new();

    public void Add(string phase, string message, bool success = true) =>
        Steps.Add(new SshDiagnosticStep { Phase = phase, Message = Sanitize(message), Success = success });

    /// <summary>Sanitized public clipboard export (default).</summary>
    public string ToClipboardText() => ToClipboardText(DiagnosticExportMode.SanitizedPublic);

    public string ToClipboardText(DiagnosticExportMode mode)
    {
        var redactEndpoints = mode == DiagnosticExportMode.SanitizedPublic;
        var host = redactEndpoints ? RedactEndpoint(Host) : Host;
        var user = redactEndpoints ? RedactUsername(Username) : Username;
        var dns = redactEndpoints ? RedactEndpoint(ResolvedAddress) : (ResolvedAddress ?? "(n/a)");
        var jump = redactEndpoints ? RedactJump(JumpHost) : (JumpHost ?? "(none)");
        // Auth method labels (Password, PrivateKey, …) are not credentials.
        var auth = string.IsNullOrWhiteSpace(AuthMethod) ? "(n/a)" : AuthMethod;

        var lines = new List<string>
        {
            $"X-Terminal SSH diagnostics",
            $"Session: {SessionName}",
            $"Target: {user}@{host}:{Port}",
            $"Auth: {auth}",
            $"Jump: {jump}",
            $"DNS: {dns}",
            $"Host key: {HostKeyFingerprint ?? "(n/a)"}",
            $"Result: {(FailureKind == SshFailureKind.None ? "OK" : FailureKind.ToString())}",
            $"Detail: {FailureMessage ?? ""}",
            $"Duration: {Duration?.TotalSeconds:0.##}s",
            "--- Steps ---"
        };
        foreach (var s in Steps)
        {
            var msg = redactEndpoints ? RedactKnownEndpointsInText(s.Message) : s.Message;
            lines.Add($"{s.Utc:HH:mm:ss.fff} [{(s.Success ? "OK" : "FAIL")}] {s.Phase}: {msg}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Returns true if <paramref name="secret"/> appears in the export. Never throws with the secret value.
    /// </summary>
    public bool ExportContainsSecret(string? secret, DiagnosticExportMode mode = DiagnosticExportMode.SanitizedPublic)
    {
        if (string.IsNullOrEmpty(secret))
            return false;
        var export = ToClipboardText(mode);
        return export.Contains(secret, StringComparison.Ordinal);
    }

    private static readonly Regex CredentialAssignment = new(
        @"(?i)\b(password|passphrase)\s*[=:]\s*\S+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Redacts credential words and assignment values from free-form error text.
    /// Structured Auth method labels (e.g. "Auth: Password") are formatted separately in exports
    /// and are not secrets.
    /// </summary>
    public static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message)) return "";
        var s = CredentialAssignment.Replace(message, "$1=********");
        return s
            .Replace("password", "********", StringComparison.OrdinalIgnoreCase)
            .Replace("passphrase", "********", StringComparison.OrdinalIgnoreCase)
            .Replace("private key", "key material", StringComparison.OrdinalIgnoreCase);
    }

    private string RedactKnownEndpointsInText(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;
        var s = message;
        if (!string.IsNullOrWhiteSpace(Host) && Host.Length >= 3)
            s = s.Replace(Host, "[host]", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(Username) && Username.Length >= 2)
            s = s.Replace(Username, "[user]", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(ResolvedAddress) && ResolvedAddress.Length >= 3)
            s = s.Replace(ResolvedAddress, "[addr]", StringComparison.OrdinalIgnoreCase);
        return s;
    }

    private static string RedactEndpoint(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(n/a)" : "[redacted-host]";

    private static string RedactUsername(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(n/a)" : "[redacted-user]";

    private static string RedactJump(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(none)" : "[redacted-jump]";

    public static SshFailureKind Classify(Exception ex)
    {
        if (ex is OperationCanceledException) return SshFailureKind.Cancelled;
        if (ex is TimeoutException) return SshFailureKind.Timeout;

        var msg = ex.Message ?? "";
        if (msg.Contains("changed", StringComparison.OrdinalIgnoreCase) &&
            msg.Contains("host key", StringComparison.OrdinalIgnoreCase))
            return SshFailureKind.ChangedHostKey;

        if (msg.Contains("unknown host key", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("not in known_hosts", StringComparison.OrdinalIgnoreCase))
            return SshFailureKind.UnknownHostKey;

        if (msg.Contains("host key", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("known_hosts", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("fingerprint", StringComparison.OrdinalIgnoreCase))
            return SshFailureKind.HostKey;

        if (msg.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("password is not stored", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("private key", StringComparison.OrdinalIgnoreCase))
            return SshFailureKind.Authentication;

        if (msg.Contains("No such host", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase) ||
            ex is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.HostNotFound })
            return SshFailureKind.Dns;

        if (msg.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return SshFailureKind.Timeout;

        if (msg.Contains("jump", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("bastion", StringComparison.OrdinalIgnoreCase))
            return SshFailureKind.JumpHost;

        if (msg.Contains("unsupported", StringComparison.OrdinalIgnoreCase) &&
            msg.Contains("algorithm", StringComparison.OrdinalIgnoreCase))
            return SshFailureKind.UnsupportedAlgorithms;

        if (msg.Contains("negotiation", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("kex", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("algorithm", StringComparison.OrdinalIgnoreCase))
            return SshFailureKind.Negotiation;

        if (msg.Contains("channel", StringComparison.OrdinalIgnoreCase) &&
            (msg.Contains("open failed", StringComparison.OrdinalIgnoreCase) ||
             msg.Contains("rejected", StringComparison.OrdinalIgnoreCase)))
            return SshFailureKind.ServerChannelRejection;

        if (ex is System.Net.Sockets.SocketException sock)
        {
            return sock.SocketErrorCode switch
            {
                System.Net.Sockets.SocketError.ConnectionRefused => SshFailureKind.ConnectionRefused,
                System.Net.Sockets.SocketError.NetworkUnreachable
                    or System.Net.Sockets.SocketError.HostUnreachable => SshFailureKind.NetworkUnreachable,
                _ => SshFailureKind.Network
            };
        }

        if (msg.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
            return SshFailureKind.ConnectionRefused;

        if (msg.Contains("unreachable", StringComparison.OrdinalIgnoreCase))
            return SshFailureKind.NetworkUnreachable;

        if (msg.Contains("network", StringComparison.OrdinalIgnoreCase))
            return SshFailureKind.Network;

        if (msg.Contains("invalid", StringComparison.OrdinalIgnoreCase) &&
            (msg.Contains("config", StringComparison.OrdinalIgnoreCase) ||
             msg.Contains("host", StringComparison.OrdinalIgnoreCase) ||
             msg.Contains("port", StringComparison.OrdinalIgnoreCase)))
            return SshFailureKind.InvalidConfiguration;

        return SshFailureKind.Unknown;
    }
}
