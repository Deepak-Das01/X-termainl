using XTerminal.Core.Models;
using XTerminal.Core.Security;
using XTerminal.Core.Services;
using XTerminal.Protocols;
using XTerminal.Protocols.Serial;
using XTerminal.Protocols.Ssh;

namespace XTerminal.Tests;

public class SerialHexAndCapabilitiesTests
{
    [Theory]
    [InlineData("01 02 FF", new byte[] { 0x01, 0x02, 0xFF })]
    [InlineData("0xDEAD", new byte[] { 0xDE, 0xAD })]
    [InlineData("aa:bb", new byte[] { 0xAA, 0xBB })]
    public void Parses_hex_payloads(string hex, byte[] expected)
    {
        Assert.True(SerialSessionHost.TryParseHex(hex, out var bytes, out _));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("GG")]
    public void Rejects_invalid_hex(string hex)
    {
        Assert.False(SerialSessionHost.TryParseHex(hex, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Serial_capabilities_gate_sftp_and_tunnels()
    {
        Assert.False(SessionCapabilities.Has(SessionKind.Serial, SessionCapability.Sftp));
        Assert.False(SessionCapabilities.Has(SessionKind.Serial, SessionCapability.PortForwarding));
        Assert.False(SessionCapabilities.Has(SessionKind.Serial, SessionCapability.TerminalResize));
        Assert.True(SessionCapabilities.Has(SessionKind.Serial, SessionCapability.ControlSignals));
        Assert.True(SessionCapabilities.Has(SessionKind.Serial, SessionCapability.BinaryData));
        Assert.Contains("do not receive terminal resize",
            SessionCapabilities.UnsupportedReason(SessionKind.Serial, SessionCapability.TerminalResize),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Factory_creates_serial_and_telnet_hosts()
    {
        var factory = new SessionHostFactory(
            new WindowsCredentialStore(),
            new JsonSshSessionLibrary(Path.Combine(Path.GetTempPath(), "xt-ssh-" + Guid.NewGuid().ToString("N") + ".json")),
            new SshHostKeyPolicy(
                new KnownHostsStore(Path.Combine(Path.GetTempPath(), "xt-kh-" + Guid.NewGuid().ToString("N") + ".json")),
                new AlwaysTrustPrompt()));
        var serial = factory.Create(SessionKind.Serial);
        var telnet = factory.Create(SessionKind.Telnet);
        Assert.True(serial.IsImplemented);
        Assert.True(telnet.IsImplemented);
        Assert.Equal(SessionKind.Serial, serial.Kind);
        Assert.Equal(SessionKind.Telnet, telnet.Kind);
        Assert.True(SessionHostFactory.IsTerminalSessionSupported(SessionKind.Serial));
        Assert.True(SessionHostFactory.IsTerminalSessionSupported(SessionKind.Telnet));
        Assert.False(SessionHostFactory.IsTerminalSessionSupported(SessionKind.Rdp));
    }

    private sealed class AlwaysTrustPrompt : ISshHostKeyPrompt
    {
        public HostKeyPromptResult Prompt(HostKeyPromptRequest request) => HostKeyPromptResult.TrustOnce;
    }
}
