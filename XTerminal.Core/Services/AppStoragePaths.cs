namespace XTerminal.Core.Services;

/// <summary>Canonical %AppData%\XTerminal paths for separated persistence files.</summary>
public static class AppStoragePaths
{
    public static string Root
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XTerminal");
            Directory.CreateDirectory(root);
            return root;
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
}
