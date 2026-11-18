using System.IO;
using System.Security.Cryptography;
using System.Text;
using XTerminal.Core.Services;

namespace XTerminal.Protocols.Ssh;

public sealed class KnownHostsStore
{
    private readonly string _path;
    private readonly object _sync = new();

    public KnownHostsStore(string? path = null)
    {
        _path = path ?? AppStoragePaths.KnownHosts;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public string FilePath => _path;

    public static string NormalizeHost(string host) => host.Trim().ToLowerInvariant();

    public static string HostIdentity(string host, int port) => $"{NormalizeHost(host)}:{port}";

    public static string FingerprintSha256(byte[] hostKey) =>
        Convert.ToHexString(SHA256.HashData(hostKey));

    public HostKeyStatus Evaluate(string host, int port, byte[] hostKey, out string fingerprint, out string? previousFingerprint)
    {
        fingerprint = FingerprintSha256(hostKey);
        previousFingerprint = null;
        var key = HostIdentity(host, port);
        lock (_sync)
        {
            if (!File.Exists(_path))
                return HostKeyStatus.Unknown;

            foreach (var line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 ||
                    !string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase))
                    continue;

                previousFingerprint = parts[1];
                if (string.Equals(parts[1], fingerprint, StringComparison.OrdinalIgnoreCase))
                    return HostKeyStatus.Trusted;

                return HostKeyStatus.Changed;
            }

            return HostKeyStatus.Unknown;
        }
    }

    /// <summary>Legacy helper used by older call sites and tests.</summary>
    public bool IsTrusted(string host, int port, byte[] hostKey, out bool isNew)
    {
        var status = Evaluate(host, port, hostKey, out _, out _);
        isNew = status == HostKeyStatus.Unknown;
        return status == HostKeyStatus.Trusted;
    }

    /// <summary>Add or replace fingerprint for host:port. Caller must have explicit user consent for replace.</summary>
    public void Trust(string host, int port, byte[] hostKey) =>
        WriteFingerprint(host, port, FingerprintSha256(hostKey));

    public void TrustFingerprint(string host, int port, string fingerprintSha256) =>
        WriteFingerprint(host, port, fingerprintSha256);

    private void WriteFingerprint(string host, int port, string fingerprint)
    {
        var key = HostIdentity(host, port);
        lock (_sync)
        {
            var lines = File.Exists(_path)
                ? File.ReadAllLines(_path)
                    .Where(l => !l.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : new List<string>();
            lines.Add($"{key} {fingerprint}");
            var temp = _path + ".tmp";
            File.WriteAllLines(temp, lines, Encoding.UTF8);
            File.Copy(temp, _path, overwrite: true);
            try { File.Delete(temp); } catch { /* best effort */ }
        }
    }
}
