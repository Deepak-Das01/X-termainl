namespace XTerminal.Core.Models.History;

public sealed class HistoryDocument
{
    public int Version { get; set; } = 1;
    public List<HistoryEntry> Entries { get; set; } = new();
}

public sealed class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Command { get; set; } = string.Empty;
    public string SessionName { get; set; } = string.Empty;
    public string ShellType { get; set; } = "Unknown";
    public string? WorkingDirectory { get; set; }
    public string? Hostname { get; set; }
    /// <summary>Only set when a real shell-integration protocol provides it. Otherwise null.</summary>
    public int? ExitCode { get; set; }
    /// <summary>UI display — never invent exit codes without shell integration.</summary>
    public string ExitCodeDisplay => ExitCode?.ToString() ?? "—";
    public bool IsSsh { get; set; }
    /// <summary>ConPtyInferred | ShellIntegration | SshInferred</summary>
    public string CaptureSource { get; set; } = "ConPtyInferred";
}
