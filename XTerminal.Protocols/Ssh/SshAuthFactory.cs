using Renci.SshNet;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Security;

namespace XTerminal.Protocols.Ssh;

internal static class SshAuthFactory
{
    public static async Task<AuthenticationMethod[]> CreateAsync(
        SshSessionRecord session,
        ISecretStore secrets,
        bool forJumpHost,
        CancellationToken cancellationToken)
    {
        var user = forJumpHost
            ? (session.ProxyJumpUsername ?? session.Username)
            : session.Username;

        var method = forJumpHost ? session.ProxyJumpAuthMethod : session.AuthMethod;
        var methods = new List<AuthenticationMethod>();

        switch (method)
        {
            case SshAuthMethod.Password:
            {
                var password = await secrets.GetSecretAsync(
                    forJumpHost ? SshSecretKeys.JumpPassword(session.Id) : SshSecretKeys.Password(session.Id),
                    cancellationToken);
                if (string.IsNullOrEmpty(password))
                {
                    throw new InvalidOperationException("Password is not stored for this session. Edit the session and save credentials.");
                }

                methods.Add(new PasswordAuthenticationMethod(user, password));
                break;
            }
            case SshAuthMethod.PrivateKey:
            {
                methods.Add(await CreatePrivateKeyMethodAsync(session, secrets, user, forJumpHost, cancellationToken));
                break;
            }
            case SshAuthMethod.PasswordAndPrivateKey:
            {
                var password = await secrets.GetSecretAsync(
                    forJumpHost ? SshSecretKeys.JumpPassword(session.Id) : SshSecretKeys.Password(session.Id),
                    cancellationToken);
                if (!string.IsNullOrEmpty(password))
                {
                    methods.Add(new PasswordAuthenticationMethod(user, password));
                }

                methods.Add(await CreatePrivateKeyMethodAsync(session, secrets, user, forJumpHost, cancellationToken));
                break;
            }
            case SshAuthMethod.Agent:
            {
                // SSH.NET in this build has no Pageant/ssh-agent client.
                // Fall back to unencrypted default keys from ~/.ssh and report clearly if none work.
                foreach (var candidate in EnumerateDefaultKeyPaths())
                {
                    if (!File.Exists(candidate))
                        continue;

                    try
                    {
                        methods.Add(new PrivateKeyAuthenticationMethod(user, new PrivateKeyFile(candidate)));
                    }
                    catch
                    {
                        // skip unreadable/encrypted keys without passphrase
                    }
                }

                if (methods.Count == 0)
                {
                    throw new InvalidOperationException(
                        "SSH agent mode is not fully supported in this build (no Pageant/ssh-agent bridge). " +
                        "No usable default keys were found under ~/.ssh (id_ed25519/id_rsa/id_ecdsa). " +
                        "Use Private Key auth with an explicit key path instead.");
                }

                break;
            }
            case SshAuthMethod.KeyboardInteractive:
            {
                var password = await secrets.GetSecretAsync(
                    forJumpHost ? SshSecretKeys.JumpPassword(session.Id) : SshSecretKeys.Password(session.Id),
                    cancellationToken);
                if (string.IsNullOrEmpty(password))
                {
                    throw new InvalidOperationException(
                        "Keyboard-interactive authentication requires a stored password for prompts. " +
                        "Edit the session and save credentials.");
                }

                var kbd = new KeyboardInteractiveAuthenticationMethod(user);
                kbd.AuthenticationPrompt += (_, e) =>
                {
                    foreach (var prompt in e.Prompts)
                    {
                        var request = prompt.Request ?? "";
                        if (request.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                            request.Contains("passphrase", StringComparison.OrdinalIgnoreCase) ||
                            e.Prompts.Count == 1)
                        {
                            prompt.Response = password;
                        }
                    }
                };
                methods.Add(kbd);
                break;
            }
            default:
                throw new InvalidOperationException("Unsupported authentication method.");
        }

        return methods.ToArray();
    }

    private static async Task<PrivateKeyAuthenticationMethod> CreatePrivateKeyMethodAsync(
        SshSessionRecord session,
        ISecretStore secrets,
        string user,
        bool forJumpHost,
        CancellationToken cancellationToken)
    {
        var path = forJumpHost ? session.ProxyJumpPrivateKeyPath : session.PrivateKeyPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException("Private key file path is missing or invalid.");
        }

        var passphrase = await secrets.GetSecretAsync(
            forJumpHost ? SshSecretKeys.JumpPassphrase(session.Id) : SshSecretKeys.Passphrase(session.Id),
            cancellationToken);

        PrivateKeyFile keyFile = string.IsNullOrEmpty(passphrase)
            ? new PrivateKeyFile(path)
            : new PrivateKeyFile(path, passphrase);

        return new PrivateKeyAuthenticationMethod(user, keyFile);
    }

    private static IEnumerable<string> EnumerateDefaultKeyPaths()
    {
        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        yield return Path.Combine(sshDir, "id_ed25519");
        yield return Path.Combine(sshDir, "id_rsa");
        yield return Path.Combine(sshDir, "id_ecdsa");
    }
}
