using XTerminal.Core.Models;
using XTerminal.Core.Models.X11;
using XTerminal.Core.Security;
using XTerminal.Core.Services;
using XTerminal.Core.Services.X11;
using XTerminal.Protocols;
using XTerminal.Protocols.Ssh;

namespace XTerminal.Tests;

public class X11ForwardingTests
{
    [Fact]
    public void Default_forwarding_disabled()
    {
        var s = new X11SessionSettings();
        Assert.False(s.EnableForwarding);
        Assert.Equal(X11TrustMode.Untrusted, s.TrustMode);
        Assert.False(s.TrustedForwardingAcknowledged);
    }

    [Fact]
    public void Backend_unavailable_for_sshnet_2026()
    {
        Assert.False(X11BackendCapability.IsForwardingAvailable);
        Assert.Contains("SSH.NET", X11BackendCapability.BlockerReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trusted_requires_acknowledgment()
    {
        var s = new X11SessionSettings
        {
            EnableForwarding = true,
            TrustMode = X11TrustMode.Trusted,
            TrustedForwardingAcknowledged = false
        };
        Assert.False(X11ProfileValidator.TryValidate(s, out _));
        s.TrustedForwardingAcknowledged = true;
        Assert.True(X11ProfileValidator.TryValidate(s, out _));
    }

    [Fact]
    public void Invalid_executable_path_rejected()
    {
        var s = new X11SessionSettings { XServerExecutablePath = "relative\\vcxsrv.exe" };
        Assert.False(X11ProfileValidator.TryValidate(s, out _));
    }

    [Fact]
    public void Coordinator_reports_backend_unavailable()
    {
        using var c = new X11ForwardingCoordinator();
        var id = Guid.NewGuid();
        var (ok, status, _) = c.RequestForwarding(id, new X11SessionSettings { EnableForwarding = true });
        Assert.False(ok);
        Assert.Equal(X11ForwardingStatus.BackendUnavailable, status);
        Assert.False(c.ShouldAcceptChannel(id));
    }

    [Fact]
    public void Cookie_generation_and_isolation()
    {
        using var registry = new X11CookieRegistry();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var ca = registry.CreateForSession(a);
        var cb = registry.CreateForSession(b);
        var bytesA = ca.GetCookieCopy();
        Assert.True(registry.ValidateForSession(a, bytesA));
        Assert.False(registry.ValidateForSession(b, bytesA));
        Assert.False(registry.ValidateForSession(a, cb.GetCookieCopy()));
        registry.Release(a);
        Assert.False(registry.ValidateForSession(a, bytesA));
    }

    [Fact]
    public void Incorrect_cookie_rejected()
    {
        using var material = X11CookieMaterial.Generate(Guid.NewGuid());
        Assert.False(material.Matches(new byte[16]));
    }

    [Fact]
    public void Setup_parser_rejects_invalid_byte_order()
    {
        var buf = new byte[12];
        buf[0] = (byte)'X';
        Assert.False(X11SetupMessageParser.TryParseHeader(buf, out _, out var err));
        Assert.Contains("byte order", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Setup_parser_handles_fragment_and_oversized()
    {
        var cookie = new byte[16];
        var full = X11SetupMessageParser.BuildSetupPrefix(true, 11, 0, X11CookieMaterial.MitMagicCookie1, cookie);
        Assert.True(X11SetupMessageParser.TryParseHeader(full, out var header, out _));
        Assert.False(X11SetupMessageParser.TryExtractAuthCookie(full.AsSpan(0, 10), header, out _, out var fragErr));
        Assert.Contains("Fragmented", fragErr, StringComparison.OrdinalIgnoreCase);

        var oversized = X11SetupMessageParser.BuildSetupPrefix(
            false, 11, 0, X11CookieMaterial.MitMagicCookie1, cookie);
        oversized[6] = 0x01;
        oversized[7] = 0x10; // little-endian 0x1001 > 256
        Assert.False(X11SetupMessageParser.TryParseHeader(oversized, out _, out var bigErr));
        Assert.Contains("too large", bigErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Setup_parser_extracts_cookie_both_endian()
    {
        var cookie = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        foreach (var big in new[] { true, false })
        {
            var buf = X11SetupMessageParser.BuildSetupPrefix(big, 11, 0, X11CookieMaterial.MitMagicCookie1, cookie);
            Assert.True(X11SetupMessageParser.TryParseHeader(buf, out var header, out _));
            Assert.True(X11SetupMessageParser.TryExtractAuthCookie(buf, header, out var extracted, out _));
            Assert.Equal(cookie, extracted);
        }
    }

    [Fact]
    public void Unsolicited_channel_rejected()
    {
        Assert.False(X11SetupMessageParser.ShouldAcceptIncomingX11Channel(false, true));
        Assert.False(X11SetupMessageParser.ShouldAcceptIncomingX11Channel(true, false));
        Assert.True(X11SetupMessageParser.ShouldAcceptIncomingX11Channel(true, true));
    }

    [Fact]
    public void Forbidden_x_server_args()
    {
        Assert.True(XServerManager.IsForbiddenArgument("-ac"));
        Assert.True(XServerManager.IsForbiddenArgument("off"));
    }

    [Fact]
    public void Missing_x_server_reported()
    {
        using var mgr = new XServerManager { ConfiguredExecutablePath = @"C:\nonexistent\vcxsrv.exe" };
        Assert.False(mgr.TryResolveExecutable(out _, out _, out var detail));
        Assert.Contains("not found", detail, StringComparison.OrdinalIgnoreCase);
        var snap = mgr.BuildDiagnostics();
        Assert.False(snap.BackendAvailable);
        Assert.False(snap.XServerExecutableValid);
    }

    [Fact]
    public void Diagnostics_are_sanitized_summary()
    {
        using var mgr = new XServerManager();
        var snap = mgr.BuildDiagnostics(X11ForwardingStatus.Requesting);
        Assert.Equal(X11ForwardingStatus.BackendUnavailable, snap.ForwardingStatus);
        Assert.DoesNotContain("cookie", snap.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Persistence_excludes_secrets_shape()
    {
        var record = new Core.Models.Ssh.SshSessionRecord
        {
            X11 = new X11SessionSettings
            {
                EnableForwarding = true,
                TrustMode = X11TrustMode.Untrusted
            }
        };
        Assert.Null(record.X11.XServerExecutablePath);
        Assert.True(record.X11.EnableForwarding);
    }

    [Fact]
    public void X11_session_kind_remains_gated_stub()
    {
        Assert.False(SessionCapabilities.Has(SessionKind.X11, SessionCapability.GraphicalDesktop));
        Assert.False(SessionHostFactory.IsTerminalSessionSupported(SessionKind.X11));
        var factory = new SessionHostFactory(
            new WindowsCredentialStore(),
            new JsonSshSessionLibrary(Path.Combine(Path.GetTempPath(), "xt-ssh-" + Guid.NewGuid().ToString("N") + ".json")),
            new SshHostKeyPolicy(
                new KnownHostsStore(Path.Combine(Path.GetTempPath(), "xt-kh-" + Guid.NewGuid().ToString("N") + ".json")),
                new AlwaysTrustPrompt()));
        var stub = factory.Create(SessionKind.X11);
        Assert.False(stub.IsImplemented);
        Assert.Equal(SessionKind.X11, stub.Kind);
    }

    private sealed class AlwaysTrustPrompt : ISshHostKeyPrompt
    {
        public HostKeyPromptResult Prompt(HostKeyPromptRequest request) => HostKeyPromptResult.TrustOnce;
    }
}
