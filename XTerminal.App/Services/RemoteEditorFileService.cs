using System.IO;
using System.Security.Cryptography;
using System.Text;
using XTerminal.Protocols.Ssh;

namespace XTerminal.App.Services;

/// <summary>
/// Downloads remote files into app-controlled temp storage and uploads saves atomically when possible.
/// </summary>
public sealed class RemoteEditorFileService
{
    private readonly string _root;

    public RemoteEditorFileService()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTerminal",
            "editor-temp");
        Directory.CreateDirectory(_root);
    }

    public async Task<RemoteEditSession> OpenAsync(
        SftpSessionService sftp,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        if (sftp.SessionId is null || !sftp.IsConnected)
        {
            throw new InvalidOperationException("SFTP is not connected.");
        }

        var content = await sftp.ReadTextFileAsync(remotePath, cancellationToken: cancellationToken);
        var mtime = await sftp.GetLastWriteTimeUtcAsync(remotePath, cancellationToken);
        var localDir = Path.Combine(_root, sftp.SessionId.Value.ToString("N"), Hash(remotePath));
        Directory.CreateDirectory(localDir);
        var fileName = SftpSessionService.GetName(remotePath);
        if (string.IsNullOrWhiteSpace(fileName) || fileName == "/")
        {
            fileName = "remote.txt";
        }

        var localPath = Path.Combine(localDir, fileName);
        await File.WriteAllTextAsync(localPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

        return new RemoteEditSession
        {
            SessionId = sftp.SessionId.Value,
            SessionName = sftp.SessionName ?? "SFTP",
            RemotePath = remotePath,
            LocalTempPath = localPath,
            Content = content,
            RemoteLastWriteUtc = mtime,
            ContentHash = Sha256(content)
        };
    }

    public async Task SaveLocalTempAsync(string localTempPath, string content, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(localTempPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = localTempPath + ".saving";
        await File.WriteAllTextAsync(tmp, content, new UTF8Encoding(false), cancellationToken);
        File.Copy(tmp, localTempPath, overwrite: true);
        File.Delete(tmp);
    }

    public async Task UploadAtomicAsync(
        SftpSessionService sftp,
        RemoteEditSession session,
        string content,
        bool overwriteConflict,
        CancellationToken cancellationToken = default)
    {
        if (!overwriteConflict && session.RemoteLastWriteUtc is not null)
        {
            var current = await sftp.GetLastWriteTimeUtcAsync(session.RemotePath, cancellationToken);
            if (current is not null &&
                current.Value - session.RemoteLastWriteUtc.Value > TimeSpan.FromSeconds(1))
            {
                throw new RemoteEditConflictException(
                    "The remote file changed since it was opened. Save again to overwrite, or reload.");
            }
        }

        await SaveLocalTempAsync(session.LocalTempPath, content, cancellationToken);
        await sftp.UploadTextAtomicAsync(session.RemotePath, content, cancellationToken);
        session.RemoteLastWriteUtc = await sftp.GetLastWriteTimeUtcAsync(session.RemotePath, cancellationToken);
        session.ContentHash = Sha256(content);
        session.Content = content;
    }

    public void Cleanup(RemoteEditSession session)
    {
        try
        {
            var dir = Path.GetDirectoryName(session.LocalTempPath);
            if (dir is not null && Directory.Exists(dir) && dir.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class RemoteEditSession
{
    public required Guid SessionId { get; init; }
    public required string SessionName { get; init; }
    public required string RemotePath { get; init; }
    public required string LocalTempPath { get; init; }
    public required string Content { get; set; }
    public DateTime? RemoteLastWriteUtc { get; set; }
    public string ContentHash { get; set; } = string.Empty;
}

public sealed class RemoteEditConflictException : Exception
{
    public RemoteEditConflictException(string message) : base(message) { }
}
