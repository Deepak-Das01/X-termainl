using System.Text.Json;
using System.Text.Json.Serialization;
using XTerminal.Core.Services;

namespace XTerminal.Core.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;

    public JsonSettingsStore(string? path = null)
    {
        _path = path ?? AppStoragePaths.Settings;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options, cancellationToken);
            return Normalize(settings ?? new AppSettings());
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, Normalize(settings), Options, cancellationToken);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.General ??= new GeneralSettings();
        settings.Terminal ??= new TerminalAppearanceSettings();
        settings.Editor ??= new EditorAppearanceSettings();
        settings.History ??= new HistorySettings();
        settings.Ssh ??= new SshAppSettings();
        settings.Files ??= new FilesAppSettings();
        settings.Security ??= new SecurityAppSettings();
        settings.Advanced ??= new AdvancedAppSettings();
        settings.Keybindings ??= new List<KeybindingEntry>();
        settings.Profiles ??= ShellProfileSettings.CreateDefaults();
        if (settings.Profiles.Count == 0)
            settings.Profiles = ShellProfileSettings.CreateDefaults();

        settings.History.RetentionDays = settings.History.RetentionDays is 0 or 7 or 30 or 90
            ? settings.History.RetentionDays
            : 30;
        settings.History.MaxEntries = Math.Clamp(settings.History.MaxEntries, 100, 100_000);
        settings.History.ExcludePatterns ??= new List<string>();
        settings.Terminal.FontSize = Math.Clamp(settings.Terminal.FontSize, 8, 32);
        settings.Terminal.LineHeight = Math.Clamp(settings.Terminal.LineHeight, 1.0, 2.0);
        settings.Terminal.Padding = Math.Clamp(settings.Terminal.Padding, 0, 32);
        settings.Terminal.ScrollbackLines = Math.Clamp(settings.Terminal.ScrollbackLines, 500, 100_000);
        settings.Ssh.ConnectionTimeoutSeconds = Math.Clamp(settings.Ssh.ConnectionTimeoutSeconds, 5, 120);
        settings.Ssh.KeepAliveIntervalSeconds = Math.Clamp(settings.Ssh.KeepAliveIntervalSeconds, 0, 300);
        settings.Ssh.DefaultPort = Math.Clamp(settings.Ssh.DefaultPort, 1, 65535);
        settings.Ssh.MaxReconnectAttempts = Math.Clamp(settings.Ssh.MaxReconnectAttempts, 0, 20);
        if (string.IsNullOrWhiteSpace(settings.Terminal.FontFamily))
            settings.Terminal.FontFamily = "Cascadia Mono";
        if (string.IsNullOrWhiteSpace(settings.Terminal.SchemeId))
            settings.Terminal.SchemeId = "xterminal-dark";
        if (string.IsNullOrWhiteSpace(settings.Terminal.DefaultShell))
            settings.Terminal.DefaultShell = "PowerShell";

        settings.Advanced.MaxLogFiles = Math.Clamp(settings.Advanced.MaxLogFiles, 1, 90);
        if (string.IsNullOrWhiteSpace(settings.Files.DefaultDownloadDirectory))
        {
            settings.Files.DefaultDownloadDirectory =
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
        }

        settings.Editor.FontSize = Math.Clamp(settings.Editor.FontSize, 8, 32);
        settings.Editor.LineHeight = Math.Clamp(settings.Editor.LineHeight, 1.0, 2.5);
        settings.Editor.TabSize = Math.Clamp(settings.Editor.TabSize, 1, 8);
        settings.Editor.AutoSaveDelayMs = Math.Clamp(settings.Editor.AutoSaveDelayMs, 500, 30000);
        if (string.IsNullOrWhiteSpace(settings.Editor.FontFamily))
            settings.Editor.FontFamily = "Cascadia Code";

        return settings;
    }
}
