namespace XTerminal.Core.Services;

public static class PortForwardBindRules
{
    public static bool HasDuplicateBind(
        IEnumerable<(Guid Id, string BindAddress, int ListenPort, bool IsRemote)> existing,
        Guid candidateId,
        string bindAddress,
        int listenPort,
        bool isRemote)
    {
        if (isRemote) return false;
        var bind = Normalize(bindAddress);
        foreach (var e in existing)
        {
            if (e.IsRemote) continue;
            if (e.Id == candidateId) continue;
            if (e.ListenPort != listenPort) continue;
            if (string.Equals(Normalize(e.BindAddress), bind, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string Normalize(string? bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress)) return "127.0.0.1";
        var b = bindAddress.Trim();
        return b is "localhost" or "::1" ? "127.0.0.1" : b;
    }
}
