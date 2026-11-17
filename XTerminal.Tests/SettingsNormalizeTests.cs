using XTerminal.Core.Settings;

namespace XTerminal.Tests;

public class SettingsNormalizeTests
{
    [Fact]
    public async Task Normalize_and_roundtrip_persist_clamped_values()
    {
        var path = Path.Combine(Path.GetTempPath(), "xterminal-settings-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            var settings = new AppSettings
            {
                General = new GeneralSettings
                {
                    OpenDefaultShellOnStartup = false,
                    ConfirmExitWithActiveSessions = false,
                    ShowToastNotifications = false
                },
                Ssh = new SshAppSettings
                {
                    ConnectionTimeoutSeconds = 999,
                    DefaultPort = 0,
                    MaxReconnectAttempts = 100
                },
                Advanced = new AdvancedAppSettings { MaxLogFiles = 0, EnableFileLogging = false },
                Terminal = new TerminalAppearanceSettings { FontSize = 99, ScrollbackLines = 1 }
            };

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            Assert.False(loaded.General.OpenDefaultShellOnStartup);
            Assert.False(loaded.General.ShowToastNotifications);
            Assert.Equal(120, loaded.Ssh.ConnectionTimeoutSeconds);
            Assert.Equal(1, loaded.Ssh.DefaultPort);
            Assert.Equal(20, loaded.Ssh.MaxReconnectAttempts);
            Assert.InRange(loaded.Advanced.MaxLogFiles, 1, 90);
            Assert.False(loaded.Advanced.EnableFileLogging);
            Assert.Equal(32, loaded.Terminal.FontSize);
            Assert.Equal(500, loaded.Terminal.ScrollbackLines);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
