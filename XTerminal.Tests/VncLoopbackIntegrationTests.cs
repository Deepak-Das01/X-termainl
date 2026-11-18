using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Desktop;
using XTerminal.Core.Security;
using XTerminal.Protocols.Vnc;

namespace XTerminal.Tests;

[Trait("Category", "VncLoopback")]
public class VncLoopbackIntegrationTests
{
    [Fact]
    public async Task Connects_negotiates_none_and_receives_raw_framebuffer()
    {
        await using var server = await LoopbackVncServer.StartAsync(security: VncSecurityType.None);
        await using var client = new VncRfbClient();
        var frameTcs = new TaskCompletionSource<VncFramebuffer>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.FrameUpdated += f => frameTcs.TrySetResult(f);

        var profile = new VncSessionProfile
        {
            Host = "127.0.0.1",
            Port = server.Port,
            AuthenticationMode = VncAuthenticationMode.None,
            AllowUnencrypted = true,
            ConnectionTimeoutSeconds = 10
        };

        await client.ConnectAsync("127.0.0.1", server.Port, profile, password: null);
        Assert.True(client.IsConnected);
        Assert.Equal(VncSecurityType.None, client.NegotiatedSecurityType);

        var frame = await frameTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(16, frame.Width);
        Assert.Equal(16, frame.Height);
        Assert.Equal(16 * 16 * 4, frame.Pixels.Length);
        // First pixel should be red-ish (0xFF0000FF in BGRA: B=0,G=0,R=255,A=255)
        Assert.Equal(255, frame.Pixels[2]);

        client.SendPointer(1, 1, 1);
        client.SendKey(0x61, true);
        client.SendKey(0x61, false);
        var inputDeadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < inputDeadline && !(server.SawPointer && server.SawKey))
            await Task.Delay(50);
        Assert.True(server.SawPointer);
        Assert.True(server.SawKey);

        await client.DisposeAsync();
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Read_only_suppresses_input()
    {
        await using var server = await LoopbackVncServer.StartAsync(security: VncSecurityType.None);
        await using var client = new VncRfbClient();
        var profile = new VncSessionProfile
        {
            Host = "127.0.0.1",
            Port = server.Port,
            AuthenticationMode = VncAuthenticationMode.None,
            AllowUnencrypted = true,
            ReadOnly = true,
            ConnectionTimeoutSeconds = 10
        };
        await client.ConnectAsync("127.0.0.1", server.Port, profile, null);
        client.SendPointer(2, 2, 1);
        client.SendKey(0x62, true);
        await Task.Delay(200);
        Assert.False(server.SawPointer);
        Assert.False(server.SawKey);
    }

    [Fact]
    public async Task Graphical_session_uses_secret_store()
    {
        await using var server = await LoopbackVncServer.StartAsync(security: VncSecurityType.None);
        var secrets = new MemorySecretStore();
        var id = Guid.NewGuid();
        await using var session = new VncGraphicalSession(secrets);
        await session.ConnectAsync(new SessionDefinition
        {
            Id = id,
            Name = "v",
            Kind = SessionKind.Vnc,
            Host = "127.0.0.1",
            Port = server.Port,
            VncAuthMode = nameof(VncAuthenticationMode.None),
            AllowUnencryptedVnc = true,
            ConnectionTimeoutSeconds = 10
        });
        Assert.True(session.IsConnected);
        await session.DisposeAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Rejects_invalid_framebuffer_from_server()
    {
        await using var server = await LoopbackVncServer.StartAsync(security: VncSecurityType.None, bogusSize: true);
        await using var client = new VncRfbClient();
        var profile = new VncSessionProfile
        {
            Host = "127.0.0.1",
            Port = server.Port,
            AuthenticationMode = VncAuthenticationMode.None,
            AllowUnencrypted = true,
            ConnectionTimeoutSeconds = 5
        };
        await Assert.ThrowsAnyAsync<Exception>(() =>
            client.ConnectAsync("127.0.0.1", server.Port, profile, null));
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _map = new();
        public Task SaveSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
        {
            _map[key] = secret;
            return Task.CompletedTask;
        }
        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_map.TryGetValue(key, out var v) ? v : null);
        public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
        {
            _map.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class LoopbackVncServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _task;
        private readonly uint _security;
        private readonly bool _bogusSize;

        public int Port { get; }
        public bool SawPointer { get; private set; }
        public bool SawKey { get; private set; }

        private LoopbackVncServer(TcpListener listener, uint security, bool bogusSize)
        {
            _listener = listener;
            _security = security;
            _bogusSize = bogusSize;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _task = Task.Run(ServeAsync);
        }

        public static async Task<LoopbackVncServer> StartAsync(uint security, bool bogusSize = false)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var s = new LoopbackVncServer(listener, security, bogusSize);
            await Task.Yield();
            return s;
        }

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                await using var stream = client.GetStream();
                var ver = Encoding.ASCII.GetBytes("RFB 003.008\n");
                await stream.WriteAsync(ver, _cts.Token);

                var clientVer = new byte[12];
                await ReadExact(stream, clientVer, _cts.Token);

                // security types: count=1, type
                await stream.WriteAsync(new byte[] { 1, (byte)_security }, _cts.Token);
                var chosen = new byte[1];
                await ReadExact(stream, chosen, _cts.Token);

                // SecurityResult OK
                await stream.WriteAsync(new byte[] { 0, 0, 0, 0 }, _cts.Token);

                // ClientInit
                var shared = new byte[1];
                await ReadExact(stream, shared, _cts.Token);

                // ServerInit
                var w = _bogusSize ? 20000 : 16;
                var h = _bogusSize ? 20000 : 16;
                var init = new byte[24];
                BinaryPrimitives.WriteUInt16BigEndian(init.AsSpan(0), (ushort)Math.Min(w, 65535));
                BinaryPrimitives.WriteUInt16BigEndian(init.AsSpan(2), (ushort)Math.Min(h, 65535));
                init[4] = 32; init[5] = 24; init[7] = 1;
                BinaryPrimitives.WriteUInt16BigEndian(init.AsSpan(8), 255);
                BinaryPrimitives.WriteUInt16BigEndian(init.AsSpan(10), 255);
                BinaryPrimitives.WriteUInt16BigEndian(init.AsSpan(12), 255);
                init[14] = 16; init[15] = 8; init[16] = 0;
                BinaryPrimitives.WriteUInt32BigEndian(init.AsSpan(20), 4);
                await stream.WriteAsync(init, _cts.Token);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("test"), _cts.Token);

                if (_bogusSize) return;

                // Read SetPixelFormat + SetEncodings + FramebufferUpdateRequest
                var buf = new byte[8192];
                var deadline = DateTime.UtcNow.AddSeconds(4);
                var sentFb = false;
                while (DateTime.UtcNow < deadline && !_cts.IsCancellationRequested)
                {
                    if (!stream.DataAvailable)
                    {
                        await Task.Delay(20, _cts.Token);
                        continue;
                    }

                    var read = await stream.ReadAsync(buf.AsMemory(0, buf.Length), _cts.Token);
                    if (read <= 0) break;
                    for (var i = 0; i < read; i++)
                    {
                        if (buf[i] == VncClientMessage.PointerEvent) SawPointer = true;
                        if (buf[i] == VncClientMessage.KeyEvent) SawKey = true;
                        if (buf[i] == VncClientMessage.FramebufferUpdateRequest && !sentFb)
                        {
                            await SendRawFrame(stream);
                            sentFb = true;
                        }
                    }
                    if (sentFb && SawPointer && SawKey) break;
                    if (sentFb && DateTime.UtcNow > deadline.AddSeconds(-1)) break;
                }
            }
            catch (OperationCanceledException) { }
            catch { /* fixture */ }
        }

        private static async Task SendRawFrame(NetworkStream stream)
        {
            // FramebufferUpdate: 1 rect Raw 16x16 solid
            var header = new byte[4];
            header[0] = VncServerMessage.FramebufferUpdate;
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), 1);
            await stream.WriteAsync(header);

            var rect = new byte[12];
            BinaryPrimitives.WriteUInt16BigEndian(rect.AsSpan(4), 16);
            BinaryPrimitives.WriteUInt16BigEndian(rect.AsSpan(6), 16);
            BinaryPrimitives.WriteInt32BigEndian(rect.AsSpan(8), VncEncoding.Raw);
            await stream.WriteAsync(rect);

            var pixels = new byte[16 * 16 * 4];
            for (var i = 0; i < 16 * 16; i++)
            {
                pixels[i * 4 + 0] = 0;
                pixels[i * 4 + 1] = 0;
                pixels[i * 4 + 2] = 255;
                pixels[i * 4 + 3] = 255;
            }
            await stream.WriteAsync(pixels);
        }

        private static async Task ReadExact(NetworkStream stream, byte[] buffer, CancellationToken ct)
        {
            var off = 0;
            while (off < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(off, buffer.Length - off), ct);
                if (n == 0) throw new EndOfStreamException();
                off += n;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
            try { await _task.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            _cts.Dispose();
        }
    }
}
