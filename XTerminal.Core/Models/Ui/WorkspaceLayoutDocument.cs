namespace XTerminal.Core.Models.Ui;

public sealed class WorkspaceLayoutDocument
{
    public int Version { get; set; } = 1;
    public bool RestoreEnabled { get; set; } = true;
    public bool AutoReconnectRemote { get; set; }
    public string? ActiveSection { get; set; }
    public List<WorkspaceTabLayout> Tabs { get; set; } = new();
    public string? LocalFilesPath { get; set; }
}

public sealed class WorkspaceTabLayout
{
    public string Kind { get; set; } = "terminal"; // terminal | editor
    public string Title { get; set; } = "";
    public bool IsPinned { get; set; }
    public Guid? SshSessionId { get; set; }
    public string? LocalShell { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? EditorPath { get; set; }
    public bool EditorIsRemote { get; set; }
    public SplitLayoutNode? SplitRoot { get; set; }
}

public sealed class SplitLayoutNode
{
    public string Type { get; set; } = "leaf"; // leaf | branch
    public string? Orientation { get; set; } // Horizontal | Vertical
    public double Ratio { get; set; } = 0.5;
    public Guid? PaneSessionId { get; set; }
    public string? PaneTitle { get; set; }
    public string? LocalShell { get; set; }
    public string? WorkingDirectory { get; set; }
    public SplitLayoutNode? First { get; set; }
    public SplitLayoutNode? Second { get; set; }
}
