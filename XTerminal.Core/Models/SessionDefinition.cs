using XTerminal.Core.Models.Serial;

namespace XTerminal.Core.Models;

public sealed class SessionDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New session";
    public SessionKind Kind { get; set; } = SessionKind.LocalShell;
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? Username { get; set; }
    public string? Group { get; set; } = "Sessions";
    public ShellKind Shell { get; set; } = ShellKind.PowerShell;
    public string? CustomCommand { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? SerialPort { get; set; }
    public int? BaudRate { get; set; }
    public int? DataBits { get; set; }
    public string? Parity { get; set; }
    public string? StopBits { get; set; }
    public string? FlowControl { get; set; }
    public bool? DtrEnable { get; set; }
    public bool? RtsEnable { get; set; }
    public int? ReadTimeoutMs { get; set; }
    public int? WriteTimeoutMs { get; set; }
    public string? TextEncoding { get; set; }
    public string? LineEnding { get; set; }
    public bool? LocalEcho { get; set; }
    public bool? AutoReconnect { get; set; }
    public int? ConnectionTimeoutSeconds { get; set; }
    public string? TerminalType { get; set; }
    public Dictionary<string, string> Extra { get; set; } = new();

    /// <summary>Links to <see cref="Ssh.SshSessionRecord"/> metadata (secrets stay in Credential Manager).</summary>
    public Guid? SshSessionId { get; set; }

    public SerialSessionProfile ToSerialProfile() => new()
    {
        Name = Name,
        PortName = SerialPort ?? "",
        BaudRate = BaudRate is > 0 ? BaudRate.Value : 115200,
        DataBits = DataBits is >= 5 and <= 8 ? DataBits.Value : 8,
        Parity = Enum.TryParse<SerialParityMode>(Parity, true, out var p) ? p : SerialParityMode.None,
        StopBits = ParseStopBits(StopBits),
        FlowControl = Enum.TryParse<SerialFlowControl>(FlowControl, true, out var f) ? f : SerialFlowControl.None,
        DtrEnable = DtrEnable ?? true,
        RtsEnable = RtsEnable ?? true,
        ReadTimeoutMs = ReadTimeoutMs is >= 0 ? ReadTimeoutMs.Value : 500,
        WriteTimeoutMs = WriteTimeoutMs is >= 0 ? WriteTimeoutMs.Value : 500,
        EncodingName = string.IsNullOrWhiteSpace(TextEncoding) ? "utf-8" : TextEncoding!,
        LineEnding = Enum.TryParse<SerialLineEnding>(LineEnding, true, out var le) ? le : SerialLineEnding.Lf,
        LocalEcho = LocalEcho ?? false,
        AutoReconnect = AutoReconnect ?? false
    };

    private static SerialStopBitsMode ParseStopBits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return SerialStopBitsMode.One;
        if (value is "1.5" or "OnePointFive") return SerialStopBitsMode.OnePointFive;
        if (value is "2" or "Two") return SerialStopBitsMode.Two;
        return SerialStopBitsMode.One;
    }
}
