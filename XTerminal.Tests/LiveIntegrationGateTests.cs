using System.Text.Json;
using XTerminal.Core.Models;

namespace XTerminal.Tests;

public class LiveIntegrationGateTests
{
    [Fact]
    public void Disabled_without_process_opt_in()
    {
        var c = LiveIntegrationGate.CreateCandidateForTests(processEnabled: false);
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.False(r.IsValid);
        Assert.Contains("disabled", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Process_opt_in_with_complete_config_succeeds()
    {
        var c = LiveIntegrationGate.CreateCandidateForTests();
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.True(r.IsValid, r.Error);
        Assert.NotNull(r.Config);
        Assert.Equal("example.test", r.Config!.Host);
        Assert.Equal(SshAuthMethod.Password, r.Config.AuthMethod);
    }

    [Fact]
    public void Missing_host_fails()
    {
        var c = LiveIntegrationGate.CreateCandidateForTests(host: "  ");
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.False(r.IsValid);
        Assert.Contains("Host", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_username_fails()
    {
        var c = LiveIntegrationGate.CreateCandidateForTests(username: null);
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.False(r.IsValid);
        Assert.Contains("Username", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_password_fails()
    {
        var c = LiveIntegrationGate.CreateCandidateForTests(password: null, passwordWasProvided: false);
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.False(r.IsValid);
        Assert.Contains("Password", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Password_whitespace_preserved()
    {
        var c = LiveIntegrationGate.CreateCandidateForTests(password: "  pass  ");
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.True(r.IsValid, r.Error);
        Assert.Equal("  pass  ", r.Config!.Password);
    }

    [Fact]
    public void Missing_fingerprint_fails()
    {
        var c = LiveIntegrationGate.CreateCandidateForTests(fingerprint: null);
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.False(r.IsValid);
        Assert.Contains("fingerprint", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invalid_fingerprint_format_fails()
    {
        var c = LiveIntegrationGate.CreateCandidateForTests(fingerprint: "not-a-fingerprint");
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.False(r.IsValid);
        Assert.Contains("fingerprint", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sha256_base64_fingerprint_normalizes()
    {
        var bytes = new byte[32];
        for (var i = 0; i < 32; i++) bytes[i] = (byte)i;
        var b64 = Convert.ToBase64String(bytes);
        var c = LiveIntegrationGate.CreateCandidateForTests(fingerprint: "SHA256:" + b64);
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.True(r.IsValid, r.Error);
        Assert.Equal(Convert.ToHexString(bytes), r.Config!.ExpectedHostKeyFingerprintSha256Hex);
    }

    [Fact]
    public void Invalid_port_fails_without_fallback()
    {
        var c = LiveIntegrationGate.CreateCandidateForTests(portText: "0");
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.False(r.IsValid);
        Assert.Contains("Port", r.Error, StringComparison.OrdinalIgnoreCase);

        var c2 = LiveIntegrationGate.CreateCandidateForTests(portText: "abc");
        var r2 = LiveIntegrationConfigValidator.Validate(c2);
        Assert.False(r2.IsValid);
    }

    [Fact]
    public void Unsupported_auth_method_fails()
    {
        var c = LiveIntegrationGate.CreateCandidateForTests(authMethodText: "Agent");
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.False(r.IsValid);
        Assert.Contains("Unsupported", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_private_key_file_fails()
    {
        var c = LiveIntegrationGate.CreateCandidateForTests(
            authMethodText: "PrivateKey",
            passwordWasProvided: false,
            password: null,
            privateKeyPath: Path.Combine(Path.GetTempPath(), "xterminal-missing-key-" + Guid.NewGuid().ToString("N")));
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.False(r.IsValid);
        Assert.Contains("Private key", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Malformed_json_reports_error_without_contents()
    {
        var load = LiveIntegrationConfigLoader.ParseJson("{ not json");
        Assert.False(load.Ok);
        Assert.Contains("malformed", load.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not json", load.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Prohibited_password_field_rejected()
    {
        var load = LiveIntegrationConfigLoader.ParseJson(
            """{"enable":true,"host":"h","username":"u","password":"nope","expectedHostKeyFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""");
        Assert.False(load.Ok);
        Assert.Contains("must not contain", load.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nope", load.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Prohibited_passphrase_field_rejected()
    {
        var load = LiveIntegrationConfigLoader.ParseJson(
            """{"host":"h","username":"u","passphrase":"x","expectedHostKeyFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""");
        Assert.False(load.Ok);
    }

    [Fact]
    public void Enabled_but_incomplete_fails()
    {
        var c = LiveIntegrationGate.CreateCandidateForTests(host: null, username: null, fingerprint: null);
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.False(r.IsValid);
    }

    [Fact]
    public void File_enable_alone_does_not_satisfy_process_gate()
    {
        var c = LiveIntegrationGate.CreateCandidateForTests(processEnabled: false, fileEnable: true);
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.False(r.IsValid);
        Assert.Contains("disabled", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Configuration_isolation_does_not_read_home_for_validator()
    {
        // Validator never touches the developer's integration.local.json.
        var c = LiveIntegrationGate.CreateCandidateForTests(host: "isolated.example", fingerprint: new string('B', 64));
        var r = LiveIntegrationConfigValidator.Validate(c);
        Assert.True(r.IsValid, r.Error);
        Assert.Equal("isolated.example", r.Config!.Host);
    }

    [Fact]
    public void Environment_override_candidate_prefers_env_host()
    {
        LiveIntegrationGate.ResetCacheForTests();
        var prevEnable = Environment.GetEnvironmentVariable("XTERMINAL_IT_ENABLE");
        var prevHost = Environment.GetEnvironmentVariable("XTERMINAL_IT_HOST");
        try
        {
            Environment.SetEnvironmentVariable("XTERMINAL_IT_ENABLE", null);
            Environment.SetEnvironmentVariable("XTERMINAL_IT_HOST", "env-override.example");
            // Without process enable, BuildCandidate still surfaces env host for inspection.
            var candidate = LiveIntegrationGate.BuildCandidateFromEnvironment(
                Path.Combine(Path.GetTempPath(), "xterminal-missing-config-" + Guid.NewGuid().ToString("N") + ".json"));
            Assert.Equal("env-override.example", candidate.Host);
            Assert.False(candidate.ProcessEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XTERMINAL_IT_ENABLE", prevEnable);
            Environment.SetEnvironmentVariable("XTERMINAL_IT_HOST", prevHost);
            LiveIntegrationGate.ResetCacheForTests();
        }
    }

    [Fact]
    public void Valid_json_without_secrets_loads()
    {
        var load = LiveIntegrationConfigLoader.ParseJson(
            """
            {
              "enable": true,
              "host": "server.example.com",
              "port": 22,
              "username": "alice",
              "auth": "Password",
              "expectedHostKeyFingerprint": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
            }
            """);
        Assert.True(load.Ok, load.Error);
        Assert.Equal("server.example.com", load.Document!.Host);
        Assert.Equal(JsonValueKind.Number, load.Document.Port!.Value.ValueKind);
    }

    [Fact]
    public void Gate_disabled_describe_when_process_not_enabled()
    {
        if (LiveIntegrationGate.IsProcessEnabled)
            return;

        Assert.Contains("disabled", LiveIntegrationGate.DescribeConfiguration(), StringComparison.OrdinalIgnoreCase);
        Assert.False(LiveIntegrationGate.IsFullyConfigured);
    }
}
