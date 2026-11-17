namespace XTerminal.Core.Models.Ui;

public sealed class UiStateDocument
{
    public int Version { get; set; } = 1;
    public List<Guid> CollapsedSessionFolderIds { get; set; } = new();
    public string? FilesLastPath { get; set; }
    public List<string> FilesExpandedPaths { get; set; } = new();
    /// <summary>Last activity section name (Terminal, Sessions, …). Not live processes.</summary>
    public string? LastActivitySection { get; set; }
}
