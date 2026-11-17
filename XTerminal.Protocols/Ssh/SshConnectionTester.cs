using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Security;

namespace XTerminal.Protocols.Ssh;

public sealed class SshConnectionTester
{
    private readonly ISecretStore _secrets;
    private readonly SshHostKeyPolicy _hostKeys;

    public SshConnectionTester(ISecretStore secrets, SshHostKeyPolicy hostKeys)
    {
        _secrets = secrets;
        _hostKeys = hostKeys;
    }

    public async Task<(bool Ok, string Message)> TestAsync(
        SshSessionRecord session,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var factory = new SshTransportFactory(_secrets, _hostKeys);
            var owned = await factory.OpenSshClientAsync(session, cancellationToken);
            try
            {
                if (owned.Client.IsConnected)
                    return (true, "Authentication succeeded.");
                return (false, "Connected but session was not active.");
            }
            finally
            {
                try { owned.Ownership?.Tunnel?.Stop(); } catch { /* ignore */ }
                try { owned.Ownership?.Tunnel?.Dispose(); } catch { /* ignore */ }
                try { if (owned.Ownership?.Jump?.IsConnected == true) owned.Ownership.Jump.Disconnect(); } catch { /* ignore */ }
                try { owned.Ownership?.Jump?.Dispose(); } catch { /* ignore */ }
                try { if (owned.Client.IsConnected) owned.Client.Disconnect(); } catch { /* ignore */ }
                try { owned.Client.Dispose(); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            return (false, Sanitize(ex.Message));
        }
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message)) return "Unknown error";
        return message
            .Replace("password", "********", StringComparison.OrdinalIgnoreCase)
            .Replace("passphrase", "********", StringComparison.OrdinalIgnoreCase);
    }
}
