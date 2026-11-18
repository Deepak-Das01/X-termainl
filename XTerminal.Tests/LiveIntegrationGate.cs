using System.Text.Json;
using System.Text.Json.Serialization;

namespace XTerminal.Tests;

/// <summary>
/// Opt-in gate for private-server integration tests. Never enabled in CI by default.
/// Loads non-secret connection metadata from %USERPROFILE%\.xterminal\integration.local.json
/// and optional environment overrides. Passwords are never read from committed files.
/// </summary>
public static class LiveIntegrationGate
{
    private static readonly object Sync = new();
    private static LiveIntegrationConfig? _fileConfig;
    private static bool _fileLoaded;

    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("XTERMINAL_IT_ENABLE"), "1", StringComparison.Ordinal) ||
        (LoadFile()?.Enable == true);

    public static string? Host =>
        NullIfEmpty(Environment.GetEnvironmentVariable("XTERMINAL_IT_HOST")) ??
        NullIfEmpty(LoadFile()?.Host);

    public static string? User =>
        NullIfEmpty(Environment.GetEnvironmentVariable("XTERMINAL_IT_USER")) ??
        NullIfEmpty(LoadFile()?.Username);

    public static int Port
    {
        get
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("XTERMINAL_IT_PORT"), out var p) &&
                p is > 0 and <= 65535)
                return p;
            var filePort = LoadFile()?.Port ?? 0;
            return filePort is > 0 and <= 65535 ? filePort : 22;
        }
    }

    public static string AuthMethod =>
        NullIfEmpty(Environment.GetEnvironmentVariable("XTERMINAL_IT_AUTH")) ??
        NullIfEmpty(LoadFile()?.Auth) ??
        "Password";

    public static string? ExpectedHostKeyFingerprint =>
        NullIfEmpty(Environment.GetEnvironmentVariable("XTERMINAL_IT_HOSTKEY_SHA256")) ??
        NullIfEmpty(LoadFile()?.ExpectedHostKeyFingerprint);

    public static string? PrivateKeyPath =>
        NullIfEmpty(Environment.GetEnvironmentVariable("XTERMINAL_IT_KEY_PATH")) ??
        NullIfEmpty(LoadFile()?.PrivateKeyPath);

    /// <summary>
    /// Password from process environment only (set by local secure prompt script).
    /// Never logged or asserted into output.
    /// </summary>
    public static string? Password =>
        NullIfEmpty(Environment.GetEnvironmentVariable("XTERMINAL_IT_PASSWORD"));

    public static bool IsFullyConfigured =>
        IsEnabled &&
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(User) &&
        (string.Equals(AuthMethod, "Password", StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrEmpty(Password)
            : !string.IsNullOrWhiteSpace(PrivateKeyPath));

    public static string DescribeConfiguration()
    {
        if (!IsEnabled) return "Live integration disabled (set XTERMINAL_IT_ENABLE=1).";
        if (string.IsNullOrWhiteSpace(Host)) return "Host missing.";
        if (string.IsNullOrWhiteSpace(User)) return "Username missing.";
        if (string.Equals(AuthMethod, "Password", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(Password))
            return "Password missing (set XTERMINAL_IT_PASSWORD in-process via secure prompt).";
        if (!string.Equals(AuthMethod, "Password", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(PrivateKeyPath))
            return "Private key path missing.";
        return "Configured.";
    }

    private static LiveIntegrationConfig? LoadFile()
    {
        lock (Sync)
        {
            if (_fileLoaded) return _fileConfig;
            _fileLoaded = true;
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".xterminal",
                    "integration.local.json");
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                _fileConfig = JsonSerializer.Deserialize<LiveIntegrationConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            }
            catch
            {
                _fileConfig = null;
            }

            return _fileConfig;
        }
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class LiveIntegrationConfig
    {
        public bool Enable { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; } = 22;
        public string? Username { get; set; }
        public string? Auth { get; set; }
        public string? ExpectedHostKeyFingerprint { get; set; }
        public string? PrivateKeyPath { get; set; }

        // Intentionally no Password property — rejected if present by policy docs.
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}
