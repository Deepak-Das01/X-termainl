namespace XTerminal.Protocols.Telnet;

/// <summary>Telnet option codes we recognize. Unsupported options are rejected.</summary>
public static class TelnetOption
{
    public const byte Binary = 0;
    public const byte Echo = 1;
    public const byte SuppressGoAhead = 3;
    public const byte TerminalType = 24;
    public const byte Naws = 31;

    public static bool IsSupported(byte option) =>
        option is Echo or SuppressGoAhead or TerminalType or Naws;
}

public static class TelnetCommand
{
    public const byte SubNegotiationEnd = 240; // SE
    public const byte NoOperation = 241;
    public const byte DataMark = 242;
    public const byte Break = 243;
    public const byte InterruptProcess = 244;
    public const byte AbortOutput = 245;
    public const byte AreYouThere = 246;
    public const byte EraseCharacter = 247;
    public const byte EraseLine = 248;
    public const byte GoAhead = 249;
    public const byte SubNegotiation = 250; // SB
    public const byte Will = 251;
    public const byte Wont = 252;
    public const byte Do = 253;
    public const byte Dont = 254;
    public const byte Iac = 255;
}
