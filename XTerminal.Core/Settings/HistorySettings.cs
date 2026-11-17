namespace XTerminal.Core.Settings;

public sealed class HistorySettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>7, 30, 90, or 0 for forever.</summary>
    public int RetentionDays { get; set; } = 30;
    public int MaxEntries { get; set; } = 10_000;
    public bool ExcludeSshSessions { get; set; }
    /// <summary>Case-insensitive substrings; empty by default.</summary>
    public List<string> ExcludePatterns { get; set; } = new();
}
