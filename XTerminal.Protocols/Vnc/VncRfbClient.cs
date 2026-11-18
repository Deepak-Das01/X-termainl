using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using XTerminal.Core.Models.Desktop;

namespace XTerminal.Protocols.Vnc;

public sealed class VncFramebuffer
{
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>BGRA32 pixels, length Width*Height*4.</summary>
    public byte[] Pixels { get; init; } = Array.Empty<byte>();
    /// <summary>Connection generation — discard frames from previous sessions.</summary>
    public long Generation { get; init; }
}

/// <summary>
/// Bounded RFB 3.3/3.7/3.8 client. Supported encodings: Raw, CopyRect.
/// Classic VNC Auth is not transport encryption. Native VeNCrypt/TLS is not implemented.
/// </summary>
public sealed class VncRfbClient : IAsyncDisposable
{
    private TcpClient? _client;
    private Stream? _stream;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private int _disposed;
    private int _width;
    private int _height;
    private byte[] _pixels = Array.Empty<byte>();
    private bool _readOnly;
    private long _generation;
    private readonly object _pixelGate = new();

    public string NegotiatedVersion { get; private set; } = "";
    public uint NegotiatedSecurityType { get; private set; }
    public int FramebufferWidth => _width;
    public int FramebufferHeight => _height;
    public long Generation => Interlocked.Read(ref _generation);
    public bool IsConnected => _stream is not null && _disposed == 0;

    public event Action<VncFramebuffer>? FrameUpdated;
    public event Action? Disconnected;
    public event Action<string>? ProtocolError;

    public async Task ConnectAsync(
        string host,
        int port,
        VncSessionProfile profile,
        string? password,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        await DisconnectCoreAsync().ConfigureAwait(false);

        _readOnly = profile.ReadOnly;
        var client = new TcpClient();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(profile.ConnectionTimeoutSeconds));
        try
        {
            await client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            stream.ReadTimeout = profile.ConnectionTimeoutSeconds * 1000;
            stream.WriteTimeout = profile.ConnectionTimeoutSeconds * 1000;
            await HandshakeAsync(stream, profile, password, linked.Token).ConfigureAwait(false);
            Interlocked.Increment(ref _generation);
            _client = client;
            _stream = stream;
            client = null;
            _readCts = new CancellationTokenSource();
            var gen = Generation;
            _readTask = Task.Run(() => ReadLoopAsync(gen, _readCts.Token));
            await RequestFramebufferUpdateAsync(incremental: false).ConfigureAwait(false);
        }
        catch
        {
            try { client?.Dispose(); } catch { /* ignore */ }
            throw;
        }
    }

    /// <summary>Test/helper entry: run handshake on an existing stream (e.g. loopback fixture).</summary>
    public async Task ConnectOnStreamAsync(
        Stream stream,
        VncSessionProfile profile,
        string? password,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        await DisconnectCoreAsync().ConfigureAwait(false);
        _readOnly = profile.ReadOnly;
        await HandshakeAsync(stream, profile, password, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _generation);
        _stream = stream;
        _readCts = new CancellationTokenSource();
        var gen = Generation;
        _readTask = Task.Run(() => ReadLoopAsync(gen, _readCts.Token));
        await RequestFramebufferUpdateAsync(incremental: false).ConfigureAwait(false);
    }

    public async Task DisconnectAsync() => await DisconnectCoreAsync().ConfigureAwait(false);

    public void SendKey(uint keysym, bool down)
    {
        if (_disposed != 0 || _readOnly || _stream is null) return;
        Span<byte> msg = stackalloc byte[8];
        msg[0] = VncClientMessage.KeyEvent;
        msg[1] = down ? (byte)1 : (byte)0;
        msg[2] = 0; msg[3] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(msg[4..], keysym);
        try { _stream.Write(msg); } catch { /* ignore */ }
    }

    public void SendPointer(int x, int y, byte buttonMask)
    {
        if (_disposed != 0 || _readOnly || _stream is null) return;
        x = Math.Clamp(x, 0, Math.Max(0, _width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, _height - 1));
        Span<byte> msg = stackalloc byte[6];
        msg[0] = VncClientMessage.PointerEvent;
        msg[1] = buttonMask;
        BinaryPrimitives.WriteUInt16BigEndian(msg[2..], (ushort)x);
        BinaryPrimitives.WriteUInt16BigEndian(msg[4..], (ushort)y);
        try { _stream.Write(msg); } catch { /* ignore */ }
    }

    public byte[] SnapshotPixels()
    {
        lock (_pixelGate)
        {
            var copy = new byte[_pixels.Length];
            Buffer.BlockCopy(_pixels, 0, copy, 0, _pixels.Length);
            return copy;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await DisconnectCoreAsync().ConfigureAwait(false);
    }

    private async Task HandshakeAsync(Stream stream, VncSessionProfile profile, string? password, CancellationToken ct)
    {
        var serverVersion = await ReadExactAsync(stream, 12, ct).ConfigureAwait(false);
        var ver = Encoding.ASCII.GetString(serverVersion);
        if (!ver.StartsWith("RFB ", StringComparison.Ordinal))
            throw new InvalidOperationException("Not an RFB server.");

        var clientVer = ver.StartsWith("RFB 003.003", StringComparison.Ordinal) ? "RFB 003.003\n"
            : ver.StartsWith("RFB 003.007", StringComparison.Ordinal) ? "RFB 003.007\n"
            : "RFB 003.008\n";
        NegotiatedVersion = clientVer.Trim();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(clientVer), ct).ConfigureAwait(false);

        if (clientVer.StartsWith("RFB 003.003", StringComparison.Ordinal))
        {
            var sec = await ReadExactAsync(stream, 4, ct).ConfigureAwait(false);
            NegotiatedSecurityType = BinaryPrimitives.ReadUInt32BigEndian(sec);
        }
        else
        {
            var countBuf = await ReadExactAsync(stream, 1, ct).ConfigureAwait(false);
            var count = countBuf[0];
            if (count == 0)
            {
                var reason = await ReadReasonAsync(stream, ct).ConfigureAwait(false);
                throw new InvalidOperationException("VNC connection rejected: " + reason);
            }

            var types = await ReadExactAsync(stream, count, ct).ConfigureAwait(false);
            NegotiatedSecurityType = SelectSecurity(types, profile);
            await stream.WriteAsync(new[] { (byte)NegotiatedSecurityType }, ct).ConfigureAwait(false);
        }

        await AuthenticateAsync(stream, NegotiatedSecurityType, profile, password, ct).ConfigureAwait(false);

        if (!clientVer.StartsWith("RFB 003.003", StringComparison.Ordinal) ||
            NegotiatedSecurityType != VncSecurityType.None)
        {
            if (NegotiatedSecurityType != VncSecurityType.None ||
                !clientVer.StartsWith("RFB 003.003", StringComparison.Ordinal))
            {
                var result = await ReadExactAsync(stream, 4, ct).ConfigureAwait(false);
                var code = BinaryPrimitives.ReadUInt32BigEndian(result);
                if (code != 0)
                {
                    var reason = clientVer.StartsWith("RFB 003.008", StringComparison.Ordinal)
                        ? await ReadReasonAsync(stream, ct).ConfigureAwait(false)
                        : "Authentication failed.";
                    throw new InvalidOperationException(reason);
                }
            }
        }

        await stream.WriteAsync(new byte[] { 1 }, ct).ConfigureAwait(false);
        var init = await ReadExactAsync(stream, 24, ct).ConfigureAwait(false);
        _width = BinaryPrimitives.ReadUInt16BigEndian(init.AsSpan(0, 2));
        _height = BinaryPrimitives.ReadUInt16BigEndian(init.AsSpan(2, 2));
        ValidateDimensions(_width, _height);

        // Server pixel format (16 bytes) — we request our own format next; reject absurd bpp.
        var bitsPerPixel = init[4];
        if (bitsPerPixel is not (8 or 16 or 32))
            throw new InvalidOperationException("Unsupported server bits-per-pixel.");

        var nameLen = BinaryPrimitives.ReadUInt32BigEndian(init.AsSpan(20, 4));
        if (nameLen > VncLimits.MaxDesktopNameBytes)
            throw new InvalidOperationException("Desktop name too large.");
        if (nameLen > 0)
            _ = await ReadExactAsync(stream, (int)nameLen, ct).ConfigureAwait(false);

        lock (_pixelGate)
            _pixels = new byte[checked(_width * _height * 4)];

        await SendPixelFormatAsync(stream, ct).ConfigureAwait(false);
        await SendEncodingsAsync(stream, ct).ConfigureAwait(false);
    }

    private static uint SelectSecurity(byte[] types, VncSessionProfile profile)
    {
        var set = new HashSet<byte>(types);
        if (profile.AuthenticationMode == VncAuthenticationMode.VncAuth && set.Contains((byte)VncSecurityType.VncAuth))
            return VncSecurityType.VncAuth;
        if (profile.AuthenticationMode == VncAuthenticationMode.None &&
            profile.AllowUnencrypted &&
            set.Contains((byte)VncSecurityType.None))
            return VncSecurityType.None;
        if (set.Contains((byte)VncSecurityType.VncAuth))
            return VncSecurityType.VncAuth;
        if (profile.AllowUnencrypted && set.Contains((byte)VncSecurityType.None))
            return VncSecurityType.None;
        throw new InvalidOperationException("No mutually supported VNC security type.");
    }

    private static async Task AuthenticateAsync(
        Stream stream, uint security, VncSessionProfile profile, string? password, CancellationToken ct)
    {
        if (security == VncSecurityType.None)
        {
            if (!profile.AllowUnencrypted)
                throw new InvalidOperationException("Unauthenticated VNC is not permitted without acknowledgment.");
            return;
        }

        if (security != VncSecurityType.VncAuth)
            throw new InvalidOperationException($"Unsupported VNC security type {security}.");

        var challenge = await ReadExactAsync(stream, 16, ct).ConfigureAwait(false);
        var response = VncDesAuth.EncryptChallenge(challenge, password ?? "");
        await stream.WriteAsync(response, ct).ConfigureAwait(false);
    }

    private static async Task SendPixelFormatAsync(Stream stream, CancellationToken ct)
    {
        var msg = new byte[20];
        msg[0] = VncClientMessage.SetPixelFormat;
        msg[4] = 32;
        msg[5] = 24;
        msg[6] = 0;
        msg[7] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(8), 255);
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(10), 255);
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(12), 255);
        msg[14] = 16;
        msg[15] = 8;
        msg[16] = 0;
        await stream.WriteAsync(msg, ct).ConfigureAwait(false);
    }

    private static async Task SendEncodingsAsync(Stream stream, CancellationToken ct)
    {
        var encodings = new[] { VncEncoding.CopyRect, VncEncoding.Raw };
        var msg = new byte[4 + encodings.Length * 4];
        msg[0] = VncClientMessage.SetEncodings;
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(2), (ushort)encodings.Length);
        for (var i = 0; i < encodings.Length; i++)
            BinaryPrimitives.WriteInt32BigEndian(msg.AsSpan(4 + i * 4), encodings[i]);
        await stream.WriteAsync(msg, ct).ConfigureAwait(false);
    }

    private async Task RequestFramebufferUpdateAsync(bool incremental)
    {
        var stream = _stream;
        if (stream is null || _disposed != 0) return;
        var msg = new byte[10];
        msg[0] = VncClientMessage.FramebufferUpdateRequest;
        msg[1] = incremental ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(6), (ushort)_width);
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(8), (ushort)_height);
        await stream.WriteAsync(msg).ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(long generation, CancellationToken token)
    {
        var stream = _stream;
        if (stream is null) return;
        try
        {
            while (!token.IsCancellationRequested && _disposed == 0 && Generation == generation)
            {
                var header = await ReadExactAsync(stream, 1, token).ConfigureAwait(false);
                switch (header[0])
                {
                    case VncServerMessage.FramebufferUpdate:
                        await HandleFramebufferUpdateAsync(stream, generation, token).ConfigureAwait(false);
                        if (Generation == generation && _disposed == 0)
                            await RequestFramebufferUpdateAsync(incremental: true).ConfigureAwait(false);
                        break;
                    case VncServerMessage.SetColourMapEntries:
                        await SkipColourMapAsync(stream, token).ConfigureAwait(false);
                        break;
                    case VncServerMessage.Bell:
                        break;
                    case VncServerMessage.ServerCutText:
                        await SkipCutTextAsync(stream, token).ConfigureAwait(false);
                        break;
                    default:
                        ProtocolError?.Invoke($"Unknown VNC server message {header[0]}.");
                        return;
                }
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (IOException) { /* disconnect */ }
        catch (Exception ex)
        {
            if (_disposed == 0 && Generation == generation)
                ProtocolError?.Invoke(ex.Message);
        }
        finally
        {
            if (Generation == generation)
                Disconnected?.Invoke();
        }
    }

    private async Task HandleFramebufferUpdateAsync(Stream stream, long generation, CancellationToken ct)
    {
        var hdr = await ReadExactAsync(stream, 3, ct).ConfigureAwait(false);
        var rects = BinaryPrimitives.ReadUInt16BigEndian(hdr.AsSpan(1, 2));
        if (rects > VncLimits.MaxRectanglesPerUpdate)
            throw new InvalidOperationException("Too many framebuffer rectangles.");

        for (var i = 0; i < rects; i++)
        {
            var rh = await ReadExactAsync(stream, 12, ct).ConfigureAwait(false);
            var x = BinaryPrimitives.ReadUInt16BigEndian(rh.AsSpan(0, 2));
            var y = BinaryPrimitives.ReadUInt16BigEndian(rh.AsSpan(2, 2));
            var w = BinaryPrimitives.ReadUInt16BigEndian(rh.AsSpan(4, 2));
            var h = BinaryPrimitives.ReadUInt16BigEndian(rh.AsSpan(6, 2));
            var enc = BinaryPrimitives.ReadInt32BigEndian(rh.AsSpan(8, 4));
            ValidateRect(x, y, w, h);

            if (enc == VncEncoding.Raw)
                await DecodeRawAsync(stream, x, y, w, h, ct).ConfigureAwait(false);
            else if (enc == VncEncoding.CopyRect)
                await DecodeCopyRectAsync(stream, x, y, w, h, ct).ConfigureAwait(false);
            else
                throw new InvalidOperationException($"Unsupported VNC encoding {enc}.");
        }

        if (Generation != generation || _disposed != 0)
            return;

        VncFramebuffer frame;
        lock (_pixelGate)
        {
            frame = new VncFramebuffer
            {
                Width = _width,
                Height = _height,
                Pixels = SnapshotPixelsUnlocked(),
                Generation = generation
            };
        }
        FrameUpdated?.Invoke(frame);
    }

    private async Task DecodeRawAsync(Stream stream, int x, int y, int w, int h, CancellationToken ct)
    {
        var byteCount = checked(w * h * 4);
        if (byteCount > VncLimits.MaxMessageBytes)
            throw new InvalidOperationException("Raw rectangle too large.");
        var data = await ReadExactAsync(stream, byteCount, ct).ConfigureAwait(false);
        lock (_pixelGate)
        {
            for (var row = 0; row < h; row++)
            {
                var src = row * w * 4;
                var dst = checked(((y + row) * _width + x) * 4);
                Buffer.BlockCopy(data, src, _pixels, dst, w * 4);
            }
        }
    }

    private async Task DecodeCopyRectAsync(Stream stream, int x, int y, int w, int h, CancellationToken ct)
    {
        var srcBuf = await ReadExactAsync(stream, 4, ct).ConfigureAwait(false);
        var srcX = BinaryPrimitives.ReadUInt16BigEndian(srcBuf.AsSpan(0, 2));
        var srcY = BinaryPrimitives.ReadUInt16BigEndian(srcBuf.AsSpan(2, 2));
        ValidateRect(srcX, srcY, w, h);
        lock (_pixelGate)
        {
            var temp = new byte[checked(w * h * 4)];
            for (var row = 0; row < h; row++)
            {
                var src = checked(((srcY + row) * _width + srcX) * 4);
                Buffer.BlockCopy(_pixels, src, temp, row * w * 4, w * 4);
            }
            for (var row = 0; row < h; row++)
            {
                var dst = checked(((y + row) * _width + x) * 4);
                Buffer.BlockCopy(temp, row * w * 4, _pixels, dst, w * 4);
            }
        }
    }

    private static async Task SkipColourMapAsync(Stream stream, CancellationToken ct)
    {
        var hdr = await ReadExactAsync(stream, 5, ct).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadUInt16BigEndian(hdr.AsSpan(3, 2));
        if (count > 4096) throw new InvalidOperationException("Colour map too large.");
        _ = await ReadExactAsync(stream, checked(count * 6), ct).ConfigureAwait(false);
    }

    private static async Task SkipCutTextAsync(Stream stream, CancellationToken ct)
    {
        var hdr = await ReadExactAsync(stream, 7, ct).ConfigureAwait(false);
        var len = BinaryPrimitives.ReadUInt32BigEndian(hdr.AsSpan(3, 4));
        if (len > VncLimits.MaxCutTextBytes) throw new InvalidOperationException("Cut text too large.");
        if (len > 0)
            _ = await ReadExactAsync(stream, (int)len, ct).ConfigureAwait(false);
    }

    private void ValidateRect(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) throw new InvalidOperationException("Invalid rectangle size.");
        if (x < 0 || y < 0) throw new InvalidOperationException("Rectangle outside framebuffer.");
        try
        {
            var right = checked(x + w);
            var bottom = checked(y + h);
            if (right > _width || bottom > _height)
                throw new InvalidOperationException("Rectangle outside framebuffer.");
            if (checked((long)w * h) > VncLimits.MaxRectPixels)
                throw new InvalidOperationException("Rectangle exceeds pixel limit.");
            if (checked((long)w * h * 4) > VncLimits.MaxMessageBytes)
                throw new InvalidOperationException("Rectangle payload exceeds message limit.");
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("Rectangle arithmetic overflow.");
        }
    }

    private static void ValidateDimensions(int w, int h)
    {
        if (w is < 1 or > VncLimits.MaxWidth || h is < 1 or > VncLimits.MaxHeight)
            throw new InvalidOperationException("Invalid framebuffer dimensions.");
        try
        {
            _ = checked(w * h * 4);
            if ((long)w * h > VncLimits.MaxRectPixels)
                throw new InvalidOperationException("Framebuffer exceeds pixel limit.");
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("Framebuffer allocation overflow.");
        }
    }

    private byte[] SnapshotPixelsUnlocked()
    {
        var copy = new byte[_pixels.Length];
        Buffer.BlockCopy(_pixels, 0, copy, 0, _pixels.Length);
        return copy;
    }

    private static async Task<string> ReadReasonAsync(Stream stream, CancellationToken ct)
    {
        var lenBuf = await ReadExactAsync(stream, 4, ct).ConfigureAwait(false);
        var len = BinaryPrimitives.ReadUInt32BigEndian(lenBuf);
        if (len > VncLimits.MaxErrorReasonBytes) return "Connection failed.";
        if (len == 0) return "Connection failed.";
        var bytes = await ReadExactAsync(stream, (int)len, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken ct)
    {
        if (count < 0 || count > VncLimits.MaxMessageBytes)
            throw new InvalidOperationException("Invalid read size.");
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("VNC connection closed.");
            offset += read;
        }
        return buffer;
    }

    private async Task DisconnectCoreAsync()
    {
        Interlocked.Increment(ref _generation);
        try { _readCts?.Cancel(); } catch { /* ignore */ }
        var stream = _stream;
        _stream = null;
        var client = _client;
        _client = null;
        try { stream?.Dispose(); } catch { /* ignore */ }
        try { client?.Dispose(); } catch { /* ignore */ }
        if (_readTask is not null)
        {
            try { await _readTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { /* ignore */ }
            _readTask = null;
        }
        _readCts?.Dispose();
        _readCts = null;
        lock (_pixelGate)
            _pixels = Array.Empty<byte>();
    }
}

/// <summary>VNC authentication DES (bit-reversed key schedule) challenge response.</summary>
public static class VncDesAuth
{
    public static byte[] EncryptChallenge(byte[] challenge, string password)
    {
        if (challenge.Length != 16) throw new ArgumentException("Challenge must be 16 bytes.");
        var key = new byte[8];
        var pwdBytes = Encoding.UTF8.GetBytes(password ?? "");
        for (var i = 0; i < 8; i++)
            key[i] = i < pwdBytes.Length ? ReverseBits(pwdBytes[i]) : (byte)0;

        using var des = DES.Create();
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;
        des.Key = key;
        using var encryptor = des.CreateEncryptor();
        var output = new byte[16];
        encryptor.TransformBlock(challenge, 0, 8, output, 0);
        encryptor.TransformBlock(challenge, 8, 8, output, 8);
        return output;
    }

    private static byte ReverseBits(byte b)
    {
        b = (byte)(((b * 0x0802u & 0x22110u) | (b * 0x8020u & 0x88440u)) * 0x10101u >> 16);
        return b;
    }
}
