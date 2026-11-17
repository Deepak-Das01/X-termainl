using System.Text.RegularExpressions;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Ssh;

namespace XTerminal.Core.Services;

public sealed class OpenSshHostEntry
{
    public string Pattern { get; set; } = "";
    public string? HostName { get; set; }
    public string? User { get; set; }
    public int? Port { get; set; }
    public string? IdentityFile { get; set; }
    public string? ProxyJump { get; set; }
    public int? ServerAliveInterval { get; set; }
    public int? ConnectTimeout { get; set; }
    public List<string> UnsupportedDirectives { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class OpenSshImportPreview
{
    public string SourcePath { get; set; } = "";
    public List<OpenSshHostEntry> Entries { get; set; } = new();
    public List<string> GlobalUnsupported { get; set; } = new();
}

/// <summary>
/// Partial OpenSSH config parser. Does not execute ProxyCommand or shell expansion beyond ~.
/// </summary>
public static class OpenSshConfigParser
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "HostName", "User", "Port", "IdentityFile",
        "ProxyJump", "ServerAliveInterval", "ConnectTimeout"
    };

    private static readonly HashSet<string> ExplicitlyBlocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProxyCommand", "LocalCommand", "RemoteCommand", "PermitLocalCommand"
    };

    public static OpenSshImportPreview ParseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("OpenSSH config file not found.", path);

        var text = File.ReadAllText(path);
        var preview = Parse(text);
        preview.SourcePath = path;
        return preview;
    }

    public static OpenSshImportPreview Parse(string text)
    {
        var preview = new OpenSshImportPreview();
        OpenSshHostEntry? current = null;

        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.None))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var idx = line.IndexOfAny([' ', '\t', '=']);
            if (idx <= 0) continue;

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim().Trim('"', '\'');
            if (key.Equals("Match", StringComparison.OrdinalIgnoreCase))
            {
                preview.GlobalUnsupported.Add("Match (conditional blocks are not imported)");
                current = null;
                continue;
            }

            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                current = new OpenSshHostEntry { Pattern = value };
                preview.Entries.Add(current);
                continue;
            }

            if (current is null)
            {
                if (!Supported.Contains(key))
                    preview.GlobalUnsupported.Add(key);
                continue;
            }

            if (ExplicitlyBlocked.Contains(key))
            {
                current.UnsupportedDirectives.Add($"{key} (blocked — never executed)");
                continue;
            }

            if (!Supported.Contains(key))
            {
                current.UnsupportedDirectives.Add(key);
                continue;
            }

            switch (key.ToLowerInvariant())
            {
                case "hostname": current.HostName = value; break;
                case "user": current.User = value; break;
                case "port":
                    if (int.TryParse(value, out var port) && port is >= 1 and <= 65535)
                        current.Port = port;
                    else
                        current.Warnings.Add($"Invalid Port: {value}");
                    break;
                case "identityfile":
                    current.IdentityFile = ExpandPath(value);
                    break;
                case "proxyjump":
                    current.ProxyJump = value;
                    break;
                case "serveraliveinterval":
                    if (int.TryParse(value, out var keep))
                        current.ServerAliveInterval = keep;
                    break;
                case "connecttimeout":
                    if (int.TryParse(value, out var timeout))
                        current.ConnectTimeout = timeout;
                    break;
            }
        }

        // Drop wildcard-only patterns that cannot become concrete profiles.
        foreach (var e in preview.Entries)
        {
            if (e.Pattern.Contains('*') || e.Pattern.Contains('?'))
            {
                if (string.IsNullOrWhiteSpace(e.HostName))
                    e.Warnings.Add("Wildcard Host without HostName cannot be imported as a concrete profile.");
            }
        }

        return preview;
    }

    public static bool CanImport(OpenSshHostEntry e)
    {
        if (string.IsNullOrWhiteSpace(e.Pattern)) return false;
        if ((e.Pattern.Contains('*') || e.Pattern.Contains('?')) && string.IsNullOrWhiteSpace(e.HostName))
            return false;
        return true;
    }

    public static SshSessionRecord ToSession(OpenSshHostEntry e, string? displayName = null)
    {
        if (!CanImport(e))
            throw new InvalidOperationException($"Host '{e.Pattern}' cannot be imported.");

        var name = displayName ?? FirstToken(e.Pattern);
        var host = string.IsNullOrWhiteSpace(e.HostName) ? FirstToken(e.Pattern) : e.HostName!;
        var record = new SshSessionRecord
        {
            Name = name,
            Host = host,
            Port = e.Port is > 0 and <= 65535 ? e.Port.Value : 22,
            Username = e.User ?? "",
            AuthMethod = string.IsNullOrWhiteSpace(e.IdentityFile) ? SshAuthMethod.Password : SshAuthMethod.PrivateKey,
            PrivateKeyPath = e.IdentityFile,
            KeepAliveIntervalSeconds = e.ServerAliveInterval is > 0 ? e.ServerAliveInterval.Value : 30,
            ConnectionTimeoutSeconds = e.ConnectTimeout is > 0 ? e.ConnectTimeout.Value : 30,
            StrictHostKeyChecking = true
        };

        if (!string.IsNullOrWhiteSpace(e.ProxyJump))
            ApplyProxyJump(record, e.ProxyJump!);

        return record;
    }

    public static void ApplyProxyJump(SshSessionRecord record, string proxyJump)
    {
        // OpenSSH ProxyJump may be a comma-separated chain: first hop → ProxyJump* fields;
        // remaining hops → jump: tags consumed by JumpHostChain / multi-hop connect.
        var hops = proxyJump.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hops.Length == 0) return;

        ApplySingleHop(record, hops[0], primary: true);
        record.Tags ??= new List<string>();
        for (var i = 1; i < hops.Length; i++)
        {
            var tag = "jump:" + hops[i].Trim();
            if (!record.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                record.Tags.Add(tag);
        }
    }

    private static void ApplySingleHop(SshSessionRecord record, string hop, bool primary)
    {
        string hostPart = hop;
        string? user = null;
        var at = hop.LastIndexOf('@');
        if (at >= 0)
        {
            user = hop[..at];
            hostPart = hop[(at + 1)..];
        }

        var port = 22;
        var colon = hostPart.LastIndexOf(':');
        if (colon > 0 && int.TryParse(hostPart[(colon + 1)..], out var p) && p is >= 1 and <= 65535)
        {
            port = p;
            hostPart = hostPart[..colon];
        }

        if (!primary) return;
        record.ProxyJumpHost = hostPart;
        record.ProxyJumpPort = port;
        record.ProxyJumpUsername = user;
        record.ProxyJumpAuthMethod = SshAuthMethod.Password;
    }

    public static string ExpandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        path = path.Trim().Trim('"');
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[2..].Replace('/', Path.DirectorySeparatorChar));
        }

        if (path.StartsWith("%USERPROFILE%", StringComparison.OrdinalIgnoreCase))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path["%USERPROFILE%".Length..].TrimStart('\\', '/'));
        }

        // Reject command-like paths
        if (path.Contains('|') || path.Contains('&') || path.Contains('>'))
            throw new InvalidOperationException("IdentityFile path contains unsafe characters.");

        return Environment.ExpandEnvironmentVariables(path);
    }

    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

    private static string FirstToken(string pattern) =>
        Regex.Split(pattern.Trim(), @"\s+")[0];
}
