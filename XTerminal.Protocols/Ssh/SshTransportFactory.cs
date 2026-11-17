using System.Text;
using Renci.SshNet;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Security;

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
        SshClient? jump = null;
        ForwardedPortLocal? tunnel = null;
        SshClient? client = null;

        try
        {
            string host;
            int port;

            if (!string.IsNullOrWhiteSpace(session.ProxyJumpHost))
            {
                (jump, tunnel) = await OpenJumpTunnelAsync(session, cancellationToken);
                host = "127.0.0.1";
                port = (int)tunnel.BoundPort;
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

            if (tunnel is null)
            {
                AttachHostKeyHandler(client, session.Host, session.Port <= 0 ? 22 : session.Port, session);
            }

            await Task.Run(() => client.Connect(), cancellationToken);

            return new OwnedSshClient(
                client,
                jump is null ? null : new TunnelOwnership(jump, tunnel));
        }
        catch
        {
            try { client?.Dispose(); } catch { /* ignore */ }
            try { tunnel?.Dispose(); } catch { /* ignore */ }
            try { jump?.Dispose(); } catch { /* ignore */ }
            throw;
        }
    }

    public async Task<SftpTransport> OpenSftpAsync(SshSessionRecord session, CancellationToken cancellationToken = default)
    {
        SshClient? jump = null;
        ForwardedPortLocal? tunnel = null;
        SftpClient? sftp = null;

        try
        {
            string host;
            int port;

            if (!string.IsNullOrWhiteSpace(session.ProxyJumpHost))
            {
                (jump, tunnel) = await OpenJumpTunnelAsync(session, cancellationToken);
                host = "127.0.0.1";
                port = (int)tunnel.BoundPort;
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

            if (tunnel is null)
            {
                AttachHostKeyHandler(sftp, session.Host, session.Port <= 0 ? 22 : session.Port, session);
            }

            await Task.Run(() => sftp.Connect(), cancellationToken);
            return new SftpTransport(sftp, jump, tunnel);
        }
        catch
        {
            try { sftp?.Dispose(); } catch { /* ignore */ }
            try { tunnel?.Dispose(); } catch { /* ignore */ }
            try { jump?.Dispose(); } catch { /* ignore */ }
            throw;
        }
    }

    private async Task<(SshClient Jump, ForwardedPortLocal Tunnel)> OpenJumpTunnelAsync(
        SshSessionRecord session,
        CancellationToken cancellationToken)
    {
        var jumpUser = session.ProxyJumpUsername ?? session.Username;
        var jumpMethods = await SshAuthFactory.CreateAsync(session, _secrets, forJumpHost: true, cancellationToken);
        var jumpInfo = new ConnectionInfo(
            session.ProxyJumpHost!,
            session.ProxyJumpPort <= 0 ? 22 : session.ProxyJumpPort,
            jumpUser,
            jumpMethods)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(5, session.ConnectionTimeoutSeconds))
        };
        ApplyCompression(jumpInfo, session.Compression);

        var jump = new SshClient(jumpInfo)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(Math.Max(0, session.KeepAliveIntervalSeconds))
        };
        AttachHostKeyHandler(jump, session.ProxyJumpHost!, session.ProxyJumpPort <= 0 ? 22 : session.ProxyJumpPort, session);
        await Task.Run(() => jump.Connect(), cancellationToken);

        var tunnel = new ForwardedPortLocal("127.0.0.1", 0, session.Host, (uint)(session.Port <= 0 ? 22 : session.Port));
        jump.AddForwardedPort(tunnel);
        tunnel.Start();
        return (jump, tunnel);
    }

    private void AttachHostKeyHandler(BaseClient client, string host, int port, SshSessionRecord session) =>
        _hostKeys.Attach(client, host, port, session.StrictHostKeyChecking);

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

public sealed record TunnelOwnership(SshClient Jump, ForwardedPortLocal? Tunnel);

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
    private readonly ForwardedPortLocal? _tunnel;

    public SftpTransport(SftpClient client, SshClient? jumpClient, ForwardedPortLocal? tunnel)
    {
        Client = client;
        JumpClient = jumpClient;
        _tunnel = tunnel;
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

        try
        {
            if (_tunnel?.IsStarted == true) _tunnel.Stop();
        }
        catch { /* ignore */ }

        try { _tunnel?.Dispose(); } catch { /* ignore */ }

        try
        {
            if (JumpClient?.IsConnected == true) JumpClient.Disconnect();
        }
        catch { /* ignore */ }

        try { JumpClient?.Dispose(); } catch { /* ignore */ }

        await Task.CompletedTask;
    }
}
