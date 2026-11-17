using System.Text.Json;
using System.Text.Json.Serialization;
using XTerminal.Core.Models.Ssh;

namespace XTerminal.Core.Services;

public sealed class JsonSshSessionLibrary : ISshSessionLibrary
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;

    public JsonSshSessionLibrary(string? path = null)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XTerminal");
        Directory.CreateDirectory(root);
        _path = path ?? Path.Combine(root, "ssh-sessions.json");
    }

    public async Task<SshLibraryDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return CreateDefaults();
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var doc = await JsonSerializer.DeserializeAsync<SshLibraryDocument>(stream, Options, cancellationToken);
            return doc ?? CreateDefaults();
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public async Task SaveAsync(SshLibraryDocument document, CancellationToken cancellationToken = default)
    {
        // Defense-in-depth: never persist fields that look like secrets.
        foreach (var session in document.Sessions)
        {
            session.EnvironmentVariables ??= new Dictionary<string, string>();
            session.Tags ??= new List<string>();
            session.PortForwards ??= new List<SshPortForwardRecord>();
        }

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, document, Options, cancellationToken);
    }

    private static SshLibraryDocument CreateDefaults()
    {
        var prod = new SshFolderRecord { Name = "Production" };
        var linux = new SshFolderRecord { Name = "Linux", ParentId = prod.Id };
        var dev = new SshFolderRecord { Name = "Development" };

        return new SshLibraryDocument
        {
            Folders = [prod, linux, dev],
            Sessions =
            [
                new SshSessionRecord
                {
                    Name = "example-bastion",
                    Host = "bastion.example.com",
                    Port = 22,
                    Username = "ubuntu",
                    FolderId = linux.Id,
                    Tags = ["example", "bastion"],
                    IsFavorite = false
                }
            ]
        };
    }
}
