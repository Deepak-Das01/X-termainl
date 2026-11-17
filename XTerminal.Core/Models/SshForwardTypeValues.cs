namespace XTerminal.Core.Models;

public static class SshForwardTypeValues
{
    public static Array All { get; } = Enum.GetValues(typeof(SshForwardType));
}
