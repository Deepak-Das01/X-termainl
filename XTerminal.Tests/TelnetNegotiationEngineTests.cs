using XTerminal.Core.Models;
using XTerminal.Protocols.Telnet;

namespace XTerminal.Tests;

public class TelnetNegotiationEngineTests
{
    [Fact]
    public void Escapes_IAC_in_application_data()
    {
        var raw = new byte[] { 0x41, 0xFF, 0x42 };
        var escaped = TelnetNegotiationEngine.EscapeApplicationData(raw);
        Assert.Equal(new byte[] { 0x41, 0xFF, 0xFF, 0x42 }, escaped);
    }

    [Fact]
    public void Unescapes_doubled_IAC_as_data()
    {
        var engine = new TelnetNegotiationEngine();
        var replies = new List<byte>();
        var app = engine.Push(new byte[] { 0x41, 0xFF, 0xFF, 0x42 }, replies);
        Assert.Equal(new byte[] { 0x41, 0xFF, 0x42 }, app);
        Assert.Empty(replies);
    }

    [Fact]
    public void Handles_fragmented_WILL()
    {
        var engine = new TelnetNegotiationEngine();
        var replies = new List<byte>();
        Assert.Empty(engine.Push(new byte[] { TelnetCommand.Iac }, replies));
        Assert.Empty(engine.Push(new byte[] { TelnetCommand.Will }, replies));
        var app = engine.Push(new byte[] { TelnetOption.Echo, (byte)'H', (byte)'i' }, replies);
        Assert.Equal(new byte[] { (byte)'H', (byte)'i' }, app);
        Assert.Contains(TelnetCommand.Do, replies);
        Assert.True(engine.RemoteEcho);
    }

    [Fact]
    public void Rejects_unsupported_option()
    {
        var engine = new TelnetNegotiationEngine();
        var replies = new List<byte>();
        engine.Push(new byte[] { TelnetCommand.Iac, TelnetCommand.Will, 99 }, replies);
        Assert.Equal(new byte[] { TelnetCommand.Iac, TelnetCommand.Dont, 99 }, replies.ToArray());
    }

    [Fact]
    public void Prevents_negotiation_loops()
    {
        var engine = new TelnetNegotiationEngine();
        for (var i = 0; i < TelnetNegotiationEngine.MaxNegotiationsPerOption + 2; i++)
        {
            var replies = new List<byte>();
            engine.Push(new byte[] { TelnetCommand.Iac, TelnetCommand.Will, TelnetOption.Echo }, replies);
        }

        var final = new List<byte>();
        engine.Push(new byte[] { TelnetCommand.Iac, TelnetCommand.Will, TelnetOption.Echo }, final);
        Assert.Equal(new byte[] { TelnetCommand.Iac, TelnetCommand.Dont, TelnetOption.Echo }, final.ToArray());
    }

    [Fact]
    public void Terminal_type_subnegotiation()
    {
        var engine = new TelnetNegotiationEngine();
        engine.SetTerminalType("xterm-256color");
        var replies = new List<byte>();
        engine.Push(new byte[] { TelnetCommand.Iac, TelnetCommand.Do, TelnetOption.TerminalType }, replies);
        replies.Clear();
        // SB TERMINAL-TYPE SEND SE
        engine.Push(new byte[]
        {
            TelnetCommand.Iac, TelnetCommand.SubNegotiation, TelnetOption.TerminalType, 1,
            TelnetCommand.Iac, TelnetCommand.SubNegotiationEnd
        }, replies);
        Assert.Contains((byte)'x', replies);
        Assert.Contains(TelnetCommand.SubNegotiation, replies);
    }

    [Fact]
    public void NAWS_on_DO()
    {
        var engine = new TelnetNegotiationEngine();
        engine.SetTerminalSize(120, 40);
        var replies = new List<byte>();
        engine.Push(new byte[] { TelnetCommand.Iac, TelnetCommand.Do, TelnetOption.Naws }, replies);
        Assert.Contains(TelnetOption.Naws, replies);
        // width 120 = 0x00 0x78
        Assert.Contains((byte)0x78, replies);
    }

    [Fact]
    public void Bounds_oversized_subnegotiation()
    {
        var engine = new TelnetNegotiationEngine();
        var replies = new List<byte>();
        var buf = new List<byte> { TelnetCommand.Iac, TelnetCommand.SubNegotiation, TelnetOption.TerminalType };
        for (var i = 0; i < TelnetNegotiationEngine.MaxSubnegotiationBytes + 50; i++)
            buf.Add(0x41);
        buf.Add(TelnetCommand.Iac);
        buf.Add(TelnetCommand.SubNegotiationEnd);
        var app = engine.Push(buf.ToArray(), replies);
        Assert.Empty(app); // negotiation bytes never leak as app data
    }

    [Fact]
    public void Interleaves_app_data_with_negotiation()
    {
        var engine = new TelnetNegotiationEngine();
        var replies = new List<byte>();
        var app = engine.Push(new byte[]
        {
            (byte)'A',
            TelnetCommand.Iac, TelnetCommand.Will, TelnetOption.SuppressGoAhead,
            (byte)'B'
        }, replies);
        Assert.Equal(new byte[] { (byte)'A', (byte)'B' }, app);
    }

    [Fact]
    public void Security_policy_host_key_is_per_host_port()
    {
        var a = Core.Services.TelnetSecurityPolicy.HostKey("Example.COM", 23);
        var b = Core.Services.TelnetSecurityPolicy.HostKey("example.com", 23);
        var c = Core.Services.TelnetSecurityPolicy.HostKey("example.com", 2323);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Telnet_profile_validator_rejects_user_at_host()
    {
        Assert.False(Core.Services.TelnetProfileValidator.TryValidate("user@host", 23, 20, out var error));
        Assert.Contains("username@", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Telnet_capabilities_mark_plaintext()
    {
        Assert.True(SessionCapabilities.Has(SessionKind.Telnet, SessionCapability.PlaintextNetwork));
        Assert.False(SessionCapabilities.Has(SessionKind.Telnet, SessionCapability.Sftp));
        Assert.False(SessionCapabilities.Has(SessionKind.Telnet, SessionCapability.PortForwarding));
    }
}
