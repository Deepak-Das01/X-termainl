using System.Text.Json;
using System.Text.Json.Serialization;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Ssh;

namespace XTerminal.Core.Services;

public sealed class SessionExportDocument
{
    public int Version { get; set; } = 1;
    public DateTimeOffset ExportedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Application { get; set; } = "X-Terminal";
    public List<SshFolderRecord> Folders { get; set; } = new();
    public List<SshSessionExportRecord> Sessions { get; set; } = new();
}

/// <summary>Export shape — never includes secrets or private key material.</summary>
public sealed class SshSessionExportRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public string AuthMethod { get; set; } = "Password";
    public string? PrivateKeyPath { get; set; }
    public Guid? FolderId { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool IsFavorite { get; set; }
    public string? StartupDirectory { get; set; }
    public int ConnectionTimeoutSeconds { get; set; } = 20;
    public int KeepAliveIntervalSeconds { get; set; } = 30;
    public bool StrictHostKeyChecking { get; set; } = true;
    public string? ProxyJumpHost { get; set; }
    public int? ProxyJumpPort { get; set; }
    public string? ProxyJumpUsername { get; set; }
}

public enum ImportConflictPolicy
{
    Skip,
    Rename,
    Replace
}

public static class SessionImportExport
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static SessionExportDocument CreateExport(
        IEnumerable<SshSessionRecord> sessions,
        IEnumerable<SshFolderRecord> folders)
    {
        var list = sessions.ToList();
        var folderIds = new HashSet<Guid>(list.Where(s => s.FolderId.HasValue).Select(s => s.FolderId!.Value));
        return new SessionExportDocument
        {
            Folders = folders.Where(f => folderIds.Contains(f.Id)).Select(CloneFolder).ToList(),
            Sessions = list.Select(ToExport).ToList()
        };
    }

    public static string Serialize(SessionExportDocument doc) =>
        JsonSerializer.Serialize(doc, Options);

    public static SessionExportDocument Deserialize(string json)
    {
        if (ContainsSecretField(json))
            throw new InvalidOperationException("Export/import must not contain secret fields.");
        var doc = JsonSerializer.Deserialize<SessionExportDocument>(json, Options)
                  ?? throw new InvalidOperationException("Invalid session export document.");
        if (doc.Version is < 1 or > 1)
            throw new InvalidOperationException($"Unsupported export version: {doc.Version}");
        Validate(doc);
        return doc;
    }

    public static void Validate(SessionExportDocument doc)
    {
        foreach (var s in doc.Sessions)
        {
            if (string.IsNullOrWhiteSpace(s.Name))
                throw new InvalidOperationException("Session name is required.");
            if (string.IsNullOrWhiteSpace(s.Host))
                throw new InvalidOperationException($"Session '{s.Name}' is missing a host.");
            if (s.Port is < 1 or > 65535)
                throw new InvalidOperationException($"Session '{s.Name}' has an invalid port.");
            if (ContainsSecretField(JsonSerializer.Serialize(s)))
                throw new InvalidOperationException("Export/import must not contain secret fields.");
        }
    }

    public static bool ContainsSecretField(string json) =>
        // Match JSON property names only — "authMethod":"password" must not false-positive.
        System.Text.RegularExpressions.Regex.IsMatch(json, "\"password\"\\s*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(json, "\"passphrase\"\\s*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(json, "\"privateKeyContents\"\\s*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(json, "\"credentialBlob\"\\s*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static SshSessionRecord ToRecord(SshSessionExportRecord e) => new()
    {
        Id = e.Id == Guid.Empty ? Guid.NewGuid() : e.Id,
        Name = e.Name.Trim(),
        Host = e.Host.Trim(),
        Port = e.Port,
        Username = e.Username?.Trim() ?? "",
        AuthMethod = Enum.TryParse<SshAuthMethod>(e.AuthMethod, true, out var m) ? m : SshAuthMethod.Password,
        PrivateKeyPath = e.PrivateKeyPath,
        FolderId = e.FolderId,
        Tags = e.Tags?.ToList() ?? new List<string>(),
        IsFavorite = e.IsFavorite,
        StartupDirectory = e.StartupDirectory,
        ConnectionTimeoutSeconds = e.ConnectionTimeoutSeconds,
        KeepAliveIntervalSeconds = e.KeepAliveIntervalSeconds,
        StrictHostKeyChecking = e.StrictHostKeyChecking,
        ProxyJumpHost = e.ProxyJumpHost,
        ProxyJumpPort = e.ProxyJumpPort is null or <= 0 ? 22 : e.ProxyJumpPort.Value,
        ProxyJumpUsername = e.ProxyJumpUsername,
        ModifiedUtc = DateTimeOffset.UtcNow,
        CreatedUtc = DateTimeOffset.UtcNow
    };

    public static (List<SshSessionRecord> Sessions, List<SshFolderRecord> Folders) Merge(
        SessionExportDocument doc,
        IReadOnlyList<SshSessionRecord> existingSessions,
        IReadOnlyList<SshFolderRecord> existingFolders,
        ImportConflictPolicy policy)
    {
        var sessions = existingSessions.Select(CloneSession).ToList();
        var folders = existingFolders.Select(CloneFolder).ToList();
        var folderMap = new Dictionary<Guid, Guid>();

        foreach (var f in doc.Folders)
        {
            var existing = folders.FirstOrDefault(x => x.Id == f.Id ||
                x.Name.Equals(f.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                var nf = CloneFolder(f);
                if (nf.Id == Guid.Empty) nf.Id = Guid.NewGuid();
                folders.Add(nf);
                folderMap[f.Id] = nf.Id;
            }
            else
            {
                folderMap[f.Id] = existing.Id;
            }
        }

        foreach (var exp in doc.Sessions)
        {
            var record = ToRecord(exp);
            if (record.FolderId is Guid fid && folderMap.TryGetValue(fid, out var mapped))
                record.FolderId = mapped;

            var conflict = sessions.FirstOrDefault(s => s.Id == record.Id ||
                (s.Name.Equals(record.Name, StringComparison.OrdinalIgnoreCase) &&
                 s.Host.Equals(record.Host, StringComparison.OrdinalIgnoreCase) &&
                 s.Username.Equals(record.Username, StringComparison.OrdinalIgnoreCase)));

            if (conflict is null)
            {
                sessions.Add(record);
                continue;
            }

            switch (policy)
            {
                case ImportConflictPolicy.Skip:
                    break;
                case ImportConflictPolicy.Rename:
                    record.Id = Guid.NewGuid();
                    record.Name = UniqueName(record.Name, sessions.Select(s => s.Name));
                    sessions.Add(record);
                    break;
                case ImportConflictPolicy.Replace:
                    sessions.Remove(conflict);
                    record.Id = conflict.Id;
                    sessions.Add(record);
                    break;
            }
        }

        return (sessions, folders);
    }

    private static string UniqueName(string baseName, IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(baseName)) return baseName;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!set.Contains(candidate)) return candidate;
        }

        return $"{baseName} ({Guid.NewGuid():N})" ;
    }

    private static SshSessionExportRecord ToExport(SshSessionRecord s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Host = s.Host,
        Port = s.Port,
        Username = s.Username,
        AuthMethod = s.AuthMethod.ToString(),
        PrivateKeyPath = s.PrivateKeyPath,
        FolderId = s.FolderId,
        Tags = s.Tags?.ToList() ?? new List<string>(),
        IsFavorite = s.IsFavorite,
        StartupDirectory = s.StartupDirectory,
        ConnectionTimeoutSeconds = s.ConnectionTimeoutSeconds,
        KeepAliveIntervalSeconds = s.KeepAliveIntervalSeconds,
        StrictHostKeyChecking = s.StrictHostKeyChecking,
        ProxyJumpHost = s.ProxyJumpHost,
        ProxyJumpPort = s.ProxyJumpPort,
        ProxyJumpUsername = s.ProxyJumpUsername
    };

    private static SshFolderRecord CloneFolder(SshFolderRecord f) => new()
    {
        Id = f.Id,
        Name = f.Name,
        ParentId = f.ParentId
    };

    private static SshSessionRecord CloneSession(SshSessionRecord s) =>
        JsonSerializer.Deserialize<SshSessionRecord>(JsonSerializer.Serialize(s, Options), Options)!;
}
