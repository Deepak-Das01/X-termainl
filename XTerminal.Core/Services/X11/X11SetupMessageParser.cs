using System.Text;

namespace XTerminal.Core.Services.X11;

/// <summary>
/// Bounded parser for the initial X11 connection setup (authorization fields).
/// Used for protocol-prep / regression tests; not live forwarding in Phase 4C.
/// </summary>
public static class X11SetupMessageParser
{
    public const int MaxAuthProtoNameBytes = 256;
    public const int MaxAuthDataBytes = 256;
    public const int MaxSetupPrefixBytes = 12 + MaxAuthProtoNameBytes + 3 + MaxAuthDataBytes + 3;

    public readonly struct SetupHeader
    {
        public required byte ByteOrder { get; init; } // 'B' or 'l'
        public required ushort MajorProtocol { get; init; }
        public required ushort MinorProtocol { get; init; }
        public required ushort AuthProtoNameLen { get; init; }
        public required ushort AuthProtoDataLen { get; init; }
        public required int HeaderBytesConsumed { get; init; }
    }

    public static bool TryParseHeader(ReadOnlySpan<byte> buffer, out SetupHeader header, out string error)
    {
        header = default;
        error = "";
        if (buffer.Length < 12)
        {
            error = "Incomplete X11 setup header.";
            return false;
        }

        var order = buffer[0];
        if (order is not ((byte)'B' or (byte)'l'))
        {
            error = "Invalid X11 byte order.";
            return false;
        }

        var big = order == (byte)'B';
        var major = ReadUInt16(buffer.Slice(2, 2), big);
        var minor = ReadUInt16(buffer.Slice(4, 2), big);
        var nameLen = ReadUInt16(buffer.Slice(6, 2), big);
        var dataLen = ReadUInt16(buffer.Slice(8, 2), big);

        if (nameLen > MaxAuthProtoNameBytes)
        {
            error = "Authorization protocol name too large.";
            return false;
        }

        if (dataLen > MaxAuthDataBytes)
        {
            error = "Authorization data too large.";
            return false;
        }

        var paddedName = Pad4(nameLen);
        var paddedData = Pad4(dataLen);
        var total = checked(12 + paddedName + paddedData);
        if (total > MaxSetupPrefixBytes)
        {
            error = "Setup message exceeds bound.";
            return false;
        }

        header = new SetupHeader
        {
            ByteOrder = order,
            MajorProtocol = major,
            MinorProtocol = minor,
            AuthProtoNameLen = nameLen,
            AuthProtoDataLen = dataLen,
            HeaderBytesConsumed = 12
        };
        return true;
    }

    public static bool TryExtractAuthCookie(
        ReadOnlySpan<byte> buffer,
        SetupHeader header,
        out byte[] cookie,
        out string error)
    {
        cookie = Array.Empty<byte>();
        error = "";
        var paddedName = Pad4(header.AuthProtoNameLen);
        var need = 12 + paddedName + header.AuthProtoDataLen;
        if (buffer.Length < need)
        {
            error = "Fragmented X11 setup message.";
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(X11CookieMaterial.MitMagicCookie1);
        if (header.AuthProtoNameLen != expected.Length)
        {
            error = "Unsupported X11 authorization protocol.";
            return false;
        }

        var name = buffer.Slice(12, header.AuthProtoNameLen);
        if (!name.SequenceEqual(expected))
        {
            error = "Unsupported X11 authorization protocol.";
            return false;
        }

        if (header.AuthProtoDataLen != X11CookieMaterial.CookieLength)
        {
            error = "Unexpected cookie length.";
            return false;
        }

        cookie = buffer.Slice(12 + paddedName, header.AuthProtoDataLen).ToArray();
        return true;
    }

    /// <summary>Reject unsolicited x11 channel opens when forwarding is not authorized for the session.</summary>
    public static bool ShouldAcceptIncomingX11Channel(bool forwardingAuthorized, bool sessionOpen) =>
        forwardingAuthorized && sessionOpen;

    public static byte[] BuildSetupPrefix(
        bool bigEndian,
        ushort major,
        ushort minor,
        string authProtoName,
        ReadOnlySpan<byte> authData)
    {
        var nameBytes = Encoding.ASCII.GetBytes(authProtoName);
        if (nameBytes.Length > MaxAuthProtoNameBytes || authData.Length > MaxAuthDataBytes)
            throw new ArgumentOutOfRangeException(nameof(authData), "Auth fields exceed bounds.");

        var paddedName = Pad4(nameBytes.Length);
        var paddedData = Pad4(authData.Length);
        var buffer = new byte[12 + paddedName + paddedData];
        buffer[0] = bigEndian ? (byte)'B' : (byte)'l';
        WriteUInt16(buffer.AsSpan(2, 2), major, bigEndian);
        WriteUInt16(buffer.AsSpan(4, 2), minor, bigEndian);
        WriteUInt16(buffer.AsSpan(6, 2), (ushort)nameBytes.Length, bigEndian);
        WriteUInt16(buffer.AsSpan(8, 2), (ushort)authData.Length, bigEndian);
        nameBytes.CopyTo(buffer.AsSpan(12));
        authData.CopyTo(buffer.AsSpan(12 + paddedName));
        return buffer;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> span, bool bigEndian) =>
        bigEndian
            ? (ushort)((span[0] << 8) | span[1])
            : (ushort)(span[0] | (span[1] << 8));

    private static void WriteUInt16(Span<byte> span, ushort value, bool bigEndian)
    {
        if (bigEndian)
        {
            span[0] = (byte)(value >> 8);
            span[1] = (byte)value;
        }
        else
        {
            span[0] = (byte)value;
            span[1] = (byte)(value >> 8);
        }
    }

    private static int Pad4(int n) => (n + 3) & ~3;
}
