namespace XTerminal.Core.Models;

public enum SessionKind
{
    LocalShell,
    Ssh,
    Sftp,
    Telnet,
    Serial,
    Rdp,
    Vnc,
    X11
}

public enum ShellKind
{
    PowerShell,
    Cmd,
    Wsl,
    GitBash,
    Custom
}
