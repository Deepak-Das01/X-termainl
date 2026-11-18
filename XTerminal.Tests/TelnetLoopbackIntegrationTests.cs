using System.Net;
using System.Net.Sockets;
using System.Text;
using XTerminal.Core.Models;
using XTerminal.Protocols.Telnet;

namespace XTerminal.Tests;

/// <summary>
/// Loopback Telnet integration against an ephemeral 127.0.0.1 listener.
/// Exercises production <see cref="TelnetSessionHost"/> — not parser-only unit tests.
/// </summary>
[Trait("Category", "TelnetLoopback")]
public class TelnetLoopbackIntegrationTests
{
    private const string Marker = "XT_TELNET_LOOPBACK_OK_9f3c";

    [Fact]
    public async Task Connects_negotiates_and_exchanges_marker()
    {
        await using var server = await LoopbackTelnetServer.StartAsync();
        await using var host = new TelnetSessionHost();
        var output = new StringBuilder();
        host.OutputReceived += bytes => output.Append(Encoding.UTF8.GetString(bytes));

        await host.ConnectAsync(new SessionDefinition
        {
            Name = "loopback",
            Kind = SessionKind.Telnet,
            Host = "127.0.0.1",
            Port = server.Port,
            ConnectionTimeoutSeconds = 10,
            TerminalType = "xterm-256color",
            AutoReconnect = false
        });

        Assert.True(host.IsConnected);
        Assert.Equal(ConnectionState.Connected, host.State);
        Assert.Contains("unencrypted", host.StatusMessage ?? "", StringComparison.OrdinalIgnoreCase);

        host.Write(Encoding.UTF8.GetBytes("ping\r\n"));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !output.ToString().Contains(Marker, StringComparison.Ordinal))
            await Task.Delay(50);

        Assert.Contains(Marker, output.ToString(), StringComparison.Ordinal);
        Assert.True(server.SawNegotiation);
        Assert.True(server.RejectedUnsupportedOption);

        await host.DisposeAsync();
        Assert.False(host.IsConnected);
    }

    [Fact]
    public async Task Remote_disconnect_is_detected()
    {
        await using var server = await LoopbackTelnetServer.StartAsync(closeAfterAccept: true);
        await using var host = new TelnetSessionHost();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Closed += () => closed.TrySetResult();

        await host.ConnectAsync(new SessionDefinition
        {
            Name = "disc",
            Kind = SessionKind.Telnet,
            Host = "127.0.0.1",
            Port = server.Port,
            ConnectionTimeoutSeconds = 10
        });

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Connect_cancellation_does_not_report_connected()
    {
        // TEST-NET-3 documentation address — typically blackholed; cancel before timeout.
        await using var host = new TelnetSessionHost();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await host.ConnectAsync(new SessionDefinition
            {
                Name = "cancel",
                Kind = SessionKind.Telnet,
                Host = "203.0.113.1",
                Port = 23,
                ConnectionTimeoutSeconds = 30
            }, cts.Token);
        });
        Assert.False(host.IsConnected);
        Assert.NotEqual(ConnectionState.Connected, host.State);
    }

    [Fact]
    public async Task Reconnect_requires_explicit_preference()
    {
        await using var server = await LoopbackTelnetServer.StartAsync();
        await using var host = new TelnetSessionHost();
        await host.ConnectAsync(new SessionDefinition
        {
            Name = "nore",
            Kind = SessionKind.Telnet,
            Host = "127.0.0.1",
            Port = server.Port,
            AutoReconnect = false
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.ReconnectAsync());
    }

    [Fact]
    public async Task Duplicate_dispose_is_safe()
    {
        await using var server = await LoopbackTelnetServer.StartAsync();
        var host = new TelnetSessionHost();
        await host.ConnectAsync(new SessionDefinition
        {
            Name = "disp",
            Kind = SessionKind.Telnet,
            Host = "127.0.0.1",
            Port = server.Port
        });
        await host.DisposeAsync();
        await host.DisposeAsync();
    }

    private sealed class LoopbackTelnetServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptTask;
        private readonly bool _closeAfterAccept;

        public int Port { get; }
        public bool SawNegotiation { get; private set; }
        public bool RejectedUnsupportedOption { get; private set; }

        private LoopbackTelnetServer(TcpListener listener, bool closeAfterAccept)
        {
            _listener = listener;
            _closeAfterAccept = closeAfterAccept;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _acceptTask = Task.Run(AcceptLoopAsync);
        }

        public static async Task<LoopbackTelnetServer> StartAsync(bool closeAfterAccept = false)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var server = new LoopbackTelnetServer(listener, closeAfterAccept);
            await Task.Yield();
            return server;
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                await using var stream = client.GetStream();
                if (_closeAfterAccept)
                {
                    await Task.Delay(50, _cts.Token);
                    return;
                }

                // Offer WILL ECHO, DO TERMINAL-TYPE, WILL unknown(99)
                var offer = new byte[]
                {
                    TelnetCommand.Iac, TelnetCommand.Will, TelnetOption.Echo,
                    TelnetCommand.Iac, TelnetCommand.Do, TelnetOption.TerminalType,
                    TelnetCommand.Iac, TelnetCommand.Will, 99
                };
                await stream.WriteAsync(offer, _cts.Token);

                var buffer = new byte[4096];
                var accumulated = new List<byte>();
                var sentMarker = false;
                var deadline = DateTime.UtcNow.AddSeconds(4);
                while (DateTime.UtcNow < deadline && !_cts.IsCancellationRequested)
                {
                    if (!stream.DataAvailable)
                    {
                        await Task.Delay(20, _cts.Token);
                        continue;
                    }

                    var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), _cts.Token);
                    if (read <= 0) break;
                    for (var i = 0; i < read; i++)
                        accumulated.Add(buffer[i]);

                    var all = accumulated.ToArray();
                    if (Array.IndexOf(all, TelnetCommand.Iac) >= 0)
                        SawNegotiation = true;
                    if (IndexOfSequence(all, new byte[] { TelnetCommand.Iac, TelnetCommand.Dont, 99 }) >= 0)
                        RejectedUnsupportedOption = true;

                    var text = Encoding.ASCII.GetString(all);
                    if (!sentMarker && text.Contains("ping", StringComparison.OrdinalIgnoreCase))
                    {
                        var reply = Encoding.UTF8.GetBytes(Marker + "\r\n");
                        await stream.WriteAsync(reply, _cts.Token);
                        sentMarker = true;
                        // Keep reading briefly so late DONT replies can arrive.
                        deadline = DateTime.UtcNow.AddMilliseconds(500);
                    }

                    if (sentMarker && RejectedUnsupportedOption)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // expected on dispose
            }
            catch
            {
                // test server best-effort
            }
        }

        private static int IndexOfSequence(byte[] haystack, byte[] needle)
        {
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        public async ValueTask DisposeAsync()
        {
            try { _cts.Cancel(); } catch { /* ignore */ }
            try { _listener.Stop(); } catch { /* ignore */ }
            try { await _acceptTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
            _cts.Dispose();
        }
    }
}
