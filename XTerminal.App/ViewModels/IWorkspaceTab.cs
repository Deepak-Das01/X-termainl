namespace XTerminal.App.ViewModels;

public interface IWorkspaceTab
{
    Guid Id { get; }
    string Title { get; set; }
    string Glyph { get; }
    string Accent { get; }
    bool IsActive { get; set; }
    bool IsDirty { get; }
    bool IsEditor { get; }
    bool IsPinned { get; set; }
    bool IsTerminalGroup { get; }
}

public enum ClosedTabKind
{
    LocalShell,
    Ssh,
    EditorLocal,
    EditorRemote,
    Sftp
}

public sealed class ClosedTabRecord
{
    public ClosedTabKind Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Glyph { get; init; } = ">";
    public Core.Models.SessionDefinition? Session { get; init; }
    public string? LocalPath { get; init; }
    public Guid? SshSessionId { get; init; }
}
