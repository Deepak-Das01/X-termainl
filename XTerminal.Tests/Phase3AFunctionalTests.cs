using XTerminal.Core.Models;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Services;

namespace XTerminal.Tests;

/// <summary>Phase 3A regression coverage: Tasks 0–3 (SSH management, jumps, OpenSSH import).</summary>
public class Phase3AFunctionalTests
{
    [Fact]
    public void OpenSshConfig_parses_supported_directives_and_blocks_ProxyCommand()
    {
        var cfg = """
            Host bastion
              HostName jump.example.com
              User ops
              Port 2222
              IdentityFile ~/.ssh/id_ed25519
              ProxyJump jumpuser@edge.example.com:2200
              ServerAliveInterval 45
              ConnectTimeout 20
              ProxyCommand nc %h %p
              ForwardAgent yes
            Host *.internal
              User root
            """;

        var preview = OpenSshConfigParser.Parse(cfg);
        Assert.Equal(2, preview.Entries.Count);
        var bastion = preview.Entries[0];
        Assert.True(OpenSshConfigParser.CanImport(bastion));
        Assert.Contains(bastion.UnsupportedDirectives, d => d.Contains("ProxyCommand", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(bastion.UnsupportedDirectives, d => d.Contains("ForwardAgent", StringComparison.OrdinalIgnoreCase));

        var session = OpenSshConfigParser.ToSession(bastion);
        Assert.Equal("jump.example.com", session.Host);
        Assert.Equal(2222, session.Port);
        Assert.Equal("ops", session.Username);
        Assert.Equal("edge.example.com", session.ProxyJumpHost);
        Assert.Equal(2200, session.ProxyJumpPort);
        Assert.Equal("jumpuser", session.ProxyJumpUsername);
        Assert.Equal(45, session.KeepAliveIntervalSeconds);
        Assert.False(string.IsNullOrWhiteSpace(session.PrivateKeyPath));
        Assert.DoesNotContain("ProxyCommand", session.StartupCommand ?? "", StringComparison.OrdinalIgnoreCase);

        Assert.False(OpenSshConfigParser.CanImport(preview.Entries[1]));
    }

    [Fact]
    public void OpenSsh_expand_path_home_and_rejects_unsafe()
    {
        var expanded = OpenSshConfigParser.ExpandPath("~/.ssh/id_rsa");
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), expanded);
        Assert.Throws<InvalidOperationException>(() => OpenSshConfigParser.ExpandPath("id_rsa|rm -rf /"));
    }

    [Fact]
    public void OpenSsh_ProxyJump_csv_stores_extra_hops_as_tags()
    {
        var cfg = """
            Host target
              HostName internal.example
              User app
              ProxyJump edge@bastion.example:2200,mid@jump.example:2222
            """;
        var session = OpenSshConfigParser.ToSession(OpenSshConfigParser.Parse(cfg).Entries[0]);
        Assert.Equal("bastion.example", session.ProxyJumpHost);
        Assert.Equal(2200, session.ProxyJumpPort);
        Assert.Equal("edge", session.ProxyJumpUsername);
        Assert.Contains(session.Tags, t => t.Equals("jump:mid@jump.example:2222", StringComparison.OrdinalIgnoreCase));

        var hops = JumpHostChain.FromSession(session);
        Assert.Equal(2, hops.Count);
        Assert.Empty(JumpHostChain.Validate(hops, "internal.example", 22));
    }

    [Fact]
    public void JumpHostChain_detects_loops_and_target_duplicate()
    {
        var hops = new List<JumpHop>
        {
            new() { Host = "a", Port = 22 },
            new() { Host = "a", Port = 22 }
        };
        var errors = JumpHostChain.Validate(hops, "target", 22);
        Assert.Contains(errors, e => e.Contains("loop", StringComparison.OrdinalIgnoreCase));

        errors = JumpHostChain.Validate(
            [new JumpHop { Host = "target", Port = 22 }],
            "target", 22);
        Assert.Contains(errors, e => e.Contains("duplicates the target", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void JumpHostChain_rejects_over_max_hops()
    {
        var hops = Enumerable.Range(1, JumpHostChain.MaxHops + 1)
            .Select(i => new JumpHop { Host = $"h{i}", Port = 22 })
            .ToList();
        var errors = JumpHostChain.Validate(hops, "target", 22);
        Assert.Contains(errors, e => e.Contains("maximum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SshFailureKinds_classify_dns_auth_hostkey_timeout()
    {
        Assert.Equal(SshFailureKind.Dns,
            SshConnectionDiagnosticReport.Classify(new Exception("No such host is known")));
        Assert.Equal(SshFailureKind.Authentication,
            SshConnectionDiagnosticReport.Classify(new Exception("Permission denied (password)")));
        Assert.Equal(SshFailureKind.HostKey,
            SshConnectionDiagnosticReport.Classify(new Exception("host key verification failed")));
        Assert.Equal(SshFailureKind.Timeout,
            SshConnectionDiagnosticReport.Classify(new TimeoutException("connect timed out")));
        Assert.Equal(SshFailureKind.Cancelled,
            SshConnectionDiagnosticReport.Classify(new OperationCanceledException()));
    }

    [Fact]
    public void SshDiagnostics_sanitize_strips_secret_words()
    {
        var text = SshConnectionDiagnosticReport.Sanitize("bad password value with passphrase");
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passphrase", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("********", text);
    }

    [Fact]
    public void SshAuthMethod_includes_keyboard_interactive()
    {
        Assert.Contains(SshAuthMethod.KeyboardInteractive, Enum.GetValues<SshAuthMethod>());
        Assert.Contains(SshAuthMethod.Password, Enum.GetValues<SshAuthMethod>());
        Assert.Contains(SshAuthMethod.PrivateKey, Enum.GetValues<SshAuthMethod>());
        Assert.Contains(SshAuthMethod.Agent, Enum.GetValues<SshAuthMethod>());
    }

    [Fact]
    public void OpenSsh_import_session_never_embeds_secrets()
    {
        var cfg = """
            Host demo
              HostName demo.example
              User root
              IdentityFile ~/.ssh/id_ed25519
            """;
        var session = OpenSshConfigParser.ToSession(OpenSshConfigParser.Parse(cfg).Entries[0]);
        var json = System.Text.Json.JsonSerializer.Serialize(session);
        Assert.DoesNotContain("BEGIN", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(session.PrivateKeyPath));
    }
}
