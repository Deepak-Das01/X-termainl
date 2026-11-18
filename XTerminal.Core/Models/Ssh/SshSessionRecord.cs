using XTerminal.Core.Models.X11;

namespace XTerminal.Core.Models.Ssh;

/// <summary>
/// Non-secret SSH session metadata. Credentials live in Windows Credential Manager.
/// </summary>
public sealed class SshSessionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New SSH session";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public SshAuthMethod AuthMethod { get; set; } = SshAuthMethod.Password;

    /// <summary>Path to private key file (not the key material).</summary>
    public string? PrivateKeyPath { get; set; }

    public string? StartupDirectory { get; set; }
    public string? StartupCommand { get; set; }
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    public int ConnectionTimeoutSeconds { get; set; } = 30;
    public int KeepAliveIntervalSeconds { get; set; } = 30;
    public bool Compression { get; set; }
    public string TerminalType { get; set; } = "xterm-256color";

    public string? ProxyJumpHost { get; set; }
    public int ProxyJumpPort { get; set; } = 22;
    public string? ProxyJumpUsername { get; set; }
    public SshAuthMethod ProxyJumpAuthMethod { get; set; } = SshAuthMethod.Password;
    public string? ProxyJumpPrivateKeyPath { get; set; }

    public bool StrictHostKeyChecking { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;
    public int MaxReconnectAttempts { get; set; } = 3;

    public List<SshPortForwardRecord> PortForwards { get; set; } = new();

    /// <summary>X11 preferences. Forwarding remains gated until a supported SSH backend exists. Never stores cookies.</summary>
    public X11SessionSettings X11 { get; set; } = new();

    public Guid? FolderId { get; set; }
    public bool IsFavorite { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTimeOffset? LastConnectedUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SshFolderRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Folder";
    public Guid? ParentId { get; set; }
}

public sealed class SshLibraryDocument
{
    public List<SshFolderRecord> Folders { get; set; } = new();
    public List<SshSessionRecord> Sessions { get; set; } = new();
}
