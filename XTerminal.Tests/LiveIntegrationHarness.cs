using System.Collections.Concurrent;
using System.Security.Cryptography;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Sftp;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Security;
using XTerminal.Core.Services;
using XTerminal.Protocols.Ssh;

namespace XTerminal.Tests;

internal sealed class MemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _map = new(StringComparer.Ordinal);

    public Task SaveSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        _map[key] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        _map.TryGetValue(key, out var v);
        return Task.FromResult(v);
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        _map.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

internal sealed class MemorySshSessionLibrary : ISshSessionLibrary
{
    private SshLibraryDocument _doc;

    public MemorySshSessionLibrary(SshLibraryDocument doc) => _doc = doc;

    public Task<SshLibraryDocument> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_doc);

    public Task SaveAsync(SshLibraryDocument document, CancellationToken cancellationToken = default)
    {
        _doc = document;
        return Task.CompletedTask;
    }
}

internal sealed class RejectHostKeyPrompt : ISshHostKeyPrompt
{
    public HostKeyPromptResult Prompt(HostKeyPromptRequest request) => HostKeyPromptResult.Reject;
}

/// <summary>Isolated production-service fixture for live SSH/SFTP tests.</summary>
internal sealed class LiveIntegrationHarness : IAsyncDisposable
{
    private readonly string _appDataRoot;
    private bool _disposed;

    public LiveIntegrationConfig Config { get; }
    public MemorySecretStore Secrets { get; }
    public KnownHostsStore KnownHosts { get; }
    public SshHostKeyPolicy HostKeys { get; }
    public SshTransportFactory Transport { get; }
    public SshSessionRecord Session { get; }
    public MemorySshSessionLibrary Library { get; }
    public string KnownHostsPath { get; }

    private LiveIntegrationHarness(
        LiveIntegrationConfig config,
        MemorySecretStore secrets,
        KnownHostsStore knownHosts,
        SshHostKeyPolicy hostKeys,
        SshTransportFactory transport,
        SshSessionRecord session,
        MemorySshSessionLibrary library,
        string appDataRoot,
        string knownHostsPath)
    {
        Config = config;
        Secrets = secrets;
        KnownHosts = knownHosts;
        HostKeys = hostKeys;
        Transport = transport;
        Session = session;
        Library = library;
        _appDataRoot = appDataRoot;
        KnownHostsPath = knownHostsPath;
    }

    public static LiveIntegrationHarness Create(
        LiveIntegrationConfig config,
        string? fingerprintHexOverride = null,
        bool trustFingerprint = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "xterminal-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var knownHostsPath = Path.Combine(root, "known_hosts");

        var secrets = new MemorySecretStore();
        var store = new KnownHostsStore(knownHostsPath);
        var policy = new SshHostKeyPolicy(store, new RejectHostKeyPrompt());
        var transport = new SshTransportFactory(secrets, policy);

        var session = new SshSessionRecord
        {
            Id = Guid.NewGuid(),
            Name = "live-it",
            Host = config.Host,
            Port = config.Port,
            Username = config.Username,
            AuthMethod = config.AuthMethod,
            PrivateKeyPath = config.PrivateKeyPath,
            StrictHostKeyChecking = true,
            ConnectionTimeoutSeconds = 30,
            KeepAliveIntervalSeconds = 30
        };

        if (config.AuthMethod == SshAuthMethod.Password && config.Password is not null)
            secrets.SaveSecretAsync(SshSecretKeys.Password(session.Id), config.Password).GetAwaiter().GetResult();

        var fp = fingerprintHexOverride ?? config.ExpectedHostKeyFingerprintSha256Hex;
        if (trustFingerprint)
            store.TrustFingerprint(session.Host, session.Port, fp);

        var library = new MemorySshSessionLibrary(new SshLibraryDocument
        {
            Sessions = { session }
        });

        return new LiveIntegrationHarness(
            config, secrets, store, policy, transport, session, library, root, knownHostsPath);
    }

    public SftpSessionService CreateSftp() => new(Library, Transport);

    public SshSessionHost CreateSessionHost() => new(Secrets, Library, HostKeys);

    public SshConnectionTester CreateTester() => new(Secrets, HostKeys);

    public PortForwardRuntimeService CreatePortForwards() => new(Transport);

    public static async Task AwaitTransferAsync(
        SftpSessionService sftp,
        Guid transferId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var snap = sftp.GetTransfers().FirstOrDefault(t => t.Id == transferId);
            if (snap is not null &&
                snap.State is SftpTransferState.Complete or SftpTransferState.Failed
                    or SftpTransferState.Cancelled or SftpTransferState.Skipped)
            {
                if (snap.State != SftpTransferState.Complete)
                    throw new InvalidOperationException(
                        $"Transfer ended as {snap.State}: {SshConnectionDiagnosticReport.Sanitize(snap.ErrorMessage ?? "")}");
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Transfer did not complete within the allotted time.");
    }

    public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    public static string Sha256HexFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (Directory.Exists(_appDataRoot))
                Directory.Delete(_appDataRoot, recursive: true);
        }
        catch
        {
            // best effort
        }

        await Task.CompletedTask;
    }
}

internal static class LiveIntegrationRequire
{
    public static LiveIntegrationConfig ConfigOrFail()
    {
        if (!LiveIntegrationGate.IsProcessEnabled)
        {
            Assert.Fail(
                "Live integration requires XTERMINAL_IT_ENABLE=1. Use tools/run-live-integration.ps1.");
        }

        if (!LiveIntegrationGate.TryGetValidatedConfig(out var config, out var error))
            Assert.Fail("Live integration enabled but configuration incomplete: " + error);

        return config;
    }
}
