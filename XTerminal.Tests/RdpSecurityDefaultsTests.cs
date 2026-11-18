using XTerminal.Core.Models.Desktop;
using XTerminal.Core.Services;
using XTerminal.App.Desktop;

namespace XTerminal.Tests;

public class RdpSecurityDefaultsTests
{
    [Fact]
    public void Secure_defaults_disable_redirection()
    {
        var p = new RdpSessionProfile { Host = "rdp.example.com" };
        Assert.True(p.RequireNla);
        Assert.Equal(2, p.AuthenticationLevel);
        Assert.False(p.RedirectClipboard);
        Assert.False(p.RedirectDrives);
        Assert.False(p.RedirectPrinters);
        Assert.False(p.RedirectAudio);
        Assert.True(RdpProfileValidator.TryValidate(p, out _));
    }

    [Fact]
    public void Disabling_nla_with_weak_auth_rejected()
    {
        var p = new RdpSessionProfile
        {
            Host = "rdp.example.com",
            RequireNla = false,
            AuthenticationLevel = 0
        };
        Assert.False(RdpProfileValidator.TryValidate(p, out _));
    }

    [Fact]
    public void Runtime_availability_check_does_not_throw()
    {
        _ = RdpActiveXHost.IsRuntimeAvailable();
        _ = RdpComAvailability.IsMsRdpClient9Registered();
    }
}
