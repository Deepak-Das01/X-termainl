namespace XTerminal.Protocols.Vnc;

public static class VncSecurityType
{
    public const uint Invalid = 0;
    public const uint None = 1;
    public const uint VncAuth = 2;
}

public static class VncClientMessage
{
    public const byte SetPixelFormat = 0;
    public const byte SetEncodings = 2;
    public const byte FramebufferUpdateRequest = 3;
    public const byte KeyEvent = 4;
    public const byte PointerEvent = 5;
    public const byte ClientCutText = 6;
}

public static class VncServerMessage
{
    public const byte FramebufferUpdate = 0;
    public const byte SetColourMapEntries = 1;
    public const byte Bell = 2;
    public const byte ServerCutText = 3;
}

public static class VncEncoding
{
    public const int Raw = 0;
    public const int CopyRect = 1;
}

public static class VncLimits
{
    public const int MaxWidth = 8192;
    public const int MaxHeight = 8192;
    public const int MaxRectPixels = MaxWidth * MaxHeight;
    public const int MaxMessageBytes = 64 * 1024 * 1024;
}
