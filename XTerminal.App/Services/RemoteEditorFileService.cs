using System.IO;
using System.Security.Cryptography;
using System.Text;
using XTerminal.Protocols.Ssh;

namespace XTerminal.App.Services;

/// <summary>
/// Downloads remote files into app-controlled temp storage and uploads saves atomically when possible.
/// Does not log file contents. Temp files are under LocalApplicationData\XTerminal\editor-temp.
/// </summary>
public sealed class RemoteEditorFileService
{
    private readonly string _root;
    public long MaxBytes { get; set; } = 2_000_000;

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
            throw new InvalidOperationException("SFTP is not connected.");

        // Probe for binary before loading as text.
        var probe = await sftp.ReadTextFileAsync(remotePath, maxBytes: Math.Min(MaxBytes, 64_000), cancellationToken: cancellationToken);
        if (LooksBinary(probe))
            throw new InvalidOperationException("File appears to be binary and cannot be opened as text.");

        var content = probe.Length >= MaxBytes
            ? probe
            : await sftp.ReadTextFileAsync(remotePath, maxBytes: MaxBytes, cancellationToken: cancellationToken);

        if (content.Length >= MaxBytes && Encoding.UTF8.GetByteCount(content) >= MaxBytes)
        {
            // ReadTextFileAsync already enforces size; keep message consistent.
        }

        var mtime = await sftp.GetLastWriteTimeUtcAsync(remotePath, cancellationToken);
        var localDir = Path.Combine(_root, sftp.SessionId.Value.ToString("N"), Hash(remotePath));
        Directory.CreateDirectory(localDir);
        var fileName = SftpSessionService.GetName(remotePath);
        if (string.IsNullOrWhiteSpace(fileName) || fileName == "/")
            fileName = "remote.txt";

        var localPath = Path.Combine(localDir, fileName);
        await File.WriteAllTextAsync(localPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

        return new RemoteEditSession
        {
            SessionId = sftp.SessionId.Value,
            SessionName = sftp.SessionName ?? "SFTP",
            RemoteHost = sftp.Host ?? "",
            RemotePath = remotePath,
            LocalTempPath = localPath,
            Content = content,
            RemoteLastWriteUtc = mtime,
            ContentHash = Sha256(content),
            LineEndingStyle = DetectLineEnding(content)
        };
    }

    public async Task<RemoteEditSession> ReloadAsync(
        SftpSessionService sftp,
        RemoteEditSession session,
        CancellationToken cancellationToken = default)
    {
        if (sftp.SessionId != session.SessionId || !sftp.IsConnected)
            throw new InvalidOperationException("SFTP session does not match this editor tab.");

        var fresh = await OpenAsync(sftp, session.RemotePath, cancellationToken);
        session.Content = fresh.Content;
        session.RemoteLastWriteUtc = fresh.RemoteLastWriteUtc;
        session.ContentHash = fresh.ContentHash;
        session.LineEndingStyle = fresh.LineEndingStyle;
        await SaveLocalTempAsync(session.LocalTempPath, session.Content, cancellationToken);
        return session;
    }

    public async Task SaveLocalTempAsync(string localTempPath, string content, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(localTempPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

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
        if (sftp.SessionId != session.SessionId)
            throw new InvalidOperationException("Cannot save: editor is bound to a different SFTP session.");

        if (!overwriteConflict)
        {
            var conflict = await DetectConflictAsync(sftp, session, cancellationToken);
            if (conflict)
            {
                throw new RemoteEditConflictException(
                    "The remote file changed since it was opened. Choose Reload, Save a local copy, or Overwrite.");
            }
        }

        // Preserve original line endings when the buffer still uses them.
        content = PreserveLineEndings(content, session.LineEndingStyle);

        await SaveLocalTempAsync(session.LocalTempPath, content, cancellationToken);
        await sftp.UploadTextAtomicAsync(session.RemotePath, content, cancellationToken);
        session.RemoteLastWriteUtc = await sftp.GetLastWriteTimeUtcAsync(session.RemotePath, cancellationToken);
        session.ContentHash = Sha256(content);
        session.Content = content;
    }

    public async Task<bool> DetectConflictAsync(
        SftpSessionService sftp,
        RemoteEditSession session,
        CancellationToken cancellationToken = default)
    {
        if (session.RemoteLastWriteUtc is null) return false;
        var current = await sftp.GetLastWriteTimeUtcAsync(session.RemotePath, cancellationToken);
        if (current is null) return false;
        return current.Value - session.RemoteLastWriteUtc.Value > TimeSpan.FromSeconds(1);
    }

    public void Cleanup(RemoteEditSession session)
    {
        try
        {
            var dir = Path.GetDirectoryName(session.LocalTempPath);
            if (dir is not null && Directory.Exists(dir) && dir.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    public static bool LooksBinary(string sample)
    {
        if (string.IsNullOrEmpty(sample)) return false;
        var take = Math.Min(sample.Length, 8000);
        var nul = 0;
        for (var i = 0; i < take; i++)
        {
            var c = sample[i];
            if (c == '\0') nul++;
            if (c < 0x09 && c is not ('\n' or '\r' or '\t'))
                return true;
        }

        return nul > 0;
    }

    public static string DetectLineEnding(string content)
    {
        if (content.Contains("\r\n", StringComparison.Ordinal)) return "crlf";
        if (content.Contains('\r')) return "cr";
        return "lf";
    }

    public static string PreserveLineEndings(string content, string style)
    {
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        return style switch
        {
            "crlf" => normalized.Replace("\n", "\r\n"),
            "cr" => normalized.Replace("\n", "\r"),
            _ => normalized
        };
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
    public string RemoteHost { get; init; } = "";
    public required string RemotePath { get; init; }
    public required string LocalTempPath { get; init; }
    public required string Content { get; set; }
    public DateTime? RemoteLastWriteUtc { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string LineEndingStyle { get; set; } = "lf";
}

public sealed class RemoteEditConflictException : Exception
{
    public RemoteEditConflictException(string message) : base(message) { }
}
