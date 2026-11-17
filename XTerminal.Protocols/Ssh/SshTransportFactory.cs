using System.Text;
using Renci.SshNet;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Security;
using XTerminal.Core.Services;

namespace XTerminal.Protocols.Ssh;

/// <summary>
/// Builds authenticated SSH/SFTP clients from session metadata + Credential Manager secrets.
/// Does not prompt for credentials; reuses stored auth for the session.
/// </summary>
public sealed class SshTransportFactory
{
    private readonly ISecretStore _secrets;
    private readonly SshHostKeyPolicy _hostKeys;

    public SshTransportFactory(ISecretStore secrets, SshHostKeyPolicy hostKeys)
    {
        _secrets = secrets;
        _hostKeys = hostKeys;
    }

    /// <summary>Opens an authenticated SSH client (connected) for tunneling or other non-shell use.</summary>
    public async Task<OwnedSshClient> OpenSshClientAsync(SshSessionRecord session, CancellationToken cancellationToken = default)
    {
        TunnelOwnership? ownership = null;
        SshClient? client = null;

        try
        {
            string host;
            int port;

            if (!string.IsNullOrWhiteSpace(session.ProxyJumpHost))
            {
                ownership = await OpenJumpTunnelAsync(session, cancellationToken);
                host = "127.0.0.1";
                port = (int)ownership.Tunnel!.BoundPort;
            }
            else
            {
                host = session.Host;
                port = session.Port <= 0 ? 22 : session.Port;
            }

            var methods = await SshAuthFactory.CreateAsync(session, _secrets, forJumpHost: false, cancellationToken);
            var info = new ConnectionInfo(host, port, session.Username, methods)
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(5, session.ConnectionTimeoutSeconds)),
                Encoding = Encoding.UTF8
            };
            ApplyCompression(info, session.Compression);

            client = new SshClient(info)
            {
                KeepAliveInterval = TimeSpan.FromSeconds(Math.Max(0, session.KeepAliveIntervalSeconds))
            };

            // Always verify the logical target host key (even when dialing through a local jump tunnel).
            AttachHostKeyHandler(client, session.Host, session.Port <= 0 ? 22 : session.Port, session);

            await Task.Run(() => client.Connect(), cancellationToken);

            return new OwnedSshClient(client, ownership);
        }
        catch
        {
            try { client?.Dispose(); } catch { /* ignore */ }
            ownership?.Dispose();
            throw;
        }
    }

    public async Task<SftpTransport> OpenSftpAsync(SshSessionRecord session, CancellationToken cancellationToken = default)
    {
        TunnelOwnership? ownership = null;
        SftpClient? sftp = null;

        try
        {
            string host;
            int port;

            if (!string.IsNullOrWhiteSpace(session.ProxyJumpHost))
            {
                ownership = await OpenJumpTunnelAsync(session, cancellationToken);
                host = "127.0.0.1";
                port = (int)ownership.Tunnel!.BoundPort;
            }
            else
            {
                host = session.Host;
                port = session.Port <= 0 ? 22 : session.Port;
            }

            var methods = await SshAuthFactory.CreateAsync(session, _secrets, forJumpHost: false, cancellationToken);
            var info = new ConnectionInfo(host, port, session.Username, methods)
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(5, session.ConnectionTimeoutSeconds)),
                Encoding = Encoding.UTF8
            };
            ApplyCompression(info, session.Compression);

            sftp = new SftpClient(info)
            {
                KeepAliveInterval = TimeSpan.FromSeconds(Math.Max(0, session.KeepAliveIntervalSeconds)),
                OperationTimeout = TimeSpan.FromSeconds(Math.Max(5, session.ConnectionTimeoutSeconds))
            };

            AttachHostKeyHandler(sftp, session.Host, session.Port <= 0 ? 22 : session.Port, session);

            await Task.Run(() => sftp.Connect(), cancellationToken);
            return new SftpTransport(sftp, ownership);
        }
        catch
        {
            try { sftp?.Dispose(); } catch { /* ignore */ }
            ownership?.Dispose();
            throw;
        }
    }

    private async Task<TunnelOwnership> OpenJumpTunnelAsync(
        SshSessionRecord session,
        CancellationToken cancellationToken)
    {
        var hops = JumpHostChain.FromSession(session);
        if (hops.Count == 0)
            throw new InvalidOperationException("Jump host is configured but the hop chain is empty.");

        var hopErrors = JumpHostChain.Validate(hops, session.Host, session.Port <= 0 ? 22 : session.Port);
        if (hopErrors.Count > 0)
            throw new InvalidOperationException("Jump host chain invalid: " + string.Join(" ", hopErrors));

        var chain = new List<SshClient>();
        var forwards = new List<ForwardedPortLocal>();
        SshClient? previous = null;

        try
        {
            for (var i = 0; i < hops.Count; i++)
            {
                var hop = hops[i];
                string dialHost;
                int dialPort;
                if (previous is null)
                {
                    dialHost = hop.Host;
                    dialPort = hop.Port;
                }
                else
                {
                    var link = new ForwardedPortLocal("127.0.0.1", 0, hop.Host, (uint)hop.Port);
                    previous.AddForwardedPort(link);
                    link.Start();
                    forwards.Add(link);
                    dialHost = "127.0.0.1";
                    dialPort = (int)link.BoundPort;
                }

                var hopUser = hop.Username
                              ?? (i == 0 ? session.ProxyJumpUsername : null)
                              ?? session.Username;
                var hopMethods = await SshAuthFactory.CreateAsync(session, _secrets, forJumpHost: true, cancellationToken);
                var hopInfo = new ConnectionInfo(dialHost, dialPort, hopUser, hopMethods)
                {
                    Timeout = TimeSpan.FromSeconds(Math.Max(5, session.ConnectionTimeoutSeconds))
                };
                ApplyCompression(hopInfo, session.Compression);

                var hopClient = new SshClient(hopInfo)
                {
                    KeepAliveInterval = TimeSpan.FromSeconds(Math.Max(0, session.KeepAliveIntervalSeconds))
                };
                AttachHostKeyHandler(hopClient, hop.Host, hop.Port, session);
                await Task.Run(() => hopClient.Connect(), cancellationToken);
                chain.Add(hopClient);
                previous = hopClient;
            }

            var egress = previous!;
            var targetPort = session.Port <= 0 ? 22 : session.Port;
            var tunnel = new ForwardedPortLocal("127.0.0.1", 0, session.Host, (uint)targetPort);
            egress.AddForwardedPort(tunnel);
            tunnel.Start();
            forwards.Add(tunnel);

            return new TunnelOwnership(chain, forwards, tunnel);
        }
        catch
        {
            foreach (var f in forwards)
            {
                try { if (f.IsStarted) f.Stop(); } catch { /* ignore */ }
                try { f.Dispose(); } catch { /* ignore */ }
            }

            for (var i = chain.Count - 1; i >= 0; i--)
            {
                try { if (chain[i].IsConnected) chain[i].Disconnect(); } catch { /* ignore */ }
                try { chain[i].Dispose(); } catch { /* ignore */ }
            }

            throw;
        }
    }

    private void AttachHostKeyHandler(BaseClient client, string host, int port, SshSessionRecord session) =>
        _hostKeys.Attach(client, host, port <= 0 ? 22 : port, session.StrictHostKeyChecking);

    private static void ApplyCompression(ConnectionInfo info, bool enabled)
    {
        try
        {
            if (enabled && info.CompressionAlgorithms.ContainsKey("zlib@openssh.com"))
            {
                info.CompressionAlgorithms.SetPosition("zlib@openssh.com", 0);
            }
        }
        catch
        {
            // ignore
        }
    }
}

public sealed class TunnelOwnership : IDisposable
{
    public TunnelOwnership(
        IReadOnlyList<SshClient> jumpChain,
        IReadOnlyList<ForwardedPortLocal> forwards,
        ForwardedPortLocal? tunnel)
    {
        JumpChain = jumpChain;
        Forwards = forwards;
        Tunnel = tunnel;
        Jump = jumpChain.Count > 0 ? jumpChain[0] : null;
    }

    public IReadOnlyList<SshClient> JumpChain { get; }
    public IReadOnlyList<ForwardedPortLocal> Forwards { get; }
    public SshClient? Jump { get; }
    public ForwardedPortLocal? Tunnel { get; }

    public void Dispose()
    {
        foreach (var f in Forwards.Reverse())
        {
            try { if (f.IsStarted) f.Stop(); } catch { /* ignore */ }
            try { f.Dispose(); } catch { /* ignore */ }
        }

        for (var i = JumpChain.Count - 1; i >= 0; i--)
        {
            try { if (JumpChain[i].IsConnected) JumpChain[i].Disconnect(); } catch { /* ignore */ }
            try { JumpChain[i].Dispose(); } catch { /* ignore */ }
        }
    }
}

public sealed class OwnedSshClient
{
    public OwnedSshClient(SshClient client, TunnelOwnership? ownership)
    {
        Client = client;
        Ownership = ownership;
    }

    public SshClient Client { get; }
    public TunnelOwnership? Ownership { get; }
}

/// <summary>Owns an authenticated SFTP client and optional jump tunnel.</summary>
public sealed class SftpTransport : IAsyncDisposable
{
    private readonly TunnelOwnership? _ownership;

    public SftpTransport(SftpClient client, TunnelOwnership? ownership)
    {
        Client = client;
        _ownership = ownership;
        JumpClient = ownership?.Jump;
    }

    public SftpClient Client { get; }
    public SshClient? JumpClient { get; }
    public bool IsConnected => Client.IsConnected;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Client.IsConnected) Client.Disconnect();
        }
        catch { /* ignore */ }

        try { Client.Dispose(); } catch { /* ignore */ }

        _ownership?.Dispose();

        await Task.CompletedTask;
    }
}
