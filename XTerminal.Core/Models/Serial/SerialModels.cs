namespace XTerminal.Core.Models.Serial;

public enum SerialParityMode
{
    None = 0,
    Odd = 1,
    Even = 2,
    Mark = 3,
    Space = 4
}

public enum SerialStopBitsMode
{
    One = 1,
    OnePointFive = 3,
    Two = 2
}

public enum SerialFlowControl
{
    None = 0,
    RtsCts = 1,
    XonXoff = 2,
    RtsCtsAndXonXoff = 3
}

public enum SerialLineEnding
{
    Cr = 0,
    Lf = 1,
    CrLf = 2
}

/// <summary>Validated serial profile parameters (not secrets).</summary>
public sealed class SerialSessionProfile
{
    public string Name { get; set; } = "Serial";
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public SerialParityMode Parity { get; set; } = SerialParityMode.None;
    public SerialStopBitsMode StopBits { get; set; } = SerialStopBitsMode.One;
    public SerialFlowControl FlowControl { get; set; } = SerialFlowControl.None;
    public bool DtrEnable { get; set; } = true;
    public bool RtsEnable { get; set; } = true;
    public int ReadTimeoutMs { get; set; } = 500;
    public int WriteTimeoutMs { get; set; } = 500;
    public string EncodingName { get; set; } = "utf-8";
    public SerialLineEnding LineEnding { get; set; } = SerialLineEnding.Lf;
    public bool LocalEcho { get; set; }
    public bool AutoReconnect { get; set; }
}

public sealed class SerialPortInfo
{
    public required string PortName { get; init; }
    public string? FriendlyName { get; init; }
    public bool IsAvailable { get; init; } = true;
}
