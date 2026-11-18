namespace XTerminal.Core.Services;

/// <summary>
/// Canonical application data paths.
/// Override with environment variable <c>XTERMINAL_APPDATA</c> for isolated test profiles
/// (never points into the repository).
/// </summary>
public static class AppStoragePaths
{
    private static string? _overrideRoot;

    /// <summary>Set once at process start (tests / isolated validation instances).</summary>
    public static void SetRootOverride(string? path)
    {
        _overrideRoot = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());
        if (_overrideRoot is not null)
            Directory.CreateDirectory(_overrideRoot);
    }

    public static string Root
    {
        get
        {
            if (_overrideRoot is not null)
            {
                Directory.CreateDirectory(_overrideRoot);
                return _overrideRoot;
            }

            var fromEnv = Environment.GetEnvironmentVariable("XTERMINAL_APPDATA");
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                var root = Path.GetFullPath(fromEnv.Trim());
                Directory.CreateDirectory(root);
                return root;
            }

            var standard = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XTerminal");
            Directory.CreateDirectory(standard);
            return standard;
        }
    }

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Sessions => Path.Combine(Root, "sessions.json");
    public static string SshSessions => Path.Combine(Root, "ssh-sessions.json");
    public static string Snippets => Path.Combine(Root, "snippets.json");
    public static string History => Path.Combine(Root, "history.json");
    public static string PortForwards => Path.Combine(Root, "port-forwards.json");
    public static string UiState => Path.Combine(Root, "ui-state.json");
    public static string WorkspaceLayout => Path.Combine(Root, "workspace-layout.json");
    public static string WorkspacesDirectory => Path.Combine(Root, "workspaces");
    public static string KnownHosts => Path.Combine(Root, "known_hosts");
}
