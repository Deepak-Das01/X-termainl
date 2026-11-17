using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Services;

namespace XTerminal.Tests;

public class SessionImportExportTests
{
    [Fact]
    public void Export_serialize_rejects_secret_fields()
    {
        var doc = SessionImportExport.CreateExport(
            [new SshSessionRecord { Name = "demo", Host = "h", Username = "u" }],
            []);
        var json = SessionImportExport.Serialize(doc);
        Assert.False(SessionImportExport.ContainsSecretField(json));

        var poisoned = json.Replace("\"host\"", "\"password\"", StringComparison.OrdinalIgnoreCase);
        Assert.True(SessionImportExport.ContainsSecretField(poisoned));
        Assert.Throws<InvalidOperationException>(() => SessionImportExport.Deserialize(
            """{"version":1,"sessions":[{"name":"x","host":"h","port":22,"password":"secret"}]}"""));
    }

    [Fact]
    public void Merge_rename_avoids_name_collision()
    {
        var existing = new List<SshSessionRecord>
        {
            new() { Id = Guid.NewGuid(), Name = "prod", Host = "a.example", Username = "root" }
        };
        var incoming = new SessionExportDocument
        {
            Sessions =
            [
                new SshSessionExportRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "prod",
                    Host = "a.example",
                    Username = "root",
                    Port = 22
                }
            ]
        };

        var (merged, _) = SessionImportExport.Merge(incoming, existing, [], ImportConflictPolicy.Rename);
        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, s => s.Name.StartsWith("prod", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(merged, s => s.Name.Contains('(', StringComparison.Ordinal));
    }

    [Fact]
    public void Merge_skip_keeps_single()
    {
        var id = Guid.NewGuid();
        var existing = new List<SshSessionRecord>
        {
            new() { Id = id, Name = "prod", Host = "a.example", Username = "root" }
        };
        var incoming = new SessionExportDocument
        {
            Sessions =
            [
                new SshSessionExportRecord
                {
                    Id = id,
                    Name = "prod",
                    Host = "a.example",
                    Username = "root",
                    Port = 22
                }
            ]
        };

        var (merged, _) = SessionImportExport.Merge(incoming, existing, [], ImportConflictPolicy.Skip);
        Assert.Single(merged);
    }
}
