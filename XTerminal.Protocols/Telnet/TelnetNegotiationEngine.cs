using System.Text;

namespace XTerminal.Protocols.Telnet;

public enum TelnetOptionSide
{
    Local,
    Remote
}

/// <summary>
/// Incremental Telnet IAC parser and option state machine.
/// Application data is emitted separately from negotiation bytes.
/// </summary>
public sealed class TelnetNegotiationEngine
{
    public const int MaxSubnegotiationBytes = 2048;
    public const int MaxNegotiationsPerOption = 8;

    private enum ParseState
    {
        Data,
        GotIac,
        GotWill,
        GotWont,
        GotDo,
        GotDont,
        GotSb,
        SbData,
        SbGotIac
    }

    private ParseState _state = ParseState.Data;
    private readonly List<byte> _sub = new();
    private readonly Dictionary<byte, int> _negoCounts = new();
    private readonly HashSet<byte> _localEnabled = new();
    private readonly HashSet<byte> _remoteEnabled = new();
    private short _cols = 80;
    private short _rows = 24;
    private string _terminalType = "xterm-256color";

    public IReadOnlyCollection<byte> LocalEnabled => _localEnabled;
    public IReadOnlyCollection<byte> RemoteEnabled => _remoteEnabled;
    public bool RemoteEcho => _remoteEnabled.Contains(TelnetOption.Echo);
    public bool LocalEchoSuppressed => _remoteEnabled.Contains(TelnetOption.Echo);

    public void SetTerminalSize(short cols, short rows)
    {
        _cols = Math.Clamp(cols, (short)1, (short)999);
        _rows = Math.Clamp(rows, (short)1, (short)999);
    }

    public void SetTerminalType(string terminalType)
    {
        if (!string.IsNullOrWhiteSpace(terminalType))
            _terminalType = terminalType.Trim();
    }

    /// <summary>
    /// Feeds inbound socket bytes. Returns application payload (IAC-escaped 0xFF restored).
    /// Negotiation replies are appended to <paramref name="replies"/>.
    /// </summary>
    public byte[] Push(ReadOnlySpan<byte> input, List<byte> replies)
    {
        var app = new List<byte>(input.Length);
        foreach (var b in input)
            Consume(b, app, replies);
        return app.ToArray();
    }

    /// <summary>Build initial client desires (DO SUPPRESS-GO-AHEAD, WILL TERMINAL-TYPE, WILL NAWS).</summary>
    public byte[] BuildInitialNegotiation()
    {
        var buf = new List<byte>();
        AppendCommand(buf, TelnetCommand.Do, TelnetOption.SuppressGoAhead);
        AppendCommand(buf, TelnetCommand.Will, TelnetOption.TerminalType);
        AppendCommand(buf, TelnetCommand.Will, TelnetOption.Naws);
        AppendCommand(buf, TelnetCommand.Do, TelnetOption.Echo);
        return buf.ToArray();
    }

    public byte[] BuildNawsSubnegotiation()
    {
        var buf = new List<byte>
        {
            TelnetCommand.Iac, TelnetCommand.SubNegotiation, TelnetOption.Naws,
            (byte)((_cols >> 8) & 0xFF), (byte)(_cols & 0xFF),
            (byte)((_rows >> 8) & 0xFF), (byte)(_rows & 0xFF),
            TelnetCommand.Iac, TelnetCommand.SubNegotiationEnd
        };
        return buf.ToArray();
    }

    /// <summary>Escape 0xFF in application data for transmission.</summary>
    public static byte[] EscapeApplicationData(ReadOnlySpan<byte> data)
    {
        if (data.IndexOf(TelnetCommand.Iac) < 0)
            return data.ToArray();

        var list = new List<byte>(data.Length + 4);
        foreach (var b in data)
        {
            list.Add(b);
            if (b == TelnetCommand.Iac)
                list.Add(TelnetCommand.Iac);
        }

        return list.ToArray();
    }

    private void Consume(byte b, List<byte> app, List<byte> replies)
    {
        switch (_state)
        {
            case ParseState.Data:
                if (b == TelnetCommand.Iac)
                    _state = ParseState.GotIac;
                else
                    app.Add(b);
                break;

            case ParseState.GotIac:
                switch (b)
                {
                    case TelnetCommand.Iac:
                        app.Add(TelnetCommand.Iac);
                        _state = ParseState.Data;
                        break;
                    case TelnetCommand.Will:
                        _state = ParseState.GotWill;
                        break;
                    case TelnetCommand.Wont:
                        _state = ParseState.GotWont;
                        break;
                    case TelnetCommand.Do:
                        _state = ParseState.GotDo;
                        break;
                    case TelnetCommand.Dont:
                        _state = ParseState.GotDont;
                        break;
                    case TelnetCommand.SubNegotiation:
                        _sub.Clear();
                        _state = ParseState.GotSb;
                        break;
                    case TelnetCommand.NoOperation:
                    case TelnetCommand.DataMark:
                    case TelnetCommand.Break:
                    case TelnetCommand.InterruptProcess:
                    case TelnetCommand.AbortOutput:
                    case TelnetCommand.AreYouThere:
                    case TelnetCommand.EraseCharacter:
                    case TelnetCommand.EraseLine:
                    case TelnetCommand.GoAhead:
                        _state = ParseState.Data;
                        break;
                    default:
                        _state = ParseState.Data;
                        break;
                }
                break;

            case ParseState.GotWill:
                OnWill(b, replies);
                _state = ParseState.Data;
                break;
            case ParseState.GotWont:
                OnWont(b, replies);
                _state = ParseState.Data;
                break;
            case ParseState.GotDo:
                OnDo(b, replies);
                _state = ParseState.Data;
                break;
            case ParseState.GotDont:
                OnDont(b, replies);
                _state = ParseState.Data;
                break;

            case ParseState.GotSb:
                _sub.Add(b);
                _state = ParseState.SbData;
                break;

            case ParseState.SbData:
                if (b == TelnetCommand.Iac)
                    _state = ParseState.SbGotIac;
                else if (_sub.Count < MaxSubnegotiationBytes)
                    _sub.Add(b);
                else
                {
                    // Oversized — discard until SE
                    _state = ParseState.SbData;
                }
                break;

            case ParseState.SbGotIac:
                if (b == TelnetCommand.SubNegotiationEnd)
                {
                    HandleSubnegotiation(replies);
                    _sub.Clear();
                    _state = ParseState.Data;
                }
                else if (b == TelnetCommand.Iac)
                {
                    if (_sub.Count < MaxSubnegotiationBytes)
                        _sub.Add(TelnetCommand.Iac);
                    _state = ParseState.SbData;
                }
                else
                {
                    if (_sub.Count < MaxSubnegotiationBytes)
                        _sub.Add(b);
                    _state = ParseState.SbData;
                }
                break;
        }
    }

    private void OnWill(byte option, List<byte> replies)
    {
        if (!Track(option))
        {
            AppendCommand(replies, TelnetCommand.Dont, option);
            return;
        }

        if (option is TelnetOption.Echo or TelnetOption.SuppressGoAhead or TelnetOption.Binary)
        {
            if (_remoteEnabled.Add(option))
                AppendCommand(replies, TelnetCommand.Do, option);
            return;
        }

        AppendCommand(replies, TelnetCommand.Dont, option);
    }

    private void OnWont(byte option, List<byte> replies)
    {
        Track(option);
        if (_remoteEnabled.Remove(option))
            AppendCommand(replies, TelnetCommand.Dont, option);
    }

    private void OnDo(byte option, List<byte> replies)
    {
        if (!Track(option))
        {
            AppendCommand(replies, TelnetCommand.Wont, option);
            return;
        }

        if (option == TelnetOption.TerminalType)
        {
            if (_localEnabled.Add(option))
                AppendCommand(replies, TelnetCommand.Will, option);
            return;
        }

        if (option == TelnetOption.Naws)
        {
            if (_localEnabled.Add(option))
            {
                AppendCommand(replies, TelnetCommand.Will, option);
                replies.AddRange(BuildNawsSubnegotiation());
            }
            return;
        }

        if (option == TelnetOption.SuppressGoAhead)
        {
            if (_localEnabled.Add(option))
                AppendCommand(replies, TelnetCommand.Will, option);
            return;
        }

        if (option == TelnetOption.Binary)
        {
            if (_localEnabled.Add(option))
                AppendCommand(replies, TelnetCommand.Will, option);
            return;
        }

        // Echo as local client — typically we WONT (server echoes)
        AppendCommand(replies, TelnetCommand.Wont, option);
        _localEnabled.Remove(option);
    }

    private void OnDont(byte option, List<byte> replies)
    {
        Track(option);
        if (_localEnabled.Remove(option))
            AppendCommand(replies, TelnetCommand.Wont, option);
    }

    private void HandleSubnegotiation(List<byte> replies)
    {
        if (_sub.Count < 1) return;
        var option = _sub[0];
        if (option == TelnetOption.TerminalType && _sub.Count >= 2 && _sub[1] == 1)
        {
            // SEND — reply IS <type>
            var typeBytes = Encoding.ASCII.GetBytes(_terminalType);
            replies.Add(TelnetCommand.Iac);
            replies.Add(TelnetCommand.SubNegotiation);
            replies.Add(TelnetOption.TerminalType);
            replies.Add(0); // IS
            replies.AddRange(typeBytes);
            replies.Add(TelnetCommand.Iac);
            replies.Add(TelnetCommand.SubNegotiationEnd);
        }
    }

    private bool Track(byte option)
    {
        _negoCounts.TryGetValue(option, out var n);
        n++;
        _negoCounts[option] = n;
        return n <= MaxNegotiationsPerOption;
    }

    private static void AppendCommand(List<byte> buf, byte command, byte option)
    {
        buf.Add(TelnetCommand.Iac);
        buf.Add(command);
        buf.Add(option);
    }
}
