using XTerminal.Core.Models.Desktop;
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
    public string? Domain { get; set; }
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

    // Graphical desktop (RDP / VNC) — passwords never stored here.
    public int? DesktopWidth { get; set; }
    public int? DesktopHeight { get; set; }
    public int? ColorDepth { get; set; }
    public bool? SmartSizing { get; set; }
    public bool? Fullscreen { get; set; }
    public bool? RequireNla { get; set; }
    public int? AuthenticationLevel { get; set; }
    public bool? RedirectClipboard { get; set; }
    public bool? RedirectDrives { get; set; }
    public bool? RedirectPrinters { get; set; }
    public bool? RedirectAudio { get; set; }
    public string? VncAuthMode { get; set; }
    public bool? VncReadOnly { get; set; }
    public string? DesktopScaling { get; set; }
    public bool? ShareClipboard { get; set; }
    public bool? AllowUnencryptedVnc { get; set; }
    /// <summary>When true, UI may persist a Credential Manager entry (never the secret itself in JSON).</summary>
    public bool? PersistCredential { get; set; }

    /// <summary>VNC over SSH local forward (application-owned). Never implies encryption solely from Host=localhost.</summary>
    public bool? UseSshTunnel { get; set; }
    public string? VncTunnelRemoteHost { get; set; }
    public int? VncTunnelRemotePort { get; set; }

    public Dictionary<string, string> Extra { get; set; } = new();

    /// <summary>Links to <see cref="Ssh.SshSessionRecord"/> metadata (secrets stay in Credential Manager).</summary>
    public Guid? SshSessionId { get; set; }

    public RdpSessionProfile ToRdpProfile() => new()
    {
        Name = Name,
        Host = Host ?? "",
        Port = Port is > 0 and <= 65535 ? Port.Value : 3389,
        Username = Username ?? "",
        Domain = Domain,
        ConnectionTimeoutSeconds = ConnectionTimeoutSeconds is >= 5 and <= 120 ? ConnectionTimeoutSeconds.Value : 30,
        DesktopWidth = DesktopWidth is >= 640 and <= 8192 ? DesktopWidth.Value : 1280,
        DesktopHeight = DesktopHeight is >= 480 and <= 8192 ? DesktopHeight.Value : 720,
        ColorDepth = ColorDepth is 8 or 15 or 16 or 24 or 32 ? ColorDepth.Value : 32,
        SmartSizing = SmartSizing ?? true,
        Fullscreen = Fullscreen ?? false,
        RequireNla = RequireNla ?? true,
        AuthenticationLevel = AuthenticationLevel is >= 0 and <= 2 ? AuthenticationLevel.Value : 2,
        RedirectClipboard = RedirectClipboard ?? false,
        RedirectDrives = RedirectDrives ?? false,
        RedirectPrinters = RedirectPrinters ?? false,
        RedirectAudio = RedirectAudio ?? false,
        AutoReconnect = AutoReconnect ?? false
    };

    public VncSessionProfile ToVncProfile() => new()
    {
        Name = Name,
        Host = Host ?? "",
        Port = Port is > 0 and <= 65535 ? Port.Value : 5900,
        AuthenticationMode = Enum.TryParse<VncAuthenticationMode>(VncAuthMode, true, out var m)
            ? m
            : Desktop.VncAuthenticationMode.VncAuth,
        ReadOnly = VncReadOnly ?? false,
        Scaling = Enum.TryParse<DesktopScalingMode>(DesktopScaling, true, out var s)
            ? s
            : DesktopScalingMode.FitWindow,
        ConnectionTimeoutSeconds = ConnectionTimeoutSeconds is >= 3 and <= 120 ? ConnectionTimeoutSeconds.Value : 20,
        Fullscreen = Fullscreen ?? false,
        ShareClipboard = ShareClipboard ?? false,
        AutoReconnect = AutoReconnect ?? false,
        AllowUnencrypted = AllowUnencryptedVnc ?? false,
        UseSshTunnel = UseSshTunnel ?? false,
        SshTunnelSessionId = SshSessionId,
        TunnelRemoteHost = string.IsNullOrWhiteSpace(VncTunnelRemoteHost) ? "127.0.0.1" : VncTunnelRemoteHost,
        TunnelRemotePort = VncTunnelRemotePort is > 0 and <= 65535 ? VncTunnelRemotePort.Value : (Port is > 0 and <= 65535 ? Port.Value : 5900)
    };

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
