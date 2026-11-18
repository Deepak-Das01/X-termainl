using XTerminal.Core.Models;
using XTerminal.Core.Models.Desktop;
using XTerminal.Core.Services;
using XTerminal.Protocols;
using XTerminal.Protocols.Vnc;

namespace XTerminal.Tests;

public class VncProfileAndAuthTests
{
    [Fact]
    public void Default_port_is_5900()
    {
        var def = new SessionDefinition { Kind = SessionKind.Vnc, Host = "vnc.example.com" };
        Assert.Equal(5900, def.ToVncProfile().Port);
    }

    [Fact]
    public void Display_number_maps_to_port()
    {
        Assert.Equal(5901, VncProfileValidator.ResolvePort(1, treatAsDisplayNumber: true));
        Assert.Equal(5900, VncProfileValidator.ResolvePort(5900, treatAsDisplayNumber: false));
    }

    [Fact]
    public void None_auth_requires_allow_unencrypted()
    {
        var p = new VncSessionProfile
        {
            Host = "127.0.0.1",
            Port = 5900,
            AuthenticationMode = VncAuthenticationMode.None,
            AllowUnencrypted = false
        };
        Assert.False(VncProfileValidator.TryValidate(p, out _));
        p.AllowUnencrypted = true;
        Assert.True(VncProfileValidator.TryValidate(p, out _));
    }

    [Fact]
    public void Vnc_des_auth_is_deterministic()
    {
        var challenge = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var a = VncDesAuth.EncryptChallenge(challenge, "secret");
        var b = VncDesAuth.EncryptChallenge(challenge, "secret");
        Assert.Equal(a, b);
        Assert.Equal(16, a.Length);
        Assert.NotEqual(a, VncDesAuth.EncryptChallenge(challenge, "other"));
    }

    [Fact]
    public void Capabilities_mark_graphical_and_plaintext()
    {
        Assert.True(SessionCapabilities.Has(SessionKind.Vnc, SessionCapability.GraphicalDesktop));
        Assert.True(SessionCapabilities.Has(SessionKind.Vnc, SessionCapability.PlaintextNetwork));
        Assert.False(SessionCapabilities.Has(SessionKind.Vnc, SessionCapability.Sftp));
        Assert.False(SessionHostFactory.IsTerminalSessionSupported(SessionKind.Vnc));
    }

    [Fact]
    public void Enforces_framebuffer_dimension_limits()
    {
        Assert.Equal(8192, VncLimits.MaxWidth);
        Assert.Equal(8192, VncLimits.MaxHeight);
        Assert.True(VncLimits.MaxMessageBytes > 0);
    }
}
