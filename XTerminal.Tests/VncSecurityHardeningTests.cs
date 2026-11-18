using System.Text;
using XTerminal.Core.Models.Desktop;
using XTerminal.Core.Services;
using XTerminal.Protocols.Vnc;

namespace XTerminal.Tests;

public class VncSecurityHardeningTests
{
    [Fact]
    public async Task Rejects_malformed_rfb_header()
    {
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes("HTTP/1.1 OK\n"));
        var client = new VncRfbClient();
        var profile = new VncSessionProfile { Host = "127.0.0.1", AllowUnencrypted = true, AuthenticationMode = VncAuthenticationMode.None };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ConnectOnStreamAsync(ms, profile, null));
    }

    [Fact]
    public void SelectSecurity_path_requires_supported_types()
    {
        // Direct profile policy: without AllowUnencrypted, None is rejected by validator.
        var p = new VncSessionProfile
        {
            Host = "127.0.0.1",
            AuthenticationMode = VncAuthenticationMode.None,
            AllowUnencrypted = false
        };
        Assert.False(VncProfileValidator.TryValidate(p, out _));
    }

    [Fact]
    public void Tunnel_profile_requires_ssh_session()
    {
        var p = new VncSessionProfile
        {
            Host = "127.0.0.1",
            Port = 5900,
            UseSshTunnel = true,
            SshTunnelSessionId = null
        };
        Assert.False(VncProfileValidator.TryValidate(p, out _));
        p.SshTunnelSessionId = Guid.NewGuid();
        Assert.True(VncProfileValidator.TryValidate(p, out _));
    }

    [Fact]
    public void Read_only_suppresses_input_when_disconnected_or_flagged()
    {
        var client = new VncRfbClient();
        // No stream — must not throw
        client.SendKey(0x20, true);
        client.SendPointer(1, 1, 1);
    }

    [Fact]
    public void Rect_pixel_limit_aligned_with_message_bytes()
    {
        Assert.True((long)VncLimits.MaxRectPixels * 4 <= VncLimits.MaxMessageBytes);
    }

    [Fact]
    public async Task Cancellation_during_auth_propagates()
    {
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("RFB 003.008\n"));
        ms.Position = 0;
        var client = new VncRfbClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var profile = new VncSessionProfile { Host = "127.0.0.1", AllowUnencrypted = true, AuthenticationMode = VncAuthenticationMode.None };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ConnectOnStreamAsync(ms, profile, null, cts.Token));
    }
}
