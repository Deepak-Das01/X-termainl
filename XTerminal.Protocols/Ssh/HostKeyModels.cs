namespace XTerminal.Protocols.Ssh;

public enum HostKeyStatus
{
    /// <summary>Fingerprint matches known_hosts.</summary>
    Trusted,
    /// <summary>No record for this host:port.</summary>
    Unknown,
    /// <summary>Host:port exists but fingerprint differs (possible MITM).</summary>
    Changed
}

public enum HostKeyPromptResult
{
    Reject,
    /// <summary>Accept for this connection only; do not persist.</summary>
    TrustOnce,
    /// <summary>Accept and write known_hosts (unknown hosts only).</summary>
    TrustAndSave,
    /// <summary>Explicitly replace a changed host key after user confirmation.</summary>
    ReplaceAndSave
}

public sealed class HostKeyPromptRequest
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string FingerprintSha256 { get; init; }
    public string KeyAlgorithm { get; init; } = "ssh-hostkey";
    public required HostKeyStatus Status { get; init; }
    public string? PreviousFingerprintSha256 { get; init; }
}

/// <summary>
/// UI or test double that decides host-key trust. Must be thread-safe for SSH.NET callbacks
/// (typically marshals to the UI thread).
/// </summary>
public interface ISshHostKeyPrompt
{
    HostKeyPromptResult Prompt(HostKeyPromptRequest request);
}
