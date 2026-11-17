using Renci.SshNet.Common;

namespace XTerminal.Protocols.Ssh;

/// <summary>
/// Central host-key verification used by terminal SSH, SFTP transport, connection tests and forwards.
/// </summary>
public sealed class SshHostKeyPolicy
{
    private readonly KnownHostsStore _store;
    private readonly ISshHostKeyPrompt _prompt;

    public SshHostKeyPolicy(KnownHostsStore store, ISshHostKeyPrompt prompt)
    {
        _store = store;
        _prompt = prompt;
    }

    public void Attach(Renci.SshNet.BaseClient client, string host, int port, bool strictHostKeyChecking, string? keyAlgorithmHint = null)
    {
        client.HostKeyReceived += (_, e) => Apply(e, host, port, strictHostKeyChecking, keyAlgorithmHint);
    }

    public void Apply(HostKeyEventArgs e, string host, int port, bool strictHostKeyChecking, string? keyAlgorithmHint = null)
    {
        e.CanTrust = EvaluateAndDecide(host, port, e.HostKey, strictHostKeyChecking, keyAlgorithmHint);
    }

    /// <summary>Core decision logic — unit-testable without constructing SSH.NET event args.</summary>
    public bool EvaluateAndDecide(
        string host,
        int port,
        byte[] hostKey,
        bool strictHostKeyChecking,
        string? keyAlgorithmHint = null)
    {
        if (!strictHostKeyChecking)
            return true;

        var status = _store.Evaluate(host, port, hostKey, out var fingerprint, out var previous);
        if (status == HostKeyStatus.Trusted)
            return true;

        var decision = _prompt.Prompt(new HostKeyPromptRequest
        {
            Host = host,
            Port = port <= 0 ? 22 : port,
            FingerprintSha256 = fingerprint,
            PreviousFingerprintSha256 = previous,
            KeyAlgorithm = string.IsNullOrWhiteSpace(keyAlgorithmHint) ? "ssh-hostkey" : keyAlgorithmHint,
            Status = status
        });

        switch (decision)
        {
            case HostKeyPromptResult.TrustOnce:
                return true;
            case HostKeyPromptResult.TrustAndSave when status == HostKeyStatus.Unknown:
                _store.Trust(host, port, hostKey);
                return true;
            case HostKeyPromptResult.ReplaceAndSave when status == HostKeyStatus.Changed:
                _store.Trust(host, port, hostKey);
                return true;
            default:
                return false;
        }
    }
}
