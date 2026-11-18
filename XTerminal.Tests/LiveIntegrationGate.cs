using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using XTerminal.Core.Models;

namespace XTerminal.Tests;

/// <summary>
/// Validated live-integration connection settings. Secrets are never logged.
/// </summary>
public sealed class LiveIntegrationConfig
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Username { get; init; }
    public required SshAuthMethod AuthMethod { get; init; }
    /// <summary>Exact password including whitespace; never logged.</summary>
    public string? Password { get; init; }
    public string? PrivateKeyPath { get; init; }
    /// <summary>Normalized uppercase hex SHA-256 (64 chars).</summary>
    public required string ExpectedHostKeyFingerprintSha256Hex { get; init; }
}

/// <summary>
/// Unvalidated candidate assembled from file + environment. Used by unit tests with controlled inputs.
/// </summary>
public sealed class LiveIntegrationCandidate
{
    public bool ProcessEnabled { get; init; }
    public bool? FileEnable { get; init; }
    public string? Host { get; init; }
    /// <summary>Raw port text from env or file; null means unspecified.</summary>
    public string? PortText { get; init; }
    public string? Username { get; init; }
    public string? AuthMethodText { get; init; }
    /// <summary>Exact password; null = missing. Whitespace preserved; not trimmed.</summary>
    public string? Password { get; init; }
    public bool PasswordWasProvided { get; init; }
    public string? PrivateKeyPath { get; init; }
    public string? ExpectedHostKeyFingerprint { get; init; }
    public string? FileLoadError { get; init; }
    public bool FileHadProhibitedSecrets { get; init; }
}

public sealed class LiveIntegrationValidationResult
{
    public bool IsValid { get; init; }
    public LiveIntegrationConfig? Config { get; init; }
    public string Error { get; init; } = "";
}

public sealed class LiveIntegrationFileLoadResult
{
    public bool FileExists { get; init; }
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public LiveIntegrationFileDocument? Document { get; init; }
}

/// <summary>Non-secret fields allowed in integration.local.json.</summary>
public sealed class LiveIntegrationFileDocument
{
    public bool? Enable { get; set; }
    public string? Host { get; set; }
    public JsonElement? Port { get; set; }
    public string? Username { get; set; }
    public string? Auth { get; set; }
    public string? AuthMethod { get; set; }
    public string? ExpectedHostKeyFingerprint { get; set; }
    public string? ExpectedHostKeyFingerprintSha256 { get; set; }
    public string? PrivateKeyPath { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Loads local JSON without validating completeness. Never returns secret values from the file.</summary>
public static class LiveIntegrationConfigLoader
{
    private static readonly HashSet<string> ProhibitedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passphrase", "secret", "secrets", "privatekey", "private_key",
        "privatekeypem", "keymaterial", "credential", "credentials", "token", "apikey", "api_key"
    };

    public static string DefaultConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".xterminal",
            "integration.local.json");

    public static LiveIntegrationFileLoadResult LoadFile(string path)
    {
        if (!File.Exists(path))
            return new LiveIntegrationFileLoadResult { FileExists = false, Ok = true, Document = null };

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch
        {
            return new LiveIntegrationFileLoadResult
            {
                FileExists = true,
                Ok = false,
                Error = "Configuration file could not be read."
            };
        }

        return ParseJson(json);
    }

    public static LiveIntegrationFileLoadResult ParseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new LiveIntegrationFileLoadResult
                {
                    FileExists = true,
                    Ok = false,
                    Error = "Configuration file must contain a JSON object."
                };
            }

            if (RootHasProhibitedSecrets(doc.RootElement))
            {
                return new LiveIntegrationFileLoadResult
                {
                    FileExists = true,
                    Ok = false,
                    Error = "Configuration file must not contain passwords, passphrases, or other secrets."
                };
            }
        }
        catch (JsonException)
        {
            return new LiveIntegrationFileLoadResult
            {
                FileExists = true,
                Ok = false,
                Error = "Configuration file contains malformed JSON."
            };
        }

        LiveIntegrationFileDocument? model;
        try
        {
            model = JsonSerializer.Deserialize<LiveIntegrationFileDocument>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException)
        {
            return new LiveIntegrationFileLoadResult
            {
                FileExists = true,
                Ok = false,
                Error = "Configuration file contains malformed JSON."
            };
        }
        catch
        {
            return new LiveIntegrationFileLoadResult
            {
                FileExists = true,
                Ok = false,
                Error = "Configuration file could not be parsed."
            };
        }

        if (model is null)
        {
            return new LiveIntegrationFileLoadResult
            {
                FileExists = true,
                Ok = false,
                Error = "Configuration file is empty."
            };
        }

        return new LiveIntegrationFileLoadResult { FileExists = true, Ok = true, Document = model };
    }

    public static bool RootHasProhibitedSecrets(JsonElement root)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (IsProhibitedKey(prop.Name))
                return true;
        }

        return false;
    }

    public static bool IsProhibitedKey(string key)
    {
        if (ProhibitedKeys.Contains(key))
            return true;
        var compact = key.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        return ProhibitedKeys.Contains(compact);
    }

    public static string? PortElementToText(JsonElement? element)
    {
        if (element is null)
            return null;
        var e = element.Value;
        return e.ValueKind switch
        {
            JsonValueKind.Number => e.TryGetInt32(out var n)
                ? n.ToString(CultureInfo.InvariantCulture)
                : e.GetRawText(),
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Null => null,
            _ => e.GetRawText()
        };
    }
}

/// <summary>Validates a candidate without reading the developer's home directory.</summary>
public static class LiveIntegrationConfigValidator
{
    public static LiveIntegrationValidationResult Validate(LiveIntegrationCandidate candidate)
    {
        if (!candidate.ProcessEnabled)
            return Fail("Live integration disabled (set XTERMINAL_IT_ENABLE=1).");

        if (!string.IsNullOrEmpty(candidate.FileLoadError))
            return Fail(candidate.FileLoadError!);

        if (candidate.FileHadProhibitedSecrets)
            return Fail("Configuration file must not contain passwords, passphrases, or other secrets.");

        var host = TrimToNull(candidate.Host);
        if (host is null)
            return Fail("Host is required.");

        var user = TrimToNull(candidate.Username);
        if (user is null)
            return Fail("Username is required.");

        if (!TryParsePort(candidate.PortText, out var port, out var portError))
            return Fail(portError);

        if (!TryParseAuth(candidate.AuthMethodText, out var auth, out var authError))
            return Fail(authError);

        if (!LiveIntegrationFingerprint.TryNormalize(candidate.ExpectedHostKeyFingerprint, out var fpHex, out var fpError))
            return Fail(fpError);

        string? password = null;
        string? keyPath = null;

        if (auth == SshAuthMethod.Password)
        {
            if (!candidate.PasswordWasProvided || candidate.Password is null)
                return Fail("Password missing (set XTERMINAL_IT_PASSWORD in-process via secure prompt).");
            if (candidate.Password.Length == 0)
                return Fail("Password is required for Password authentication.");
            // Preserve exactly — including leading/trailing whitespace.
            password = candidate.Password;
        }
        else if (auth == SshAuthMethod.PrivateKey)
        {
            keyPath = TrimToNull(candidate.PrivateKeyPath);
            if (keyPath is null)
                return Fail("Private key path is required for PrivateKey authentication.");
            if (!File.Exists(keyPath))
                return Fail("Private key file was not found.");
            try
            {
                using var _ = File.OpenRead(keyPath);
            }
            catch
            {
                return Fail("Private key file is not readable.");
            }
        }
        else
        {
            return Fail("Unsupported authentication method for live integration.");
        }

        return new LiveIntegrationValidationResult
        {
            IsValid = true,
            Config = new LiveIntegrationConfig
            {
                Host = host,
                Port = port,
                Username = user,
                AuthMethod = auth,
                Password = password,
                PrivateKeyPath = keyPath,
                ExpectedHostKeyFingerprintSha256Hex = fpHex
            }
        };
    }

    private static LiveIntegrationValidationResult Fail(string error) =>
        new() { IsValid = false, Error = error };

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static bool TryParsePort(string? portText, out int port, out string error)
    {
        port = 22;
        error = "";
        if (string.IsNullOrWhiteSpace(portText))
        {
            port = 22;
            return true;
        }

        if (!int.TryParse(portText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ||
            p is < 1 or > 65535)
        {
            error = "Port must be an integer between 1 and 65535.";
            return false;
        }

        port = p;
        return true;
    }

    public static bool TryParseAuth(string? text, out SshAuthMethod auth, out string error)
    {
        auth = SshAuthMethod.Password;
        error = "";
        var t = string.IsNullOrWhiteSpace(text) ? "Password" : text.Trim();
        if (string.Equals(t, "Password", StringComparison.OrdinalIgnoreCase))
        {
            auth = SshAuthMethod.Password;
            return true;
        }

        if (string.Equals(t, "PrivateKey", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t, "Key", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t, "PublicKey", StringComparison.OrdinalIgnoreCase))
        {
            auth = SshAuthMethod.PrivateKey;
            return true;
        }

        error = "Unsupported authentication method. Supported: Password, PrivateKey.";
        return false;
    }
}

public static class LiveIntegrationFingerprint
{
    public static bool TryNormalize(string? raw, out string hex, out string error)
    {
        hex = "";
        error = "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Expected SHA-256 host-key fingerprint is required.";
            return false;
        }

        var s = raw.Trim();
        if (s.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
            s = s[7..].Trim();

        s = s.Replace(":", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);

        if (s.Length == 64 && IsHex(s))
        {
            hex = s.ToUpperInvariant();
            return true;
        }

        // Base64 / base64url (OpenSSH ssh-keygen -lf)
        var b64 = s.Replace('-', '+').Replace('_', '/');
        while (b64.Length % 4 != 0)
            b64 += "=";

        try
        {
            var bytes = Convert.FromBase64String(b64);
            if (bytes.Length != 32)
            {
                error = "Invalid host-key fingerprint format.";
                return false;
            }

            hex = Convert.ToHexString(bytes);
            return true;
        }
        catch
        {
            error = "Invalid host-key fingerprint format.";
            return false;
        }
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        return true;
    }
}

/// <summary>
/// Opt-in gate for private-server integration tests. Requires process-level XTERMINAL_IT_ENABLE=1.
/// File enable is advisory for the local runner; it cannot enable tests alone.
/// </summary>
public static class LiveIntegrationGate
{
    private static readonly object Sync = new();
    private static LiveIntegrationFileLoadResult? _cachedFile;
    private static string? _cachedPath;

    public static bool IsProcessEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("XTERMINAL_IT_ENABLE"), "1", StringComparison.Ordinal);

    /// <summary>True only when process opt-in is set and configuration validates.</summary>
    public static bool IsEnabled => IsProcessEnabled;

    public static bool IsFullyConfigured => TryGetValidatedConfig(out _, out _);

    public static string? Host => TryGetValidatedConfig(out var c, out _) ? c.Host : TrimEnvOrFileHost();
    public static string? User => TryGetValidatedConfig(out var c, out _) ? c.Username : TrimEnvOrFileUser();
    public static int Port => TryGetValidatedConfig(out var c, out _) ? c.Port : TryPeekPort();
    public static string AuthMethod => TryGetValidatedConfig(out var c, out _)
        ? c.AuthMethod.ToString()
        : (NullIfWhite(Environment.GetEnvironmentVariable("XTERMINAL_IT_AUTH")) ?? PeekFileAuth() ?? "Password");

    public static string? ExpectedHostKeyFingerprint =>
        NullIfWhite(Environment.GetEnvironmentVariable("XTERMINAL_IT_HOSTKEY_SHA256")) ??
        PeekFileFingerprint();

    public static string? PrivateKeyPath =>
        NullIfWhite(Environment.GetEnvironmentVariable("XTERMINAL_IT_KEY_PATH")) ??
        PeekFileKeyPath();

    /// <summary>Exact password from process environment; never trimmed. Not logged.</summary>
    public static string? Password
    {
        get
        {
            // Distinguish missing vs empty: GetEnvironmentVariable returns null when unset.
            return Environment.GetEnvironmentVariable("XTERMINAL_IT_PASSWORD");
        }
    }

    public static string DescribeConfiguration()
    {
        if (!IsProcessEnabled)
            return "Live integration disabled (set XTERMINAL_IT_ENABLE=1).";

        if (TryGetValidatedConfig(out _, out var error))
            return "Configured.";

        return error;
    }

    public static bool TryGetValidatedConfig(out LiveIntegrationConfig config, out string error)
    {
        var result = LiveIntegrationConfigValidator.Validate(BuildCandidateFromEnvironment());
        if (result.IsValid && result.Config is not null)
        {
            config = result.Config;
            error = "";
            return true;
        }

        config = null!;
        error = result.Error;
        return false;
    }

    /// <summary>Build candidate from controlled inputs (unit tests). Does not read the home directory.</summary>
    public static LiveIntegrationCandidate CreateCandidateForTests(
        bool processEnabled = true,
        string? host = "example.test",
        string? portText = "22",
        string? username = "ituser",
        string? authMethodText = "Password",
        string? password = "secret",
        bool passwordWasProvided = true,
        string? privateKeyPath = null,
        string? fingerprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        string? fileLoadError = null,
        bool fileHadProhibitedSecrets = false,
        bool? fileEnable = true) =>
        new()
        {
            ProcessEnabled = processEnabled,
            FileEnable = fileEnable,
            Host = host,
            PortText = portText,
            Username = username,
            AuthMethodText = authMethodText,
            Password = password,
            PasswordWasProvided = passwordWasProvided,
            PrivateKeyPath = privateKeyPath,
            ExpectedHostKeyFingerprint = fingerprint,
            FileLoadError = fileLoadError,
            FileHadProhibitedSecrets = fileHadProhibitedSecrets
        };

    public static void ResetCacheForTests()
    {
        lock (Sync)
        {
            _cachedFile = null;
            _cachedPath = null;
        }
    }

    public static LiveIntegrationCandidate BuildCandidateFromEnvironment(string? configPath = null)
    {
        var path = configPath ?? LiveIntegrationConfigLoader.DefaultConfigPath;
        var file = GetFile(path);

        string? fileLoadError = null;
        bool prohibited = false;
        LiveIntegrationFileDocument? doc = null;

        if (!file.Ok)
        {
            fileLoadError = file.Error;
        }
        else
        {
            doc = file.Document;
        }

        var envPassword = Environment.GetEnvironmentVariable("XTERMINAL_IT_PASSWORD");
        var envPort = Environment.GetEnvironmentVariable("XTERMINAL_IT_PORT");
        var filePortText = LiveIntegrationConfigLoader.PortElementToText(doc?.Port);

        return new LiveIntegrationCandidate
        {
            ProcessEnabled = IsProcessEnabled,
            FileEnable = doc?.Enable,
            Host = NullIfWhite(Environment.GetEnvironmentVariable("XTERMINAL_IT_HOST")) ?? NullIfWhite(doc?.Host),
            PortText = NullIfWhite(envPort) ?? filePortText,
            Username = NullIfWhite(Environment.GetEnvironmentVariable("XTERMINAL_IT_USER")) ?? NullIfWhite(doc?.Username),
            AuthMethodText = NullIfWhite(Environment.GetEnvironmentVariable("XTERMINAL_IT_AUTH"))
                             ?? NullIfWhite(doc?.Auth)
                             ?? NullIfWhite(doc?.AuthMethod),
            Password = envPassword,
            PasswordWasProvided = envPassword is not null,
            PrivateKeyPath = NullIfWhite(Environment.GetEnvironmentVariable("XTERMINAL_IT_KEY_PATH"))
                             ?? NullIfWhite(doc?.PrivateKeyPath),
            ExpectedHostKeyFingerprint = NullIfWhite(Environment.GetEnvironmentVariable("XTERMINAL_IT_HOSTKEY_SHA256"))
                                         ?? NullIfWhite(doc?.ExpectedHostKeyFingerprint)
                                         ?? NullIfWhite(doc?.ExpectedHostKeyFingerprintSha256),
            FileLoadError = fileLoadError,
            FileHadProhibitedSecrets = prohibited
        };
    }

    private static LiveIntegrationFileLoadResult GetFile(string path)
    {
        lock (Sync)
        {
            if (_cachedFile is not null && string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase))
                return _cachedFile;

            _cachedPath = path;
            _cachedFile = LiveIntegrationConfigLoader.LoadFile(path);
            return _cachedFile;
        }
    }

    private static string? TrimEnvOrFileHost() =>
        NullIfWhite(Environment.GetEnvironmentVariable("XTERMINAL_IT_HOST")) ??
        NullIfWhite(GetFile(LiveIntegrationConfigLoader.DefaultConfigPath).Document?.Host);

    private static string? TrimEnvOrFileUser() =>
        NullIfWhite(Environment.GetEnvironmentVariable("XTERMINAL_IT_USER")) ??
        NullIfWhite(GetFile(LiveIntegrationConfigLoader.DefaultConfigPath).Document?.Username);

    private static int TryPeekPort()
    {
        var text = NullIfWhite(Environment.GetEnvironmentVariable("XTERMINAL_IT_PORT"))
                   ?? LiveIntegrationConfigLoader.PortElementToText(
                       GetFile(LiveIntegrationConfigLoader.DefaultConfigPath).Document?.Port);
        return LiveIntegrationConfigValidator.TryParsePort(text, out var p, out _) ? p : 22;
    }

    private static string? PeekFileAuth()
    {
        var doc = GetFile(LiveIntegrationConfigLoader.DefaultConfigPath).Document;
        return NullIfWhite(doc?.Auth) ?? NullIfWhite(doc?.AuthMethod);
    }

    private static string? PeekFileFingerprint()
    {
        var doc = GetFile(LiveIntegrationConfigLoader.DefaultConfigPath).Document;
        return NullIfWhite(doc?.ExpectedHostKeyFingerprint) ?? NullIfWhite(doc?.ExpectedHostKeyFingerprintSha256);
    }

    private static string? PeekFileKeyPath() =>
        NullIfWhite(GetFile(LiveIntegrationConfigLoader.DefaultConfigPath).Document?.PrivateKeyPath);

    private static string? NullIfWhite(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
