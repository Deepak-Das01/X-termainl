using XTerminal.Core.Models.Serial;
using XTerminal.Core.Services;

namespace XTerminal.Tests;

public class SerialProfileValidatorTests
{
    private static SerialSessionProfile Valid() => new()
    {
        Name = "Test",
        PortName = "COM3",
        BaudRate = 115200,
        DataBits = 8,
        Parity = SerialParityMode.None,
        StopBits = SerialStopBitsMode.One,
        FlowControl = SerialFlowControl.None
    };

    [Fact]
    public void Accepts_common_baud_rates()
    {
        foreach (var baud in new[] { 9600, 19200, 38400, 57600, 115200, 230400 })
        {
            var p = Valid();
            p.BaudRate = baud;
            Assert.True(SerialProfileValidator.TryValidate(p, out _));
        }
    }

    [Fact]
    public void Rejects_missing_port()
    {
        var p = Valid();
        p.PortName = "";
        Assert.False(SerialProfileValidator.TryValidate(p, out var error));
        Assert.Contains("COM port", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_invalid_port_shape()
    {
        var p = Valid();
        p.PortName = "USB1";
        Assert.False(SerialProfileValidator.TryValidate(p, out _));
    }

    [Fact]
    public void Rejects_invalid_data_bits()
    {
        var p = Valid();
        p.DataBits = 9;
        Assert.False(SerialProfileValidator.TryValidate(p, out var error));
        Assert.Contains("Data bits", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_invalid_stop_bits_enum()
    {
        var p = Valid();
        p.StopBits = (SerialStopBitsMode)99;
        Assert.False(SerialProfileValidator.TryValidate(p, out _));
    }

    [Fact]
    public void Rejects_invalid_flow_control()
    {
        var p = Valid();
        p.FlowControl = (SerialFlowControl)42;
        Assert.False(SerialProfileValidator.TryValidate(p, out _));
    }

    [Fact]
    public void Rejects_out_of_range_baud()
    {
        var p = Valid();
        p.BaudRate = 50;
        Assert.False(SerialProfileValidator.TryValidate(p, out _));
    }

    [Fact]
    public void Rejects_unknown_encoding()
    {
        var p = Valid();
        p.EncodingName = "not-a-real-encoding-xyz";
        Assert.False(SerialProfileValidator.TryValidate(p, out var error));
        Assert.Contains("encoding", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Does_not_silently_substitute_invalid_values()
    {
        var p = Valid();
        p.DataBits = 4;
        Assert.False(SerialProfileValidator.TryValidate(p, out _));
    }
}
