namespace XTerminal.Core.Models.Snippets;

public sealed class SnippetDocument
{
    public int Version { get; set; } = 1;
    public List<SnippetFolderRecord> Folders { get; set; } = new();
    public List<SnippetRecord> Snippets { get; set; } = new();
}

public sealed class SnippetFolderRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Folder";
    public Guid? ParentId { get; set; }
}

public sealed class SnippetRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Snippet";
    public string? Description { get; set; }
    public string Command { get; set; } = string.Empty;
    public string ShellCompatibility { get; set; } = "Any";
    public List<string> Tags { get; set; } = new();
    public Guid? FolderId { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
}
