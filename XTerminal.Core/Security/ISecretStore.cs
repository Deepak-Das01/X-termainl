namespace XTerminal.Core.Security;

public interface ISecretStore
{
    Task SaveSecretAsync(string key, string secret, CancellationToken cancellationToken = default);
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default);
}

public static class SshSecretKeys
{
    public static string Password(Guid sessionId) => $"XTerminal/SSH/{sessionId:N}/password";
    public static string Passphrase(Guid sessionId) => $"XTerminal/SSH/{sessionId:N}/passphrase";
    public static string JumpPassword(Guid sessionId) => $"XTerminal/SSH/{sessionId:N}/jump-password";
    public static string JumpPassphrase(Guid sessionId) => $"XTerminal/SSH/{sessionId:N}/jump-passphrase";
}

/// <summary>RDP/VNC secrets are isolated from SSH credential keys.</summary>
public static class DesktopSecretKeys
{
    public static string RdpPassword(Guid sessionId) => $"XTerminal/RDP/{sessionId:N}/password";
    public static string VncPassword(Guid sessionId) => $"XTerminal/VNC/{sessionId:N}/password";
}
