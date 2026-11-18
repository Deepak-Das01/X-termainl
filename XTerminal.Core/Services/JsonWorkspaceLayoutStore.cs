using System.Text.Json;
using System.Text.Json.Serialization;
using XTerminal.Core.Models.Ui;

namespace XTerminal.Core.Services;

public interface IWorkspaceLayoutStore
{
    Task<WorkspaceLayoutDocument> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(WorkspaceLayoutDocument document, CancellationToken cancellationToken = default);
    string StorePath { get; }
}

/// <summary>Atomic JSON workspace metadata store with last-good backup recovery.</summary>
public sealed class JsonWorkspaceLayoutStore : IWorkspaceLayoutStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly string _backupPath;

    public JsonWorkspaceLayoutStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppStoragePaths.Root, "workspace-layout.json");
        _backupPath = _path + ".bak";
    }

    public string StorePath => _path;

    public async Task<WorkspaceLayoutDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        var primary = await TryReadAsync(_path, cancellationToken);
        if (primary.IsValid && primary.Document is not null)
            return primary.Document;

        var backup = await TryReadAsync(_backupPath, cancellationToken);
        if (backup.IsValid && backup.Document is not null)
        {
            backup.Document.WorkspaceName = string.IsNullOrWhiteSpace(backup.Document.WorkspaceName)
                ? "Recovered workspace"
                : backup.Document.WorkspaceName;
            return backup.Document;
        }

        return new WorkspaceLayoutDocument();
    }

    public async Task SaveAsync(WorkspaceLayoutDocument document, CancellationToken cancellationToken = default)
    {
        var validated = WorkspaceLayoutValidator.ValidateAndNormalize(document);
        if (!validated.IsValid || validated.Document is null)
            throw new InvalidOperationException(validated.Error ?? "Invalid workspace document.");

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(validated.Document, Options);
        if (WorkspaceLayoutValidator.ContainsSecrets(json))
            throw new InvalidOperationException("Workspace serialization refused: secret-like content detected.");

        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, json, cancellationToken);

        if (File.Exists(_path))
        {
            try { File.Copy(_path, _backupPath, overwrite: true); }
            catch { /* best-effort backup */ }
        }

        // Atomic replace where supported.
        File.Move(temp, _path, overwrite: true);
    }

    private static async Task<WorkspaceValidationResult> TryReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return new WorkspaceValidationResult { IsValid = false, Error = "Missing", UsedFallback = true };

        try
        {
            await using var stream = File.OpenRead(path);
            var raw = await JsonSerializer.DeserializeAsync<WorkspaceLayoutDocument>(stream, Options, ct);
            return WorkspaceLayoutValidator.ValidateAndNormalize(raw);
        }
        catch (Exception ex)
        {
            return new WorkspaceValidationResult { IsValid = false, Error = ex.Message, UsedFallback = true };
        }
    }
}

public interface IWorkspaceProfileStore
{
    Task<WorkspaceProfileCatalog> LoadCatalogAsync(CancellationToken cancellationToken = default);
    Task SaveCatalogAsync(WorkspaceProfileCatalog catalog, CancellationToken cancellationToken = default);
    Task<WorkspaceLayoutDocument?> LoadProfileAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveProfileAsync(WorkspaceProfileEntry entry, WorkspaceLayoutDocument document, CancellationToken cancellationToken = default);
    Task DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default);
    string ProfilesDirectory { get; }
}

public sealed class JsonWorkspaceProfileStore : IWorkspaceProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _dir;
    private readonly string _catalogPath;
    private readonly IWorkspaceLayoutStore _active;

    public JsonWorkspaceProfileStore(string? directory = null, IWorkspaceLayoutStore? activeLayout = null)
    {
        _dir = directory ?? Path.Combine(AppStoragePaths.Root, "workspaces");
        _catalogPath = Path.Combine(_dir, "catalog.json");
        _active = activeLayout ?? new JsonWorkspaceLayoutStore();
        Directory.CreateDirectory(_dir);
    }

    public string ProfilesDirectory => _dir;

    public async Task<WorkspaceProfileCatalog> LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_catalogPath))
            return new WorkspaceProfileCatalog();
        try
        {
            await using var stream = File.OpenRead(_catalogPath);
            return await JsonSerializer.DeserializeAsync<WorkspaceProfileCatalog>(stream, Options, cancellationToken)
                   ?? new WorkspaceProfileCatalog();
        }
        catch
        {
            return new WorkspaceProfileCatalog();
        }
    }

    public async Task SaveCatalogAsync(WorkspaceProfileCatalog catalog, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_dir);
        var temp = _catalogPath + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, catalog, Options, cancellationToken);
        File.Move(temp, _catalogPath, overwrite: true);
    }

    public async Task<WorkspaceLayoutDocument?> LoadProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var catalog = await LoadCatalogAsync(cancellationToken);
        var entry = catalog.Profiles.FirstOrDefault(p => p.Id == id);
        if (entry is null || string.IsNullOrWhiteSpace(entry.FileName)) return null;
        var path = Path.Combine(_dir, entry.FileName);
        if (!File.Exists(path)) return null;
        var store = new JsonWorkspaceLayoutStore(path);
        return await store.LoadAsync(cancellationToken);
    }

    public async Task SaveProfileAsync(WorkspaceProfileEntry entry, WorkspaceLayoutDocument document, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.Name))
            throw new ArgumentException("Workspace name is required.");
        if (entry.Name.Length > 80)
            throw new ArgumentException("Workspace name is too long.");

        var catalog = await LoadCatalogAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(entry.FileName))
            entry.FileName = $"{entry.Id:N}.json";
        entry.ModifiedUtc = DateTimeOffset.UtcNow;

        var path = Path.Combine(_dir, entry.FileName);
        var store = new JsonWorkspaceLayoutStore(path);
        document.WorkspaceId = entry.Id;
        document.WorkspaceName = entry.Name;
        await store.SaveAsync(document, cancellationToken);

        var existing = catalog.Profiles.FirstOrDefault(p => p.Id == entry.Id);
        if (existing is null)
        {
            if (catalog.Profiles.Any(p => p.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A workspace with that name already exists.");
            catalog.Profiles.Add(entry);
        }
        else
        {
            if (catalog.Profiles.Any(p => p.Id != entry.Id &&
                                          p.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A workspace with that name already exists.");
            existing.Name = entry.Name;
            existing.FileName = entry.FileName;
            existing.ModifiedUtc = entry.ModifiedUtc;
        }

        await SaveCatalogAsync(catalog, cancellationToken);
    }

    public async Task DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var catalog = await LoadCatalogAsync(cancellationToken);
        var entry = catalog.Profiles.FirstOrDefault(p => p.Id == id);
        if (entry is null) return;
        catalog.Profiles.Remove(entry);
        if (catalog.DefaultWorkspaceId == id)
            catalog.DefaultWorkspaceId = null;
        await SaveCatalogAsync(catalog, cancellationToken);
        var path = Path.Combine(_dir, entry.FileName);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    /// <summary>Copy the active layout file into a named profile snapshot.</summary>
    public async Task CaptureActiveAsProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        var doc = await _active.LoadAsync(cancellationToken);
        var entry = new WorkspaceProfileEntry { Name = name.Trim() };
        await SaveProfileAsync(entry, doc, cancellationToken);
    }
}
