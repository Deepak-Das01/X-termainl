using XTerminal.Core.Models;

namespace XTerminal.Terminal.ConPty;

public static class ShellResolver
{
    public static string ResolveCommand(SessionDefinition session)
    {
        return session.Shell switch
        {
            ShellKind.Cmd => ResolveCmd(),
            ShellKind.Wsl => "wsl.exe",
            ShellKind.GitBash => ResolveGitBash(),
            ShellKind.Custom => string.IsNullOrWhiteSpace(session.CustomCommand)
                ? ResolvePowerShell()
                : session.CustomCommand!,
            _ => ResolvePowerShell()
        };
    }

    private static string ResolvePowerShell()
    {
        var pwsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PowerShell", "7", "pwsh.exe");
        if (File.Exists(pwsh))
        {
            return $"\"{pwsh}\"";
        }

        return "powershell.exe";
    }

    private static string ResolveCmd() =>
        Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static string ResolveGitBash()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "bin", "bash.exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return $"\"{path}\" --login -i";
            }
        }

        return ResolvePowerShell();
    }
}
