using XTerminal.Core.Models.X11;

namespace XTerminal.Core.Settings;

public sealed class AppSettings
{
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.Dark;
    public WindowMaterialMode WindowMaterial { get; set; } = WindowMaterialMode.Mica;
    public bool IsActivityBarExpanded { get; set; }
    public bool IsUtilityPanelOpen { get; set; }

    public GeneralSettings General { get; set; } = new();
    public TerminalAppearanceSettings Terminal { get; set; } = new();
    public EditorAppearanceSettings Editor { get; set; } = new();
    public HistorySettings History { get; set; } = new();
    public SshAppSettings Ssh { get; set; } = new();
    public X11AppSettings X11 { get; set; } = new();
    public FilesAppSettings Files { get; set; } = new();
    public SecurityAppSettings Security { get; set; } = new();
    public AdvancedAppSettings Advanced { get; set; } = new();
    public List<KeybindingEntry> Keybindings { get; set; } = new();
    public List<ShellProfileSettings> Profiles { get; set; } = ShellProfileSettings.CreateDefaults();
}

public sealed class GeneralSettings
{
    /// <summary>Open a default local shell on startup.</summary>
    public bool OpenDefaultShellOnStartup { get; set; } = true;
    public string DefaultWorkspace { get; set; } = "Terminal";
    public bool ConfirmExitWithActiveSessions { get; set; } = true;
    /// <summary>Remember last activity section (not live terminal processes).</summary>
    public bool RestoreLastActivity { get; set; } = true;
    public bool ShowToastNotifications { get; set; } = true;
}

public sealed class TerminalAppearanceSettings
{
    public string FontFamily { get; set; } = "Cascadia Mono";
    public double FontSize { get; set; } = 13;
    public string FontWeight { get; set; } = "Normal";
    public double LineHeight { get; set; } = 1.35;
    public double LetterSpacing { get; set; }
    public string SchemeId { get; set; } = "xterminal-dark";
    public CursorStyleMode CursorStyle { get; set; } = CursorStyleMode.Bar;
    public bool CursorBlink { get; set; } = true;
    public int Padding { get; set; } = 10;
    public bool ShowScrollbar { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public int ScrollbackLines { get; set; } = 5000;
    public bool CopyOnSelect { get; set; }
    public string RightClickAction { get; set; } = "ContextMenu"; // ContextMenu | Paste | CopyPaste
    public string BellStyle { get; set; } = "Visual"; // None | Visual | Audible
    public string DefaultShell { get; set; } = "PowerShell"; // PowerShell | Cmd | Wsl
}

public sealed class SshAppSettings
{
    public int ConnectionTimeoutSeconds { get; set; } = 20;
    public int KeepAliveIntervalSeconds { get; set; } = 30;
    public int DefaultPort { get; set; } = 22;
    public string DefaultUsername { get; set; } = "";
    public bool AutoReconnect { get; set; }
    public int MaxReconnectAttempts { get; set; } = 3;
    public bool StrictHostKeyChecking { get; set; } = true;
}

public sealed class FilesAppSettings
{
    public string DefaultDownloadDirectory { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
    public string TransferConflictBehavior { get; set; } = "Prompt"; // Prompt | Overwrite | Skip | Rename
    public bool ShowHiddenFiles { get; set; }
    public bool HumanReadableSizes { get; set; } = true;
    public bool NotifyOnTransferComplete { get; set; } = true;
    /// <summary>Max concurrent SFTP transfers on a single connection (1–4). Default 1 for SSH.NET safety.</summary>
    public int MaxConcurrentTransfers { get; set; } = 1;
    /// <summary>Maximum remote text file size openable in the integrated editor (bytes).</summary>
    public long MaxEditorFileBytes { get; set; } = 2_000_000;
}

public sealed class SecurityAppSettings
{
    public bool ConfirmBroadcastMultiLinePaste { get; set; } = true;
    public bool WarnBroadcastToMultipleHosts { get; set; } = true;
    public bool BlockBroadcastWhilePrivateInput { get; set; } = true;
}

public sealed class AdvancedAppSettings
{
    public bool EnableFileLogging { get; set; } = true;
    public int MaxLogFiles { get; set; } = 14;
    /// <summary>Parse OSC 7/133/633 shell-integration sequences when the shell emits them.</summary>
    public bool EnableShellIntegration { get; set; }
    public bool ShowCommandExecutionStatus { get; set; } = true;
    public bool ShowCommandDuration { get; set; } = true;
    public bool TrackTerminalWorkingDirectory { get; set; } = true;
    public bool FollowTerminalDirectoryInSftp { get; set; }
    public bool ConfirmNonLoopbackPortBinds { get; set; } = true;
    /// <summary>Restore tab layout on startup (never restores terminal buffers or auto-runs commands). Phase 3D.</summary>
    public bool RestoreWorkspaceLayout { get; set; } = true;
    /// <summary>When true with RestoreWorkspaceLayout, attempt SSH reconnect for restored SSH tabs. Phase 3D.</summary>
    public bool AutoReconnectRestoredSessions { get; set; }
}

public sealed class KeybindingEntry
{
    public string CommandId { get; set; } = "";
    public string Gesture { get; set; } = "";
}

public sealed class ShellProfileSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Shell { get; set; } = "PowerShell"; // PowerShell | Cmd | Wsl
    public string? StartupDirectory { get; set; }
    public string? StartupArgs { get; set; }
    public string Glyph { get; set; } = "▸";
    public bool IsDefault { get; set; }

    public static List<ShellProfileSettings> CreateDefaults() =>
    [
        new() { Name = "PowerShell", Shell = "PowerShell", Glyph = "❯", IsDefault = true },
        new() { Name = "Command Prompt", Shell = "Cmd", Glyph = "›" },
        new() { Name = "WSL", Shell = "Wsl", Glyph = "◆" }
    ];
}
