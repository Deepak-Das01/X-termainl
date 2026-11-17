using XTerminal.Core.Models.Ssh;

namespace XTerminal.Core.Services;

public sealed class JumpHop
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string? Username { get; set; }
    public Guid? ProfileId { get; set; }
}

public static class JumpHostChain
{
    public const int MaxHops = 5;

    public static List<JumpHop> FromSession(SshSessionRecord session)
    {
        var hops = new List<JumpHop>();
        if (!string.IsNullOrWhiteSpace(session.ProxyJumpHost))
        {
            hops.Add(new JumpHop
            {
                Host = session.ProxyJumpHost!.Trim(),
                Port = session.ProxyJumpPort <= 0 ? 22 : session.ProxyJumpPort,
                Username = session.ProxyJumpUsername
            });
        }

        // Optional multi-hop encoded in Tags: "jump:user@host:port"
        foreach (var tag in session.Tags.Where(t => t.StartsWith("jump:", StringComparison.OrdinalIgnoreCase)))
        {
            var spec = tag["jump:".Length..];
            if (TryParseHop(spec, out var hop))
                hops.Add(hop);
        }

        return hops;
    }

    public static List<string> Validate(IReadOnlyList<JumpHop> hops, string targetHost, int targetPort)
    {
        var errors = new List<string>();
        if (hops.Count > MaxHops)
            errors.Add($"Jump chain exceeds maximum of {MaxHops} hops.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < hops.Count; i++)
        {
            var h = hops[i];
            if (string.IsNullOrWhiteSpace(h.Host))
            {
                errors.Add($"Hop {i + 1} is missing a host.");
                continue;
            }

            if (h.Port is < 1 or > 65535)
                errors.Add($"Hop {i + 1} has an invalid port.");

            var key = $"{h.Host}:{h.Port}";
            if (!seen.Add(key))
                errors.Add($"Jump chain loop detected at {key}.");

            if (h.Host.Equals(targetHost, StringComparison.OrdinalIgnoreCase) && h.Port == targetPort)
                errors.Add($"Hop {i + 1} duplicates the target {targetHost}:{targetPort}.");
        }

        return errors;
    }

    public static bool TryParseHop(string spec, out JumpHop hop)
    {
        hop = new JumpHop();
        if (string.IsNullOrWhiteSpace(spec)) return false;

        var s = spec.Trim();
        string? user = null;
        var at = s.LastIndexOf('@');
        if (at >= 0)
        {
            user = s[..at];
            s = s[(at + 1)..];
        }

        var port = 22;
        var colon = s.LastIndexOf(':');
        if (colon > 0 && int.TryParse(s[(colon + 1)..], out var p) && p is >= 1 and <= 65535)
        {
            port = p;
            s = s[..colon];
        }

        if (string.IsNullOrWhiteSpace(s)) return false;
        hop = new JumpHop { Host = s, Port = port, Username = user };
        return true;
    }
}
