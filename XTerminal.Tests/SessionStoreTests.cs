using System.Text.Json;
using XTerminal.Core.Models;
using XTerminal.Core.Services;

namespace XTerminal.Tests;

public class SessionStoreTests
{
    [Fact]
    public async Task Round_trip_local_shells_without_passwords()
    {
        var path = Path.Combine(Path.GetTempPath(), "xterminal-tests", Guid.NewGuid().ToString("N"), "sessions.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var store = new JsonSessionStore(path);
        var sessions = new List<SessionDefinition>
        {
            new() { Name = "PowerShell", Kind = SessionKind.LocalShell, Shell = ShellKind.PowerShell, Group = "Local" },
            new() { Name = "CMD", Kind = SessionKind.LocalShell, Shell = ShellKind.Cmd, Group = "Local" }
        };
        await store.SaveAsync(sessions);
        var loaded = await store.LoadAsync();
        Assert.Equal(2, loaded.Count);
        Assert.All(loaded, s => Assert.Equal(SessionKind.LocalShell, s.Kind));

        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passphrase", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Defaults_do_not_include_coming_soon_remotes()
    {
        var path = Path.Combine(Path.GetTempPath(), "xterminal-tests", Guid.NewGuid().ToString("N"), "sessions.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var store = new JsonSessionStore(path);
        var loaded = await store.LoadAsync();
        Assert.All(loaded, s => Assert.Equal(SessionKind.LocalShell, s.Kind));
        Assert.DoesNotContain(loaded, s => s.Name.Contains("coming soon", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SessionDefinition_json_has_no_secret_fields()
    {
        var def = new SessionDefinition
        {
            Name = "x",
            Kind = SessionKind.Ssh,
            Host = "h",
            Username = "u",
            Port = 22
        };
        var json = JsonSerializer.Serialize(def);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Passphrase", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateKey", json, StringComparison.OrdinalIgnoreCase);
    }
}
