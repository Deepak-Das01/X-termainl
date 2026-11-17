namespace XTerminal.Core.Models;

public sealed class SessionDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New session";
    public SessionKind Kind { get; set; } = SessionKind.LocalShell;
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? Username { get; set; }
    public string? Group { get; set; } = "Sessions";
    public ShellKind Shell { get; set; } = ShellKind.PowerShell;
    public string? CustomCommand { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? SerialPort { get; set; }
    public int? BaudRate { get; set; }
    public Dictionary<string, string> Extra { get; set; } = new();

    /// <summary>Links to <see cref="Ssh.SshSessionRecord"/> metadata (secrets stay in Credential Manager).</summary>
    public Guid? SshSessionId { get; set; }
}
