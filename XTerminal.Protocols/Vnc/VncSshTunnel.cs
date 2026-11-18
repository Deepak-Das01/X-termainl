using System.Net;
using System.Net.Sockets;
using XTerminal.Core.Models;
using XTerminal.Core.Models.PortForwards;
using XTerminal.Core.Models.Ssh;
using XTerminal.Protocols.Ssh;

namespace XTerminal.Protocols.Vnc;

/// <summary>
/// Application-owned SSH local forward for VNC. Loopback listener only; disposed with the graphical session.
/// SSH encryption protects traffic between SSH endpoints — not necessarily beyond the remote DestinationHost.
/// </summary>
public sealed class VncSshTunnel : IAsyncDisposable
{
    private readonly PortForwardRuntimeService _ports;
    private PortForwardDefinition? _definition;
    private int _disposed;

    public VncSshTunnel(PortForwardRuntimeService ports) => _ports = ports;

    public Guid ForwardId => _definition?.Id ?? Guid.Empty;
    public string ListenHost { get; private set; } = "127.0.0.1";
    public int ListenPort { get; private set; }
    public bool IsEstablished { get; private set; }

    public async Task EstablishAsync(
        SshSessionRecord sshSession,
        string remoteHost,
        int remotePort,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        await DisposeCoreAsync().ConfigureAwait(false);

        var listenPort = AllocateLoopbackPort();
        var def = new PortForwardDefinition
        {
            Id = Guid.NewGuid(),
            Name = "VNC tunnel (ephemeral)",
            SshSessionId = sshSession.Id,
            Type = SshForwardType.Local,
            BindAddress = "127.0.0.1",
            ListenPort = listenPort,
            DestinationHost = remoteHost,
            DestinationPort = remotePort,
            AllowLan = false,
            AutoStart = false,
            ReconnectWithSsh = false
        };

        await _ports.StartAsync(def, sshSession, cancellationToken).ConfigureAwait(false);
        var state = _ports.GetState(def.Id);
        if (state is not (PortForwardRuntimeState.Running or PortForwardRuntimeState.Active))
        {
            var err = _ports.GetError(def.Id) ?? "SSH local forward failed.";
            await _ports.StopAsync(def.Id).ConfigureAwait(false);
            throw new InvalidOperationException(err);
        }

        _definition = def;
        ListenHost = "127.0.0.1";
        ListenPort = listenPort;
        IsEstablished = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await DisposeCoreAsync().ConfigureAwait(false);
    }

    private async Task DisposeCoreAsync()
    {
        IsEstablished = false;
        var def = _definition;
        _definition = null;
        if (def is not null)
            await _ports.StopAsync(def.Id).ConfigureAwait(false);
    }

    private static int AllocateLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
