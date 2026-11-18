using System.Text.Json;

namespace XTerminal.Core.Services;

public sealed class JsonVncAcknowledgmentStore : IVncAcknowledgmentStore
{
    private readonly string _path;
    private readonly object _sync = new();

    public JsonVncAcknowledgmentStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppStoragePaths.Root, "vnc-ack.json");
    }

    public bool IsAcknowledged(string host, int port)
    {
        var key = VncSecurityPolicy.HostKey(host, port);
        lock (_sync) return Load().Contains(key);
    }

    public void Acknowledge(string host, int port)
    {
        var key = VncSecurityPolicy.HostKey(host, port);
        lock (_sync)
        {
            var set = Load();
            if (set.Add(key)) Save(set);
        }
    }

    private HashSet<string> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new HashSet<string>(StringComparer.Ordinal);
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? new List<string>();
            return new HashSet<string>(list, StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private void Save(HashSet<string> set)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(set.OrderBy(x => x).ToList()));
        File.Copy(temp, _path, overwrite: true);
        try { File.Delete(temp); } catch { /* ignore */ }
    }
}
