using System.Text.Json;
using System.Text.Json.Serialization;
using XTerminal.Core.Models.Ui;

namespace XTerminal.Core.Services;

public interface IWorkspaceLayoutStore
{
    Task<WorkspaceLayoutDocument> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(WorkspaceLayoutDocument document, CancellationToken cancellationToken = default);
}

public sealed class JsonWorkspaceLayoutStore : IWorkspaceLayoutStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;

    public JsonWorkspaceLayoutStore(string? path = null) =>
        _path = path ?? Path.Combine(AppStoragePaths.Root, "workspace-layout.json");

    public async Task<WorkspaceLayoutDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return new WorkspaceLayoutDocument();

        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<WorkspaceLayoutDocument>(stream, Options, cancellationToken)
                   ?? new WorkspaceLayoutDocument();
        }
        catch
        {
            return new WorkspaceLayoutDocument();
        }
    }

    public async Task SaveAsync(WorkspaceLayoutDocument document, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, document, Options, cancellationToken);
    }
}
