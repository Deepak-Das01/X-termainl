using XTerminal.App.Services;
using XTerminal.App.ViewModels;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Security;
using XTerminal.Core.Services;
using XTerminal.Core.Settings;
using XTerminal.Protocols;
using XTerminal.Protocols.Ssh;

namespace XTerminal.Tests;

public class Phase2FunctionalTests
{
    [Fact]
    public async Task Settings_roundtrip_preserves_new_sections()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xt-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new JsonSettingsStore(Path.Combine(dir, "settings.json"));
            var settings = new AppSettings
            {
                General = new GeneralSettings { OpenDefaultShellOnStartup = false, ConfirmExitWithActiveSessions = false },
                Ssh = new SshAppSettings { DefaultPort = 2222, ConnectionTimeoutSeconds = 45 },
                Files = new FilesAppSettings { ShowHiddenFiles = true },
                Security = new SecurityAppSettings { WarnBroadcastToMultipleHosts = false },
                Advanced = new AdvancedAppSettings { EnableFileLogging = false, MaxLogFiles = 3 }
            };
            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();
            Assert.False(loaded.General.OpenDefaultShellOnStartup);
            Assert.Equal(2222, loaded.Ssh.DefaultPort);
            Assert.True(loaded.Files.ShowHiddenFiles);
            Assert.False(loaded.Security.WarnBroadcastToMultipleHosts);
            Assert.False(loaded.Advanced.EnableFileLogging);
            Assert.Equal(3, loaded.Advanced.MaxLogFiles);
            Assert.NotEmpty(loaded.Profiles);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void KeybindingService_detects_conflicts()
    {
        var kb = new KeybindingService(new (string, string)[]
        {
            ("a", "Ctrl+K"),
            ("b", "Ctrl+L")
        });
        kb.SetGesture("b", "Ctrl+K");
        var conflicts = kb.DetectConflicts();
        Assert.Contains(conflicts, c => c.Gesture.Equals("Ctrl+K", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SessionImportExport_rejects_secret_fields_and_renames_duplicates()
    {
        Assert.True(SessionImportExport.ContainsSecretField("{\"password\":\"x\"}"));

        var existing = new List<SshSessionRecord>
        {
            new() { Id = Guid.NewGuid(), Name = "prod", Host = "h", Port = 22, Username = "u" }
        };
        var doc = new SessionExportDocument
        {
            Sessions =
            [
                new SshSessionExportRecord
                {
                    Id = existing[0].Id,
                    Name = "prod",
                    Host = "h",
                    Port = 22,
                    Username = "u"
                }
            ]
        };

        var (merged, _) = SessionImportExport.Merge(doc, existing, Array.Empty<SshFolderRecord>(), ImportConflictPolicy.Rename);
        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, s => s.Name.Contains("prod", StringComparison.OrdinalIgnoreCase) && s.Id != existing[0].Id);
    }

    [Fact]
    public void SessionImportExport_skip_keeps_single_session()
    {
        var id = Guid.NewGuid();
        var existing = new List<SshSessionRecord>
        {
            new() { Id = id, Name = "prod", Host = "h", Port = 22, Username = "u" }
        };
        var doc = new SessionExportDocument
        {
            Sessions =
            [
                new SshSessionExportRecord { Id = id, Name = "prod", Host = "h", Port = 22, Username = "u" }
            ]
        };
        var (merged, _) = SessionImportExport.Merge(doc, existing, Array.Empty<SshFolderRecord>(), ImportConflictPolicy.Skip);
        Assert.Single(merged);
    }

    [Fact]
    public void Broadcast_targets_exclude_private_and_disconnected()
    {
        var panes = new List<(ConnectionState State, bool Private, bool Target)>
        {
            (ConnectionState.Connected, false, true),
            (ConnectionState.Connected, true, true),
            (ConnectionState.Disconnected, false, true),
            (ConnectionState.Connected, false, false)
        };

        static IEnumerable<int> Select(BroadcastMode mode, IReadOnlyList<(ConnectionState State, bool Private, bool Target)> panes)
        {
            if (mode == BroadcastMode.Off) yield break;
            for (var i = 0; i < panes.Count; i++)
            {
                var p = panes[i];
                if (p.State != ConnectionState.Connected) continue;
                if (p.Private) continue;
                if (mode == BroadcastMode.AllPanes || p.Target)
                    yield return i;
            }
        }

        Assert.Equal(new[] { 0, 3 }, Select(BroadcastMode.AllPanes, panes).ToArray());
        Assert.Equal(new[] { 0 }, Select(BroadcastMode.SelectedPanes, panes).ToArray());
    }

    [Fact]
    public async Task PrivateInput_suppresses_history_recording()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xt-hist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new JsonHistoryStore(Path.Combine(dir, "history.json"));
            var history = new HistoryViewModel(store);
            await history.InitializeAsync(new HistorySettings { Enabled = true, MaxEntries = 100, RetentionDays = 30 });
            var tracker = new CommandHistoryTracker(history);

            var tab = CreateTab();
            tab.PrivateInputMode = true;
            tracker.OnTerminalInput(tab, "secret-password\r");
            await Task.Delay(80);
            Assert.Empty(history.Entries);

            tab.PrivateInputMode = false;
            tracker.OnTerminalInput(tab, "echo hello\r");
            await Task.Delay(200);
            Assert.Contains(history.Entries, e => e.Command.Contains("echo hello", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void AppCommandRegistry_registers_unique_ids()
    {
        var registry = new AppCommandRegistry();
        registry.RegisterMany(new[]
        {
            new AppCommand { Id = "a", Title = "A", Category = CommandCategory.Application, Execute = () => { } },
            new AppCommand { Id = "b", Title = "B", Category = CommandCategory.Terminal, Execute = () => { } }
        });
        Assert.Equal(2, registry.Commands.Count);
        Assert.Contains(registry.Commands, c => c.Id == "a");
    }

    [Fact]
    public void PortForward_GetBytes_returns_null_metrics()
    {
        var runtime = new PortForwardRuntimeService(
            new SshTransportFactory(new WindowsCredentialStore(), new SshHostKeyPolicy(new KnownHostsStore(), new TrustOncePrompt())));
        var bytes = runtime.GetBytes(Guid.NewGuid());
        Assert.Null(bytes.Sent);
        Assert.Null(bytes.Received);
    }

    private static TerminalTabViewModel CreateTab()
    {
        var factory = new SessionHostFactory(
            new WindowsCredentialStore(),
            new JsonSshSessionLibrary(),
            new SshHostKeyPolicy(new KnownHostsStore(), new TrustOncePrompt()));
        return new TerminalTabViewModel(new SessionDefinition
        {
            Name = "t",
            Kind = SessionKind.LocalShell,
            Shell = ShellKind.PowerShell
        }, factory);
    }

    private sealed class TrustOncePrompt : ISshHostKeyPrompt
    {
        public HostKeyPromptResult Prompt(HostKeyPromptRequest request) => HostKeyPromptResult.TrustOnce;
    }
}
