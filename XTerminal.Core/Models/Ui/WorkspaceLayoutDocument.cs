namespace XTerminal.Core.Models.Ui;

/// <summary>
/// Versioned workspace metadata. Never contains passwords, passphrases, key material,
/// terminal buffers, or live session handles.
/// </summary>
public sealed class WorkspaceLayoutDocument
{
    public const int CurrentSchemaVersion = 2;
    public const int MaxTabs = 24;
    public const int MaxPanesPerTab = 16;
    public const int MaxSplitDepth = 6;

    public int Version { get; set; } = CurrentSchemaVersion;
    public Guid WorkspaceId { get; set; } = Guid.NewGuid();
    public string WorkspaceName { get; set; } = "Previous session";
    public bool RestoreEnabled { get; set; } = true;
    /// <summary>When true, restore may start SSH hosts for saved session references (opt-in).</summary>
    public bool AutoReconnectRemote { get; set; }
    public string? ActiveSection { get; set; }
    public Guid? ActiveTabId { get; set; }
    public bool ActivityBarExpanded { get; set; }
    public bool UtilityPanelOpen { get; set; }
    public double? SidebarWidth { get; set; }
    public List<WorkspaceTabLayout> Tabs { get; set; } = new();
    public string? LocalFilesPath { get; set; }
}

public sealed class WorkspaceTabLayout
{
    public Guid TabId { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "terminal"; // terminal | editor
    public string Title { get; set; } = "";
    public bool IsPinned { get; set; }
    public Guid? SshSessionId { get; set; }
    public string? LocalShell { get; set; }
    public string? WorkingDirectory { get; set; }
    /// <summary>SessionKind name for Serial/Telnet workspace leaves (credential-free).</summary>
    public string? ProtocolKind { get; set; }
    public Guid? SessionProfileId { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? SerialPort { get; set; }
    public int? BaudRate { get; set; }
    public string? EditorPath { get; set; }
    public bool EditorIsRemote { get; set; }
    public Guid? EditorSshSessionId { get; set; }
    public string? EditorLanguage { get; set; }
    public Guid? ActivePaneId { get; set; }
    public SplitLayoutNode? SplitRoot { get; set; }
}

public sealed class SplitLayoutNode
{
    public string Type { get; set; } = "leaf"; // leaf | branch
    public string? Orientation { get; set; } // Horizontal | Vertical
    public double Ratio { get; set; } = 0.5;
    public Guid PaneId { get; set; } = Guid.NewGuid();
    public Guid? PaneSessionId { get; set; }
    public string? PaneTitle { get; set; }
    public string? LocalShell { get; set; }
    public string? WorkingDirectory { get; set; }
    public Guid? SshSessionId { get; set; }
    public string? ProtocolKind { get; set; }
    public Guid? SessionProfileId { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? SerialPort { get; set; }
    public int? BaudRate { get; set; }
    public SplitLayoutNode? First { get; set; }
    public SplitLayoutNode? Second { get; set; }
}

/// <summary>Catalog of named workspace profiles (credential-free references only).</summary>
public sealed class WorkspaceProfileCatalog
{
    public int Version { get; set; } = 1;
    public Guid? DefaultWorkspaceId { get; set; }
    public List<WorkspaceProfileEntry> Profiles { get; set; } = new();
}

public sealed class WorkspaceProfileEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string FileName { get; set; } = "";
}
