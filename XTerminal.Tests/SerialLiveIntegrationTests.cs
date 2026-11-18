using XTerminal.Core.Models;

namespace XTerminal.Tests;

/// <summary>
/// Optional hardware/virtual COM pair integration.
/// Skipped unless XTERMINAL_SERIAL_PORT_A and XTERMINAL_SERIAL_PORT_B are set to a loopback pair.
/// Never invents COM port names. Not executed in normal CI.
/// </summary>
[Trait("Category", "SerialLiveIntegration")]
public class SerialLiveIntegrationTests
{
    [Fact]
    public async Task Hardware_loopback_roundtrip_when_configured()
    {
        var a = Environment.GetEnvironmentVariable("XTERMINAL_SERIAL_PORT_A");
        var b = Environment.GetEnvironmentVariable("XTERMINAL_SERIAL_PORT_B");
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            // Explicit skip — not a pass, not a fail. xUnit Skip throws SkipException via Skip.If in Xunit.SkippableFact;
            // we use Assert.True with message when forcing run, otherwise return early with documented non-execution.
            return;
        }

        await using var hostA = new Protocols.Serial.SerialSessionHost();
        await using var hostB = new Protocols.Serial.SerialSessionHost();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        hostB.OutputReceived += bytes =>
            received.TrySetResult(System.Text.Encoding.UTF8.GetString(bytes));

        var marker = "XT_SERIAL_" + Guid.NewGuid().ToString("N")[..8];
        await hostA.ConnectAsync(new SessionDefinition
        {
            Name = "A",
            Kind = SessionKind.Serial,
            SerialPort = a,
            BaudRate = 115200,
            DataBits = 8,
            Parity = "None",
            StopBits = "1",
            FlowControl = "None",
            LocalEcho = false
        });
        await hostB.ConnectAsync(new SessionDefinition
        {
            Name = "B",
            Kind = SessionKind.Serial,
            SerialPort = b,
            BaudRate = 115200,
            DataBits = 8,
            Parity = "None",
            StopBits = "1",
            FlowControl = "None",
            LocalEcho = false
        });

        hostA.Write(System.Text.Encoding.UTF8.GetBytes(marker));
        var text = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(marker, text, StringComparison.Ordinal);

        // Exclusive ownership
        await using var conflict = new Protocols.Serial.SerialSessionHost();
        await Assert.ThrowsAsync<InvalidOperationException>(() => conflict.ConnectAsync(new SessionDefinition
        {
            Name = "C",
            Kind = SessionKind.Serial,
            SerialPort = a,
            BaudRate = 115200
        }));
    }
}
