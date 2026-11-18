using System.IO;
using System.Text.Json;
using XTerminal.Core.Models;

namespace XTerminal.Core.Services;

public sealed class JsonSessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly object _sync = new();

    public JsonSessionStore(string? path = null)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XTerminal");
        Directory.CreateDirectory(root);
        _path = path ?? Path.Combine(root, "sessions.json");
    }

    public async Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            var defaults = CreateDefaults();
            await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
            return defaults;
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var items = await JsonSerializer.DeserializeAsync<List<SessionDefinition>>(stream, Options, cancellationToken)
                .ConfigureAwait(false);
            if (items is { Count: > 0 })
                return SanitizeLoaded(items);
        }
        catch (JsonException)
        {
            // Preserve damaged file; do not overwrite. Fall back to in-memory defaults.
            var backup = _path + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            try { File.Copy(_path, backup, overwrite: false); } catch { /* ignore */ }
        }

        return CreateDefaults();
    }

    public async Task SaveAsync(IEnumerable<SessionDefinition> sessions, CancellationToken cancellationToken = default)
    {
        var list = sessions.ToList();
        var temp = _path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, list, Options, cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            File.Copy(temp, _path, overwrite: true);
            try { File.Delete(temp); } catch { /* best effort */ }
        }
    }

    /// <summary>Local shell profiles only — remote protocols live in ssh-sessions.json / dedicated activities.</summary>
    private static List<SessionDefinition> CreateDefaults() =>
    [
        new() { Name = "PowerShell", Kind = SessionKind.LocalShell, Shell = ShellKind.PowerShell, Group = "Local" },
        new() { Name = "Command Prompt", Kind = SessionKind.LocalShell, Shell = ShellKind.Cmd, Group = "Local" },
        new() { Name = "WSL", Kind = SessionKind.LocalShell, Shell = ShellKind.Wsl, Group = "Local" }
    ];

    private static List<SessionDefinition> SanitizeLoaded(List<SessionDefinition> items)
    {
        // Strip misleading legacy "coming soon" remote stubs from older defaults without deleting real data.
        return items
            .Where(s =>
                s.Kind == SessionKind.LocalShell ||
                s.Kind == SessionKind.Serial ||
                s.Kind == SessionKind.Telnet ||
                s.Kind == SessionKind.Rdp ||
                s.Kind == SessionKind.Vnc ||
                (s.Kind == SessionKind.Ssh && !string.IsNullOrWhiteSpace(s.Host) &&
                 !s.Name.Contains("coming soon", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
