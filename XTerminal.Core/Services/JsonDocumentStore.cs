using System.Text.Json;
using System.Text.Json.Serialization;

namespace XTerminal.Core.Services;

public abstract class JsonDocumentStore<T> where T : class, new()
{
    protected static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;
    private readonly Func<T> _createDefault;
    private readonly Action<T>? _normalize;

    protected JsonDocumentStore(string path, Func<T>? createDefault = null, Action<T>? normalize = null)
    {
        _path = path;
        _createDefault = createDefault ?? (() => new T());
        _normalize = normalize;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public async Task<T> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return _createDefault();
        try
        {
            await using var stream = File.OpenRead(_path);
            var doc = await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
            var result = doc ?? _createDefault();
            _normalize?.Invoke(result);
            return result;
        }
        catch
        {
            return _createDefault();
        }
    }

    public async Task SaveAsync(T document, CancellationToken cancellationToken = default)
    {
        _normalize?.Invoke(document);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, document, Options, cancellationToken);
    }
}
