using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using XTerminal.Core.Models.Sftp;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Services;

namespace XTerminal.Protocols.Ssh;

public sealed class SftpSessionService : IAsyncDisposable
{
    private readonly ISshSessionLibrary _library;
    private readonly SshTransportFactory _factory;
    private readonly ConcurrentDictionary<Guid, TransferJob> _transfers = new();
    private readonly object _gate = new();
    private SftpTransport? _transport;
    private SshSessionRecord? _session;
    private readonly SemaphoreSlim _opLock = new(1, 1);

    public SftpSessionService(ISshSessionLibrary library, SshTransportFactory factory)
    {
        _library = library;
        _factory = factory;
    }

    public Guid? SessionId => _session?.Id;
    public string? SessionName => _session?.Name;
    public string? Host => _session?.Host;
    public string? Username => _session?.Username;
    public bool IsConnected => _transport?.IsConnected == true;
    public string? StatusMessage { get; private set; }
    public string WorkingDirectory { get; private set; } = "/";
    public string HomeDirectory { get; private set; } = "/";

    public event Action? ConnectionChanged;
    public event Action? TransfersChanged;
    public event Action<SftpTransferSnapshot>? TransferUpdated;

    public IReadOnlyList<SftpTransferSnapshot> GetTransfers() =>
        _transfers.Values.Select(t => t.ToSnapshot()).OrderByDescending(t => t.Id).ToList();

    public async Task ConnectAsync(Guid sshSessionId, CancellationToken cancellationToken = default)
    {
        await _opLock.WaitAsync(cancellationToken);
        try
        {
            if (_session?.Id == sshSessionId && IsConnected)
            {
                return;
            }

            await DisposeTransportAsync();

            var doc = await _library.LoadAsync(cancellationToken);
            _session = doc.Sessions.FirstOrDefault(s => s.Id == sshSessionId)
                       ?? throw new InvalidOperationException("SSH session was not found in the library.");

            StatusMessage = $"Connecting SFTP to {_session.Username}@{_session.Host}…";
            ConnectionChanged?.Invoke();

            _transport = await _factory.OpenSftpAsync(_session, cancellationToken);
            HomeDirectory = NormalizePath(_transport.Client.WorkingDirectory);
            WorkingDirectory = HomeDirectory;
            StatusMessage = "Connected";
            ConnectionChanged?.Invoke();
        }
        catch (Exception ex)
        {
            var (kind, msg) = SshErrorMapper.Map(ex);
            StatusMessage = FormatError(kind, msg);
            await DisposeTransportAsync();
            ConnectionChanged?.Invoke();
            throw new InvalidOperationException(StatusMessage, ex);
        }
        finally
        {
            _opLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _opLock.WaitAsync();
        try
        {
            await DisposeTransportAsync();
            StatusMessage = "Disconnected";
            ConnectionChanged?.Invoke();
        }
        finally
        {
            _opLock.Release();
        }
    }

    public async Task<IReadOnlyList<SftpRemoteEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        path = NormalizePath(path);
        try
        {
            var entries = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!client.Exists(path))
                {
                    throw new DirectoryNotFoundException("Path does not exist.");
                }

                return client.ListDirectory(path)
                    .Where(e => e.Name is not "." and not "..")
                    .Select(MapEntry)
                    .OrderByDescending(e => e.IsDirectory)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }, cancellationToken);

            WorkingDirectory = path;
            return entries;
        }
        catch (Exception ex)
        {
            throw MapAndWrap(ex);
        }
    }

    public async Task<IReadOnlyList<SftpRemoteEntry>> ListTreeChildrenAsync(string path, CancellationToken cancellationToken = default)
    {
        var all = await ListAsync(path, cancellationToken);
        return all.Where(e => e.IsDirectory).ToList();
    }

    public Task NavigateHomeAsync(CancellationToken cancellationToken = default) =>
        ListAsync(HomeDirectory, cancellationToken);

    public async Task CreateFolderAsync(string parentPath, string name, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        var full = Join(parentPath, name);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                client.CreateDirectory(full);
            }, cancellationToken);
        }
        catch (Exception ex) { throw MapAndWrap(ex); }
    }

    public async Task CreateFileAsync(string parentPath, string name, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        var full = Join(parentPath, name);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = client.OpenWrite(full);
                stream.SetLength(0);
            }, cancellationToken);
        }
        catch (Exception ex) { throw MapAndWrap(ex); }
    }

    public async Task RenameAsync(string fullPath, string newName, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        var parent = GetParent(fullPath);
        var dest = Join(parent, newName);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                client.RenameFile(fullPath, dest);
            }, cancellationToken);
        }
        catch (Exception ex) { throw MapAndWrap(ex); }
    }

    public async Task DeleteAsync(string fullPath, bool isDirectory, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (isDirectory)
                {
                    DeleteDirectoryRecursive(client, fullPath, cancellationToken);
                }
                else
                {
                    client.DeleteFile(fullPath);
                }
            }, cancellationToken);
        }
        catch (Exception ex) { throw MapAndWrap(ex); }
    }

    public async Task<string> ReadTextFileAsync(string fullPath, long maxBytes = 2_000_000, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attrs = client.GetAttributes(fullPath);
                if (attrs.IsDirectory)
                {
                    throw new InvalidOperationException("Path is a directory.");
                }

                if (attrs.Size > maxBytes)
                {
                    throw new InvalidOperationException($"File is larger than {maxBytes / 1024 / 1024} MB and cannot be opened in the editor.");
                }

                using var stream = client.OpenRead(fullPath);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }, cancellationToken);
        }
        catch (Exception ex) { throw MapAndWrap(ex); }
    }

    public async Task WriteTextFileAsync(string fullPath, string content, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
                using var stream = client.OpenWrite(fullPath);
                stream.Write(bytes, 0, bytes.Length);
                stream.SetLength(bytes.Length);
            }, cancellationToken);
        }
        catch (Exception ex) { throw MapAndWrap(ex); }
    }

    public async Task<DateTime?> GetLastWriteTimeUtcAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!client.Exists(fullPath))
                {
                    return (DateTime?)null;
                }

                var attrs = client.GetAttributes(fullPath);
                return DateTime.SpecifyKind(attrs.LastWriteTimeUtc, DateTimeKind.Utc);
            }, cancellationToken);
        }
        catch (Exception ex) { throw MapAndWrap(ex); }
    }

    /// <summary>
    /// Writes via a sibling temp path then renames into place when the server allows it.
    /// </summary>
    public async Task UploadTextAtomicAsync(string fullPath, string content, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        var parent = GetParent(fullPath);
        var name = GetName(fullPath);
        var tempPath = Join(parent, $".{name}.xterminal-tmp");

        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
                using (var stream = client.OpenWrite(tempPath))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.SetLength(bytes.Length);
                }

                try
                {
                    if (client.Exists(fullPath))
                    {
                        client.DeleteFile(fullPath);
                    }

                    client.RenameFile(tempPath, fullPath);
                }
                catch
                {
                    // Fallback: overwrite destination directly and clean temp.
                    using var stream = client.OpenWrite(fullPath);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.SetLength(bytes.Length);
                    try
                    {
                        if (client.Exists(tempPath))
                        {
                            client.DeleteFile(tempPath);
                        }
                    }
                    catch { /* ignore */ }
                }
            }, cancellationToken);
        }
        catch (Exception ex) { throw MapAndWrap(ex); }
    }

    public Guid EnqueueUploadFile(string localPath, string remoteDirectory)
    {
        var name = Path.GetFileName(localPath);
        var dest = Join(remoteDirectory, name);
        var job = new TransferJob(
            Guid.NewGuid(),
            SftpTransferDirection.Upload,
            name,
            localPath,
            dest,
            File.Exists(localPath) ? new FileInfo(localPath).Length : 0);
        _transfers[job.Id] = job;
        TransfersChanged?.Invoke();
        _ = RunTransferAsync(job);
        return job.Id;
    }

    public Guid EnqueueUploadFolder(string localDirectory, string remoteDirectory)
    {
        var name = new DirectoryInfo(localDirectory).Name;
        var destRoot = Join(remoteDirectory, name);
        var job = new TransferJob(
            Guid.NewGuid(),
            SftpTransferDirection.Upload,
            name,
            localDirectory,
            destRoot,
            EstimateLocalFolderSize(localDirectory),
            isFolder: true);
        _transfers[job.Id] = job;
        TransfersChanged?.Invoke();
        _ = RunTransferAsync(job);
        return job.Id;
    }

    public Guid EnqueueDownload(string remotePath, string localPath, long size, bool isDirectory)
    {
        var name = GetName(remotePath);
        var job = new TransferJob(
            Guid.NewGuid(),
            SftpTransferDirection.Download,
            name,
            remotePath,
            localPath,
            size,
            isFolder: isDirectory);
        _transfers[job.Id] = job;
        TransfersChanged?.Invoke();
        _ = RunTransferAsync(job);
        return job.Id;
    }

    public void CancelTransfer(Guid id)
    {
        if (_transfers.TryGetValue(id, out var job))
        {
            job.Cancel();
            Publish(job);
        }
    }

    public void RetryTransfer(Guid id)
    {
        if (!_transfers.TryGetValue(id, out var old))
        {
            return;
        }

        if (old.State is not (SftpTransferState.Failed or SftpTransferState.Cancelled))
        {
            return;
        }

        var job = old.CloneForRetry();
        _transfers[job.Id] = job;
        _transfers.TryRemove(old.Id, out _);
        TransfersChanged?.Invoke();
        _ = RunTransferAsync(job);
    }

    public void ClearCompletedTransfers()
    {
        foreach (var id in _transfers.Where(kv =>
                     kv.Value.State is SftpTransferState.Complete or SftpTransferState.Cancelled)
                 .Select(kv => kv.Key).ToList())
        {
            _transfers.TryRemove(id, out _);
        }

        TransfersChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var job in _transfers.Values)
        {
            job.Cancel();
        }

        await DisconnectAsync();
        _opLock.Dispose();
    }

    private async Task RunTransferAsync(TransferJob job)
    {
        job.State = SftpTransferState.Transferring;
        Publish(job);
        var sw = Stopwatch.StartNew();

        try
        {
            var client = RequireClient();
            if (job.Direction == SftpTransferDirection.Upload)
            {
                if (job.IsFolder)
                {
                    await Task.Run(() => UploadFolder(client, job.Source, job.Destination, job), job.Token);
                }
                else
                {
                    await Task.Run(() =>
                    {
                        using var fs = File.OpenRead(job.Source);
                        client.UploadFile(
                            fs,
                            job.Destination,
                            true,
                            uploaded =>
                            {
                                job.TransferredBytes = (long)uploaded;
                                UpdateSpeed(job, sw);
                                Publish(job);
                            });
                    }, job.Token);
                    job.TransferredBytes = job.TotalBytes;
                }
            }
            else
            {
                if (job.IsFolder)
                {
                    await Task.Run(() => DownloadFolder(client, job.Source, job.Destination, job), job.Token);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(job.Destination)!);
                    await Task.Run(() =>
                    {
                        using var fs = File.Create(job.Destination);
                        client.DownloadFile(job.Source, fs, downloaded =>
                        {
                            job.TransferredBytes = (long)downloaded;
                            UpdateSpeed(job, sw);
                            Publish(job);
                        });
                    }, job.Token);
                    job.TransferredBytes = job.TotalBytes > 0 ? job.TotalBytes : job.TransferredBytes;
                }
            }

            job.Token.ThrowIfCancellationRequested();
            job.State = SftpTransferState.Complete;
            job.Percentage = 100;
            job.ErrorMessage = null;
            job.ErrorKind = SftpErrorKind.None;
        }
        catch (OperationCanceledException)
        {
            job.State = SftpTransferState.Cancelled;
            job.ErrorKind = SftpErrorKind.Cancelled;
            job.ErrorMessage = "Cancelled";
        }
        catch (Exception ex)
        {
            var (kind, msg) = SshErrorMapper.Map(ex);
            job.State = SftpTransferState.Failed;
            job.ErrorKind = kind;
            job.ErrorMessage = msg;
        }

        Publish(job);
        TransfersChanged?.Invoke();
    }

    private void UploadFolder(SftpClient client, string localDir, string remoteDir, TransferJob job)
    {
        EnsureRemoteDirectory(client, remoteDir);
        foreach (var file in Directory.EnumerateFiles(localDir))
        {
            job.Token.ThrowIfCancellationRequested();
            var dest = Join(remoteDir, Path.GetFileName(file));
            using var fs = File.OpenRead(file);
            client.UploadFile(fs, dest, true, uploaded =>
            {
                job.TransferredBytes += 0; // progress updated below via length
            });
            job.TransferredBytes += new FileInfo(file).Length;
            UpdateSpeed(job, Stopwatch.StartNew());
            Publish(job);
        }

        foreach (var dir in Directory.EnumerateDirectories(localDir))
        {
            job.Token.ThrowIfCancellationRequested();
            UploadFolder(client, dir, Join(remoteDir, Path.GetFileName(dir)), job);
        }
    }

    private void DownloadFolder(SftpClient client, string remoteDir, string localDir, TransferJob job)
    {
        Directory.CreateDirectory(localDir);
        foreach (var entry in client.ListDirectory(remoteDir).Where(e => e.Name is not "." and not ".."))
        {
            job.Token.ThrowIfCancellationRequested();
            var localPath = Path.Combine(localDir, entry.Name);
            if (entry.IsDirectory)
            {
                DownloadFolder(client, entry.FullName, localPath, job);
            }
            else
            {
                using var fs = File.Create(localPath);
                client.DownloadFile(entry.FullName, fs);
                job.TransferredBytes += entry.Length;
                Publish(job);
            }
        }
    }

    private static void EnsureRemoteDirectory(SftpClient client, string path)
    {
        path = NormalizePath(path);
        if (client.Exists(path))
        {
            return;
        }

        var parent = GetParent(path);
        if (!string.IsNullOrEmpty(parent) && parent != path)
        {
            EnsureRemoteDirectory(client, parent);
        }

        client.CreateDirectory(path);
    }

    private static void DeleteDirectoryRecursive(SftpClient client, string path, CancellationToken token)
    {
        foreach (var entry in client.ListDirectory(path).Where(e => e.Name is not "." and not ".."))
        {
            token.ThrowIfCancellationRequested();
            if (entry.IsDirectory)
            {
                DeleteDirectoryRecursive(client, entry.FullName, token);
            }
            else
            {
                client.DeleteFile(entry.FullName);
            }
        }

        client.DeleteDirectory(path);
    }

    private static void UpdateSpeed(TransferJob job, Stopwatch sw)
    {
        var seconds = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        job.BytesPerSecond = job.TransferredBytes / seconds;
        job.Percentage = job.TotalBytes <= 0
            ? 0
            : Math.Clamp(job.TransferredBytes * 100.0 / job.TotalBytes, 0, 100);
        if (job.BytesPerSecond > 0 && job.TotalBytes > job.TransferredBytes)
        {
            var remain = job.TotalBytes - job.TransferredBytes;
            job.Eta = TimeSpan.FromSeconds(remain / job.BytesPerSecond);
        }
        else
        {
            job.Eta = null;
        }
    }

    private void Publish(TransferJob job) => TransferUpdated?.Invoke(job.ToSnapshot());

    private SftpClient RequireClient()
    {
        if (_transport?.Client is not { IsConnected: true } client)
        {
            throw new InvalidOperationException("SFTP is not connected.");
        }

        return client;
    }

    private async Task DisposeTransportAsync()
    {
        if (_transport is not null)
        {
            await _transport.DisposeAsync();
            _transport = null;
        }
    }

    private static SftpRemoteEntry MapEntry(ISftpFile file)
    {
        var attrs = file.Attributes;
        var perms = FormatPermissions(file);
        var owner = attrs is null
            ? string.Empty
            : attrs.UserId != 0 || attrs.GroupId != 0
                ? $"{attrs.UserId}:{attrs.GroupId}"
                : string.Empty;

        return new SftpRemoteEntry
        {
            Name = file.Name,
            FullPath = NormalizePath(file.FullName),
            IsDirectory = file.IsDirectory,
            Size = file.IsDirectory ? 0 : file.Length,
            ModifiedUtc = DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc),
            Permissions = perms,
            Owner = owner,
            Group = attrs?.GroupId.ToString() ?? string.Empty
        };
    }

    private static string FormatPermissions(ISftpFile file)
    {
        var a = file.Attributes;
        if (a is null)
        {
            return file.IsDirectory ? "d?????????" : "-?????????";
        }

        var sb = new StringBuilder(10);
        sb.Append(file.IsDirectory ? 'd' : file.IsSymbolicLink ? 'l' : '-');
        sb.Append(a.OwnerCanRead ? 'r' : '-');
        sb.Append(a.OwnerCanWrite ? 'w' : '-');
        sb.Append(a.OwnerCanExecute ? 'x' : '-');
        sb.Append(a.GroupCanRead ? 'r' : '-');
        sb.Append(a.GroupCanWrite ? 'w' : '-');
        sb.Append(a.GroupCanExecute ? 'x' : '-');
        sb.Append(a.OthersCanRead ? 'r' : '-');
        sb.Append(a.OthersCanWrite ? 'w' : '-');
        sb.Append(a.OthersCanExecute ? 'x' : '-');
        return sb.ToString();
    }

    private static long EstimateLocalFolderSize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        catch
        {
            return 0;
        }
    }

    private static InvalidOperationException MapAndWrap(Exception ex)
    {
        var (kind, msg) = SshErrorMapper.Map(ex);
        return new InvalidOperationException(FormatError(kind, msg), ex);
    }

    private static string FormatError(SftpErrorKind kind, string message) => kind switch
    {
        SftpErrorKind.PermissionDenied => "Permission denied",
        SftpErrorKind.PathNotFound => "Path does not exist",
        SftpErrorKind.ConnectionLost => "Connection lost",
        SftpErrorKind.DiskUnavailable => "Disk unavailable",
        SftpErrorKind.Timeout => "Timeout",
        SftpErrorKind.Cancelled => "Cancelled",
        _ => message
    };

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        path = path.Replace('\\', '/').Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        while (path.Contains("//", StringComparison.Ordinal))
        {
            path = path.Replace("//", "/", StringComparison.Ordinal);
        }

        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path.TrimEnd('/');
        }

        return path;
    }

    public static string Join(string parent, string name)
    {
        parent = NormalizePath(parent);
        name = name.Replace('\\', '/').Trim('/');
        return parent == "/" ? "/" + name : parent + "/" + name;
    }

    public static string GetParent(string path)
    {
        path = NormalizePath(path);
        if (path == "/")
        {
            return "/";
        }

        var idx = path.LastIndexOf('/');
        return idx <= 0 ? "/" : path[..idx];
    }

    public static string GetName(string path)
    {
        path = NormalizePath(path);
        if (path == "/")
        {
            return "/";
        }

        var idx = path.LastIndexOf('/');
        return idx < 0 ? path : path[(idx + 1)..];
    }

    private sealed class TransferJob
    {
        private readonly CancellationTokenSource _cts = new();

        public TransferJob(
            Guid id,
            SftpTransferDirection direction,
            string fileName,
            string source,
            string destination,
            long totalBytes,
            bool isFolder = false)
        {
            Id = id;
            Direction = direction;
            FileName = fileName;
            Source = source;
            Destination = destination;
            TotalBytes = totalBytes;
            IsFolder = isFolder;
        }

        public Guid Id { get; }
        public SftpTransferDirection Direction { get; }
        public string FileName { get; }
        public string Source { get; }
        public string Destination { get; }
        public long TotalBytes { get; set; }
        public long TransferredBytes { get; set; }
        public double Percentage { get; set; }
        public double BytesPerSecond { get; set; }
        public TimeSpan? Eta { get; set; }
        public SftpTransferState State { get; set; } = SftpTransferState.Queued;
        public string? ErrorMessage { get; set; }
        public SftpErrorKind ErrorKind { get; set; }
        public bool IsFolder { get; }
        public CancellationToken Token => _cts.Token;

        public void Cancel() => _cts.Cancel();

        public TransferJob CloneForRetry() => new(
            Guid.NewGuid(), Direction, FileName, Source, Destination, TotalBytes, IsFolder);

        public SftpTransferSnapshot ToSnapshot() => new()
        {
            Id = Id,
            State = State,
            Direction = Direction,
            FileName = FileName,
            Source = Source,
            Destination = Destination,
            TransferredBytes = TransferredBytes,
            TotalBytes = TotalBytes,
            Percentage = Percentage,
            BytesPerSecond = BytesPerSecond,
            Eta = Eta,
            ErrorMessage = ErrorMessage,
            ErrorKind = ErrorKind
        };
    }
}
