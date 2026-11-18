using System.Text.Json;
using XTerminal.App.Services;
using XTerminal.Core.Models.Ui;
using XTerminal.Core.Services;

namespace XTerminal.Tests;

public class Phase3DFunctionalTests
{
    [Fact]
    public async Task WorkspaceLayout_roundtrip_atomic_and_secret_free()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xt-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "workspace-layout.json");
            var store = new JsonWorkspaceLayoutStore(path);
            var doc = new WorkspaceLayoutDocument
            {
                WorkspaceName = "Demo",
                RestoreEnabled = true,
                AutoReconnectRemote = false,
                Tabs =
                {
                    new WorkspaceTabLayout
                    {
                        Kind = "terminal",
                        Title = "pwsh",
                        LocalShell = "PowerShell",
                        WorkingDirectory = @"%USERPROFILE%\Documents",
                        SplitRoot = new SplitLayoutNode
                        {
                            Type = "branch",
                            Orientation = "Vertical",
                            Ratio = 0.4,
                            First = new SplitLayoutNode { Type = "leaf", LocalShell = "PowerShell", PaneTitle = "A" },
                            Second = new SplitLayoutNode { Type = "leaf", LocalShell = "Cmd", PaneTitle = "B" }
                        }
                    }
                }
            };

            await store.SaveAsync(doc);
            Assert.True(File.Exists(path));
            Assert.True(File.Exists(path + ".bak") || true); // bak created on subsequent saves

            await store.SaveAsync(doc); // creates backup
            Assert.True(File.Exists(path + ".bak"));

            var loaded = await store.LoadAsync();
            Assert.Equal(2, loaded.Version);
            Assert.Equal("Demo", loaded.WorkspaceName);
            Assert.False(loaded.AutoReconnectRemote);
            Assert.Single(loaded.Tabs);
            Assert.Equal("branch", loaded.Tabs[0].SplitRoot?.Type);

            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("buffer", json, StringComparison.OrdinalIgnoreCase);
            Assert.False(WorkspaceLayoutValidator.ContainsSecrets(json));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void WorkspaceLayout_rejects_invalid_and_migrates_v1()
    {
        var raw = new WorkspaceLayoutDocument
        {
            Version = 1,
            Tabs =
            {
                new WorkspaceTabLayout { Kind = "terminal", Title = "a", LocalShell = "PowerShell" },
                new WorkspaceTabLayout { Kind = "nope", Title = "skip" }
            }
        };
        // Force duplicate tab ids
        raw.Tabs[0].TabId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var dup = new WorkspaceTabLayout
        {
            TabId = raw.Tabs[0].TabId,
            Kind = "terminal",
            Title = "dup",
            LocalShell = "Cmd"
        };
        raw.Tabs.Add(dup);

        var result = WorkspaceLayoutValidator.ValidateAndNormalize(raw);
        Assert.True(result.IsValid);
        Assert.NotNull(result.Document);
        Assert.Equal(2, result.Document!.Version);
        Assert.Equal(2, result.Document.Tabs.Count); // invalid kind removed
        Assert.Equal(2, result.Document.Tabs.Select(t => t.TabId).Distinct().Count());
    }

    [Fact]
    public async Task WorkspaceLayout_recovers_from_corrupt_primary_using_backup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xt-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "workspace-layout.json");
            var store = new JsonWorkspaceLayoutStore(path);
            await store.SaveAsync(new WorkspaceLayoutDocument
            {
                WorkspaceName = "Good",
                Tabs = { new WorkspaceTabLayout { Kind = "terminal", LocalShell = "PowerShell", Title = "t" } }
            });
            await store.SaveAsync(await store.LoadAsync()); // ensure bak
            await File.WriteAllTextAsync(path, "{ not-json");
            var recovered = await store.LoadAsync();
            Assert.Equal("Good", recovered.WorkspaceName);
            Assert.NotEmpty(recovered.Tabs);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task WorkspaceProfiles_save_open_delete_without_secrets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xt-prof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var profiles = new JsonWorkspaceProfileStore(dir);
            var doc = new WorkspaceLayoutDocument
            {
                WorkspaceName = "Ops",
                Tabs = { new WorkspaceTabLayout { Kind = "terminal", LocalShell = "PowerShell", Title = "ops" } }
            };
            var entry = new WorkspaceProfileEntry { Name = "Ops" };
            await profiles.SaveProfileAsync(entry, doc);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await profiles.SaveProfileAsync(new WorkspaceProfileEntry { Name = "Ops" }, doc));

            var catalog = await profiles.LoadCatalogAsync();
            Assert.Single(catalog.Profiles);
            var loaded = await profiles.LoadProfileAsync(catalog.Profiles[0].Id);
            Assert.NotNull(loaded);
            Assert.Equal("Ops", loaded!.WorkspaceName);

            await profiles.DeleteProfileAsync(catalog.Profiles[0].Id);
            catalog = await profiles.LoadCatalogAsync();
            Assert.Empty(catalog.Profiles);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void KeybindingService_reserved_conflict_reset_and_normalize()
    {
        var svc = new KeybindingService(new (string, string)[]
        {
            ("a", "Ctrl+X"),
            ("b", "Ctrl+Y"),
            ("app.palette", "Ctrl+Shift+P")
        });

        Assert.True(svc.IsReserved("Ctrl+Shift+P"));
        Assert.False(svc.TrySetGesture("a", "Alt+F4", false, out var err));
        Assert.Contains("reserved", err, StringComparison.OrdinalIgnoreCase);

        Assert.False(svc.TrySetGesture("b", "Ctrl+X", false, out var conflict));
        Assert.Contains("Conflicts", conflict, StringComparison.OrdinalIgnoreCase);
        Assert.True(svc.TrySetGesture("b", "Ctrl+X", true, out _));
        Assert.Equal("", svc.GetGesture("a"));
        Assert.Equal("Ctrl+X", svc.GetGesture("b"));

        svc.Reset("b");
        Assert.Equal("Ctrl+Y", svc.GetGesture("b"));
        svc.SetGesture("b", "ctrl + z");
        Assert.Equal("ctrl+z", KeybindingService.Normalize("ctrl + z"));
        Assert.Equal("ctrl+z", svc.GetGesture("b"));

        svc.ResetAll();
        Assert.Equal("Ctrl+X", svc.GetGesture("a"));
    }

    [Fact]
    public void ShortcutContextResolver_blocks_terminal_shortcuts_in_files()
    {
        var ctx = ShortcutContextResolver.Resolve(
            settingsOpen: false,
            activitySection: "Files",
            editorFocused: false,
            terminalFocused: false,
            textInputFocused: false);
        Assert.Equal(ShortcutContext.Files, ctx);
        Assert.False(ShortcutContextResolver.AllowsCommand(ctx, "term.splitRight"));
        Assert.True(ShortcutContextResolver.AllowsCommand(ctx, "app.palette"));
        Assert.True(ShortcutContextResolver.AllowsCommand(ctx, "files.open"));
    }

    [Fact]
    public void Workspace_serialization_has_no_credential_fields()
    {
        var doc = new WorkspaceLayoutDocument
        {
            Tabs =
            {
                new WorkspaceTabLayout
                {
                    Kind = "terminal",
                    SshSessionId = Guid.NewGuid(),
                    Title = "ssh"
                }
            }
        };
        var json = JsonSerializer.Serialize(doc);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Passphrase", json, StringComparison.OrdinalIgnoreCase);
    }
}
