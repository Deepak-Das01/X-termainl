using XTerminal.Core.Models;
using XTerminal.Core.Security;
using XTerminal.Core.Services;
using XTerminal.Protocols.Local;
using XTerminal.Protocols.Serial;
using XTerminal.Protocols.Ssh;
using XTerminal.Protocols.Stubs;
using XTerminal.Protocols.Telnet;

namespace XTerminal.Protocols;

public sealed class SessionHostFactory
{
    private readonly ISecretStore _secrets;
    private readonly ISshSessionLibrary _sshLibrary;
    private readonly SshHostKeyPolicy _hostKeys;
    private readonly SshConnectionManager _connectionManager;

    public SshConnectionManager ConnectionManager => _connectionManager;
    public SshHostKeyPolicy HostKeys => _hostKeys;

    public SessionHostFactory(
        ISecretStore secrets,
        ISshSessionLibrary sshLibrary,
        SshHostKeyPolicy hostKeys,
        SshConnectionManager? connectionManager = null)
    {
        _secrets = secrets;
        _sshLibrary = sshLibrary;
        _hostKeys = hostKeys;
        _connectionManager = connectionManager ?? new SshConnectionManager();
    }

    public static bool IsProtocolSupported(SessionKind kind) =>
        kind is SessionKind.LocalShell or SessionKind.Ssh or SessionKind.Serial or SessionKind.Telnet;

    /// <summary>
    /// SFTP is supported as a workspace activity, not as a terminal session tab.
    /// </summary>
    public static bool IsTerminalSessionSupported(SessionKind kind) =>
        kind is SessionKind.LocalShell or SessionKind.Ssh or SessionKind.Serial or SessionKind.Telnet;

    public ISessionHost Create(SessionKind kind) => kind switch
    {
        SessionKind.LocalShell => new LocalShellSessionHost(),
        SessionKind.Ssh => new SshSessionHost(_secrets, _sshLibrary, _hostKeys, _connectionManager),
        SessionKind.Serial => new SerialSessionHost(),
        SessionKind.Telnet => new TelnetSessionHost(),
        SessionKind.Sftp => new StubSessionHost(SessionKind.Sftp, "SFTP",
            "SFTP is available from the SFTP activity. Terminal SFTP tabs are not used."),
        SessionKind.Rdp => new StubSessionHost(SessionKind.Rdp, "RDP",
            "RDP is not supported in this release."),
        SessionKind.Vnc => new StubSessionHost(SessionKind.Vnc, "VNC",
            "VNC is not supported in this release."),
        SessionKind.X11 => new StubSessionHost(SessionKind.X11, "X11",
            "X11 forwarding UI is not supported in this release."),
        _ => new StubSessionHost(kind, kind.ToString(), "Not supported in this release.")
    };
}
