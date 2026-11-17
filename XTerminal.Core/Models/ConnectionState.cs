namespace XTerminal.Core.Models;

public enum ConnectionState
{
    Idle,
    Connecting,
    Connected,
    Reconnecting,
    Failed,
    Disconnected,
    Cancelled
}

public enum SshAuthMethod
{
    Password = 0,
    PrivateKey = 1,
    Agent = 2,
    PasswordAndPrivateKey = 3,
    /// <summary>Responds to keyboard-interactive prompts using stored password when appropriate.</summary>
    KeyboardInteractive = 4
}

public enum SshForwardType
{
    Local = 0,
    Remote = 1,
    Dynamic = 2
}
