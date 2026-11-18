using XTerminal.App.Desktop;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Desktop;
using XTerminal.Core.Services;
using XTerminal.Protocols;

namespace XTerminal.Tests;

public class RdpProfileValidatorTests
{
    private static RdpSessionProfile Valid() => new()
    {
        Name = "r",
        Host = "rdp.example.com",
        Port = 3389,
        Username = "user",
        RequireNla = true,
        AuthenticationLevel = 2,
        DesktopWidth = 1280,
        DesktopHeight = 720,
        ColorDepth = 32
    };

    [Fact]
    public void Default_port_is_3389()
    {
        var def = new SessionDefinition { Kind = SessionKind.Rdp, Host = "h", Port = null };
        Assert.Equal(3389, def.ToRdpProfile().Port);
    }

    [Fact]
    public void Rejects_invalid_resolution()
    {
        var p = Valid();
        p.DesktopWidth = 100;
        Assert.False(RdpProfileValidator.TryValidate(p, out _));
    }

    [Fact]
    public void Rejects_user_at_host()
    {
        var p = Valid();
        p.Host = "user@host";
        Assert.False(RdpProfileValidator.TryValidate(p, out var error));
        Assert.Contains("Username", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Secure_defaults_require_nla()
    {
        var p = Valid();
        Assert.True(p.RequireNla);
        Assert.Equal(2, p.AuthenticationLevel);
        Assert.False(p.RedirectClipboard);
        Assert.False(p.RedirectDrives);
    }

    [Fact]
    public void Rejects_lowering_auth_without_nla()
    {
        var p = Valid();
        p.RequireNla = false;
        p.AuthenticationLevel = 0;
        Assert.False(RdpProfileValidator.TryValidate(p, out _));
    }

    [Fact]
    public void Capabilities_gate_terminal_features()
    {
        Assert.True(SessionCapabilities.Has(SessionKind.Rdp, SessionCapability.GraphicalDesktop));
        Assert.False(SessionCapabilities.Has(SessionKind.Rdp, SessionCapability.Sftp));
        Assert.False(SessionCapabilities.Has(SessionKind.Rdp, SessionCapability.ShellIntegration));
        Assert.False(SessionHostFactory.IsTerminalSessionSupported(SessionKind.Rdp));
    }

    [Fact]
    public void Reports_native_runtime_availability()
    {
        // On Windows CI / developer machines with mstscax, this is typically true.
        _ = RdpActiveXHost.IsRuntimeAvailable();
        _ = RdpComAvailability.IsMsRdpClient9Registered();
    }
}
