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
    private const int AbsoluteMaxConcurrent = 4;
    private const int PublishThrottleMs = 100;

    private readonly ISshSessionLibrary _library;
    private readonly SshTransportFactory _factory;
    private readonly ConcurrentDictionary<Guid, TransferJob> _transfers = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _opLock = new(1, 1);

    // SSH.NET SftpClient is not thread-safe for concurrent ops on one instance.
    // Default MaxConcurrentTransfers = 1; property allows 1–4 (clamped) via SemaphoreSlim.
    private readonly SemaphoreSlim _transferSlots;
    private int _maxConcurrentTransfers = 1;
    private int _reservedIdleSlots;

    private SftpTransport? _transport;
    private SshSessionRecord? _session;
    private bool _disposed;
    private bool _transportDisposed = true;

    public SftpSessionService(ISshSessionLibrary library, SshTransportFactory factory)
    {
        _library = library;
        _factory = factory;
        // Start with AbsoluteMax permits, then reserve idle slots so only 1 remains available.
        _transferSlots = new SemaphoreSlim(AbsoluteMaxConcurrent, AbsoluteMaxConcurrent);
        _reservedIdleSlots = AbsoluteMaxConcurrent - _maxConcurrentTransfers;
        for (var i = 0; i < _reservedIdleSlots; i++)
        {
            _transferSlots.Wait();
        }
    }

    public Guid? SessionId => _session?.Id;
    public string? SessionName => _session?.Name;
    public string? Host => _session?.Host;
    public string? Username => _session?.Username;
    public bool IsConnected => _transport?.IsConnected == true;
    public string? StatusMessage { get; private set; }
    public string WorkingDirectory { get; private set; } = "/";
    public string HomeDirectory { get; private set; } = "/";

    /// <summary>Default conflict policy when the destination already exists.</summary>
    public TransferConflictAction ConflictPolicy { get; set; } = TransferConflictAction.Ask;

    /// <summary>
    /// Optional UI callback when <see cref="ConflictPolicy"/> is <see cref="TransferConflictAction.Ask"/>.
    /// Return Overwrite, Skip, or Rename. Throwing <see cref="OperationCanceledException"/> marks the transfer Cancelled.
    /// </summary>
    public Func<TransferConflictInfo, TransferConflictAction>? ConflictResolver { get; set; }

    /// <summary>
    /// Max parallel transfers on this session (clamped 1–4). Default is 1 because SSH.NET's
    /// <see cref="SftpClient"/> is not safe for concurrent operations on a single instance.
    /// </summary>
    public int MaxConcurrentTransfers
    {
        get => _maxConcurrentTransfers;
        set
        {
            var n = Math.Clamp(value, 1, AbsoluteMaxConcurrent);
            lock (_gate)
            {
                if (n == _maxConcurrentTransfers)
                {
                    return;
                }

                var desiredReserved = AbsoluteMaxConcurrent - n;
                while (_reservedIdleSlots < desiredReserved)
                {
                    // Shrinking concurrency: reserve an extra idle slot (may block briefly if all busy).
                    if (_transferSlots.Wait(0))
                    {
                        _reservedIdleSlots++;
                    }
                    else
                    {
                        // Slots busy — apply remaining reservation asynchronously when released.
                        break;
                    }
                }

                while (_reservedIdleSlots > desiredReserved)
                {
                    _reservedIdleSlots--;
                    _transferSlots.Release();
                }

                _maxConcurrentTransfers = n;
            }
        }
    }

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
            ObjectDisposedException.ThrowIf(_disposed, this);

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

            // Host-key verification is enforced inside OpenSftpAsync.
            _transport = await _factory.OpenSftpAsync(_session, cancellationToken);
            _transportDisposed = false;
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
            CancelAllTransfers();
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
        var full = SftpPathSafety.CombineRemote(parentPath, name);
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
        var full = SftpPathSafety.CombineRemote(parentPath, name);
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
        fullPath = SftpPathSafety.NormalizeRemote(fullPath);
        var parent = GetParent(fullPath);
        var dest = SftpPathSafety.CombineRemote(parent, newName);
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

    public async Task DeleteAsync(string fullPath, bool isDirectory, CancellationToken cancellationToken = default) =>
        await DeleteAsync(fullPath, isDirectory, allowDangerous: false, cancellationToken);

    /// <summary>
    /// Deletes a remote path. Refuses critical roots unless <paramref name="allowDangerous"/> is true.
    /// Never follows symlinks during recursive directory delete.
    /// </summary>
    public async Task DeleteAsync(
        string fullPath,
        bool isDirectory,
        bool allowDangerous,
        CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        fullPath = SftpPathSafety.NormalizeRemote(fullPath);

        if (!allowDangerous && SftpPathSafety.IsDangerousRemoteDeleteTarget(fullPath))
        {
            throw new InvalidOperationException(
                $"Refusing to delete critical remote path '{fullPath}'. " +
                "Pass allowDangerous: true only when the user explicitly confirms.");
        }

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

    /// <summary>
    /// Sets Unix permission bits on a remote path (chmod). Does not change ownership (no chown).
    /// </summary>
    public async Task SetPermissionsAsync(string path, short mode, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        path = SftpPathSafety.NormalizeRemote(path);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                // chmod only — never chown / change owner or group.
                client.ChangePermissions(path, mode);
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
                // Do not log file contents.
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
    /// Does not log file contents.
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
        long total = 0;
        try
        {
            if (File.Exists(localPath))
            {
                total = new FileInfo(localPath).Length;
            }
        }
        catch { /* keep 0 */ }

        var job = new TransferJob(
            Guid.NewGuid(),
            SftpTransferDirection.Upload,
            name,
            localPath,
            dest,
            total);
        return Enqueue(job);
    }

    public Guid EnqueueUploadFolder(string localDirectory, string remoteDirectory)
    {
        var name = new DirectoryInfo(localDirectory).Name;
        var destRoot = Join(remoteDirectory, name);
        var estimated = EstimateLocalFolderSize(localDirectory);
        var job = new TransferJob(
            Guid.NewGuid(),
            SftpTransferDirection.Upload,
            name,
            localDirectory,
            destRoot,
            estimated >= 0 ? estimated : TransferProgress.UnknownTotal,
            isFolder: true);
        return Enqueue(job);
    }

    public Guid EnqueueDownload(string remotePath, string localPath, long size, bool isDirectory)
    {
        var name = GetName(remotePath);
        var total = isDirectory
            ? (size > 0 ? size : TransferProgress.UnknownTotal)
            : size;
        var job = new TransferJob(
            Guid.NewGuid(),
            SftpTransferDirection.Download,
            name,
            remotePath,
            localPath,
            total,
            isFolder: isDirectory);
        return Enqueue(job);
    }

    public void CancelTransfer(Guid id)
    {
        if (!_transfers.TryGetValue(id, out var job))
        {
            return;
        }

        if (!TransferStateMachine.CanCancel(job.State))
        {
            return;
        }

        job.Cancel();
        if (job.State == SftpTransferState.Queued)
        {
            TrySetState(job, SftpTransferState.Cancelled);
            job.ErrorKind = SftpErrorKind.Cancelled;
            job.ErrorMessage = "Cancelled";
            job.CompletedUtc = DateTimeOffset.UtcNow;
        }

        Publish(job, force: true);
        TransfersChanged?.Invoke();
    }

    public void CancelAllTransfers()
    {
        foreach (var job in _transfers.Values)
        {
            if (!TransferStateMachine.CanCancel(job.State))
            {
                continue;
            }

            job.Cancel();
            if (job.State == SftpTransferState.Queued)
            {
                TrySetState(job, SftpTransferState.Cancelled);
                job.ErrorKind = SftpErrorKind.Cancelled;
                job.ErrorMessage = "Cancelled";
                job.CompletedUtc = DateTimeOffset.UtcNow;
                Publish(job, force: true);
            }
        }

        TransfersChanged?.Invoke();
    }

    public void RemoveTransfer(Guid id)
    {
        if (!_transfers.TryGetValue(id, out var job))
        {
            return;
        }

        if (!TransferStateMachine.CanRemove(job.State))
        {
            return;
        }

        if (job.State == SftpTransferState.Queued)
        {
            job.Cancel();
            TrySetState(job, SftpTransferState.Cancelled);
        }

        _transfers.TryRemove(id, out _);
        TransfersChanged?.Invoke();
    }

    public void RetryTransfer(Guid id)
    {
        if (!_transfers.TryGetValue(id, out var old))
        {
            return;
        }

        if (!TransferStateMachine.CanRetry(old.State))
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
        foreach (var id in _transfers.Where(kv => TransferStateMachine.CanRemove(kv.Value.State)
                                                  && kv.Value.State != SftpTransferState.Queued)
                     .Select(kv => kv.Key).ToList())
        {
            _transfers.TryRemove(id, out _);
        }

        TransfersChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelAllTransfers();
        await DisposeTransportAsync();
        StatusMessage = "Disconnected";
        try
        {
            ConnectionChanged?.Invoke();
        }
        catch { /* ignore listener errors during dispose */ }

        _opLock.Dispose();
        _transferSlots.Dispose();
    }

    private Guid Enqueue(TransferJob job)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _transfers[job.Id] = job;
        TransfersChanged?.Invoke();
        Publish(job, force: true);
        _ = RunTransferAsync(job);
        return job.Id;
    }

    private async Task RunTransferAsync(TransferJob job)
    {
        var sw = Stopwatch.StartNew();
        job.Stopwatch = sw;
        var slotAcquired = false;

        try
        {
            await _transferSlots.WaitAsync(job.Token);
            slotAcquired = true;

            // Reconcile reserved idle slots if MaxConcurrentTransfers was lowered while busy.
            ReconcileReservedSlots();

            if (job.Token.IsCancellationRequested)
            {
                TrySetState(job, SftpTransferState.Cancelled);
                job.ErrorKind = SftpErrorKind.Cancelled;
                job.ErrorMessage = "Cancelled";
                job.CompletedUtc = DateTimeOffset.UtcNow;
                return;
            }

            TrySetState(job, SftpTransferState.Transferring);
            job.StartedUtc = DateTimeOffset.UtcNow;
            job.CompletedUtc = null;
            Publish(job, force: true);

            var client = RequireClient();
            if (job.Direction == SftpTransferDirection.Upload)
            {
                if (job.IsFolder)
                {
                    await Task.Run(() => UploadFolder(client, job.Source, job.Destination, job, destinationRoot: job.Destination), job.Token);
                }
                else
                {
                    var resolved = await ResolveUploadConflictAsync(client, job);
                    if (!resolved)
                    {
                        return;
                    }

                    await Task.Run(() => UploadFileAtomic(client, job), job.Token);
                }
            }
            else
            {
                if (job.IsFolder)
                {
                    await Task.Run(() => DownloadFolder(client, job.Source, job.Destination, job, destinationRoot: job.Destination), job.Token);
                }
                else
                {
                    await Task.Run(() => DownloadFileAtomic(client, job, destinationRoot: Path.GetDirectoryName(job.Destination) ?? job.Destination), job.Token);
                }
            }

            job.Token.ThrowIfCancellationRequested();
            MarkComplete(job);
        }
        catch (OperationCanceledException)
        {
            TrySetState(job, SftpTransferState.Cancelled);
            job.ErrorKind = SftpErrorKind.Cancelled;
            job.ErrorMessage = "Cancelled";
            job.CompletedUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            var (kind, msg) = SshErrorMapper.Map(ex);
            TrySetState(job, SftpTransferState.Failed);
            job.ErrorKind = kind;
            job.ErrorMessage = msg;
            job.CompletedUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            sw.Stop();
            job.Elapsed = sw.Elapsed;
            if (slotAcquired)
            {
                try
                {
                    _transferSlots.Release();
                }
                catch (ObjectDisposedException) { /* shutting down */ }

                ReconcileReservedSlots();
            }

            Publish(job, force: true);
            TransfersChanged?.Invoke();
        }
    }

    private void ReconcileReservedSlots()
    {
        lock (_gate)
        {
            var desiredReserved = AbsoluteMaxConcurrent - _maxConcurrentTransfers;
            while (_reservedIdleSlots < desiredReserved && _transferSlots.Wait(0))
            {
                _reservedIdleSlots++;
            }
        }
    }

    private void MarkComplete(TransferJob job)
    {
        TrySetState(job, SftpTransferState.Complete);
        job.Percentage = TransferProgress.CompletedPercentage(job.TransferredBytes, job.TotalBytes);
        if (job.TotalBytes >= 0 && job.TransferredBytes < job.TotalBytes)
        {
            job.TransferredBytes = job.TotalBytes;
        }

        job.ErrorMessage = null;
        job.ErrorKind = SftpErrorKind.None;
        job.CompletedUtc = DateTimeOffset.UtcNow;
        job.Eta = null;
    }

    private static void TrySetState(TransferJob job, SftpTransferState next)
    {
        var state = job.State;
        if (!TransferStateMachine.CanTransition(state, next))
        {
            return;
        }

        TransferStateMachine.Transition(ref state, next);
        job.State = state;
    }

    /// <summary>
    /// Applies conflict policy for a single-file upload. Returns false when skipped/cancelled.
    /// </summary>
    private async Task<bool> ResolveUploadConflictAsync(SftpClient client, TransferJob job)
    {
        var dest = SftpPathSafety.NormalizeRemote(job.Destination);
        job.Destination = dest;

        SftpFileAttributes? existingAttrs = null;
        var exists = await Task.Run(() =>
        {
            if (!client.Exists(dest))
            {
                return false;
            }

            try { existingAttrs = client.GetAttributes(dest); }
            catch { /* existence alone is enough */ }
            return true;
        }, job.Token);

        if (!exists)
        {
            return true;
        }

        DateTimeOffset? sourceModified = null;
        long? sourceSize = null;
        try
        {
            var fi = new FileInfo(job.Source);
            if (fi.Exists)
            {
                sourceSize = fi.Length;
                sourceModified = fi.LastWriteTimeUtc;
            }
        }
        catch { /* ignore */ }

        var action = await ResolveConflictAsync(new TransferConflictInfo
        {
            SourcePath = job.Source,
            DestinationPath = dest,
            SourceName = Path.GetFileName(job.Source),
            DestinationName = GetName(dest),
            SourceSize = sourceSize,
            DestinationSize = existingAttrs?.Size,
            SourceModifiedUtc = sourceModified,
            DestinationModifiedUtc = existingAttrs is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(existingAttrs.LastWriteTimeUtc, DateTimeKind.Utc)),
            DestinationIsDirectory = existingAttrs?.IsDirectory == true,
            Direction = SftpTransferDirection.Upload
        }, job);

        switch (action)
        {
            case TransferConflictAction.Overwrite:
                job.Overwrite = true;
                return true;

            case TransferConflictAction.Skip:
                MarkSkipped(job, $"Skipped — remote file already exists: {dest}");
                return false;

            case TransferConflictAction.Rename:
            {
                var parent = GetParent(dest);
                var originalName = GetName(dest);
                var unique = SftpPathSafety.UniqueName(
                    originalName,
                    candidate => client.Exists(Join(parent, candidate)));
                job.Destination = Join(parent, unique);
                job.FileName = unique;
                job.Overwrite = false;
                return true;
            }

            default:
                throw new InvalidOperationException($"Remote file already exists: {dest}");
        }
    }

    private async Task<bool> ResolveDownloadConflictAsync(TransferJob job, string localPath)
    {
        if (!File.Exists(localPath))
        {
            return true;
        }

        DateTimeOffset? sourceModified = null;
        long? sourceSize = job.TotalBytes >= 0 ? job.TotalBytes : null;
        try
        {
            var client = RequireClient();
            var attrs = await Task.Run(() => client.GetAttributes(job.Source), job.Token);
            sourceSize = attrs.Size;
            sourceModified = new DateTimeOffset(DateTime.SpecifyKind(attrs.LastWriteTimeUtc, DateTimeKind.Utc));
        }
        catch { /* ignore */ }

        DateTimeOffset? destModified = null;
        long? destSize = null;
        try
        {
            var fi = new FileInfo(localPath);
            destSize = fi.Length;
            destModified = fi.LastWriteTimeUtc;
        }
        catch { /* ignore */ }

        var action = await ResolveConflictAsync(new TransferConflictInfo
        {
            SourcePath = job.Source,
            DestinationPath = localPath,
            SourceName = GetName(job.Source),
            DestinationName = Path.GetFileName(localPath),
            SourceSize = sourceSize,
            DestinationSize = destSize,
            SourceModifiedUtc = sourceModified,
            DestinationModifiedUtc = destModified,
            DestinationIsDirectory = false,
            Direction = SftpTransferDirection.Download
        }, job);

        switch (action)
        {
            case TransferConflictAction.Overwrite:
                return true;

            case TransferConflictAction.Skip:
                MarkSkipped(job, $"Skipped — local file already exists: {localPath}");
                return false;

            case TransferConflictAction.Rename:
            {
                var dir = Path.GetDirectoryName(localPath) ?? ".";
                var original = Path.GetFileName(localPath);
                var unique = SftpPathSafety.UniqueName(
                    original,
                    candidate => File.Exists(Path.Combine(dir, candidate)));
                job.Destination = Path.Combine(dir, unique);
                job.FileName = unique;
                return true;
            }

            default:
                throw new InvalidOperationException($"Local file already exists: {localPath}");
        }
    }

    private Task<TransferConflictAction> ResolveConflictAsync(TransferConflictInfo info, TransferJob job)
    {
        var action = ConflictPolicy;
        if (action != TransferConflictAction.Ask)
        {
            return Task.FromResult(action);
        }

        if (ConflictResolver is null)
        {
            throw new InvalidOperationException(
                $"Destination already exists: {info.DestinationPath}. Set ConflictPolicy or provide ConflictResolver.");
        }

        try
        {
            action = ConflictResolver(info);
        }
        catch (OperationCanceledException)
        {
            TrySetState(job, SftpTransferState.Cancelled);
            job.ErrorKind = SftpErrorKind.Cancelled;
            job.ErrorMessage = "Cancelled";
            job.CompletedUtc = DateTimeOffset.UtcNow;
            throw;
        }

        if (action == TransferConflictAction.Ask)
        {
            throw new InvalidOperationException($"Destination already exists: {info.DestinationPath}");
        }

        return Task.FromResult(action);
    }

    private static void MarkSkipped(TransferJob job, string message)
    {
        TrySetState(job, SftpTransferState.Skipped);
        job.ErrorKind = SftpErrorKind.Cancelled;
        job.ErrorMessage = message;
        job.Percentage = 0;
        job.CompletedUtc = DateTimeOffset.UtcNow;
    }

    private void UploadFileAtomic(SftpClient client, TransferJob job)
    {
        job.Token.ThrowIfCancellationRequested();
        var dest = SftpPathSafety.NormalizeRemote(job.Destination);
        var parent = GetParent(dest);
        var name = GetName(dest);
        var tempRemote = Join(parent, $".{name}.xterminal-tmp");
        var sw = job.Stopwatch ?? Stopwatch.StartNew();

        void OnProgress(ulong uploaded)
        {
            job.TransferredBytes = (long)uploaded;
            UpdateMetrics(job, sw);
            Publish(job);
        }

        using var fs = File.OpenRead(job.Source);

        if (job.Overwrite)
        {
            try
            {
                // Prefer temp sibling then rename when overwriting.
                client.UploadFile(fs, tempRemote, canOverride: true, OnProgress);
                job.Token.ThrowIfCancellationRequested();
                try
                {
                    if (client.Exists(dest))
                    {
                        client.DeleteFile(dest);
                    }

                    client.RenameFile(tempRemote, dest);
                }
                catch
                {
                    // Fallback: direct overwrite.
                    try
                    {
                        if (client.Exists(tempRemote))
                        {
                            client.DeleteFile(tempRemote);
                        }
                    }
                    catch { /* ignore */ }

                    fs.Position = 0;
                    client.UploadFile(fs, dest, canOverride: true, OnProgress);
                }
            }
            catch
            {
                try
                {
                    if (client.Exists(tempRemote))
                    {
                        client.DeleteFile(tempRemote);
                    }
                }
                catch { /* ignore */ }

                throw;
            }
        }
        else
        {
            client.UploadFile(fs, dest, canOverride: false, OnProgress);
        }

        if (job.TotalBytes >= 0)
        {
            job.TransferredBytes = job.TotalBytes;
        }

        UpdateMetrics(job, sw);
    }

    private void UploadFolder(SftpClient client, string localDir, string remoteDir, TransferJob job, string destinationRoot)
    {
        if (SftpPathSafety.IsReparsePoint(localDir))
        {
            // Do not recurse into local reparse points / symlinks.
            return;
        }

        EnsureRemoteDirectory(client, remoteDir);
        var sw = job.Stopwatch ?? Stopwatch.StartNew();

        foreach (var file in Directory.EnumerateFiles(localDir))
        {
            job.Token.ThrowIfCancellationRequested();
            if (SftpPathSafety.IsReparsePoint(file))
            {
                continue;
            }

            var fileName = Path.GetFileName(file);
            var dest = Join(remoteDir, fileName);
            long fileSize = 0;
            try { fileSize = new FileInfo(file).Length; }
            catch { /* ignore */ }

            var overwrite = false;
            if (client.Exists(dest))
            {
                DateTimeOffset? sourceModified = null;
                try { sourceModified = File.GetLastWriteTimeUtc(file); }
                catch { /* ignore */ }

                SftpFileAttributes? attrs = null;
                try { attrs = client.GetAttributes(dest); }
                catch { /* ignore */ }

                var action = ResolveConflictAsync(new TransferConflictInfo
                {
                    SourcePath = file,
                    DestinationPath = dest,
                    SourceName = fileName,
                    DestinationName = fileName,
                    SourceSize = fileSize,
                    DestinationSize = attrs?.Size,
                    SourceModifiedUtc = sourceModified,
                    DestinationModifiedUtc = attrs is null
                        ? null
                        : new DateTimeOffset(DateTime.SpecifyKind(attrs.LastWriteTimeUtc, DateTimeKind.Utc)),
                    DestinationIsDirectory = attrs?.IsDirectory == true,
                    Direction = SftpTransferDirection.Upload
                }, job).GetAwaiter().GetResult();

                if (action == TransferConflictAction.Skip)
                {
                    continue;
                }

                if (action == TransferConflictAction.Rename)
                {
                    var unique = SftpPathSafety.UniqueName(
                        fileName,
                        candidate => client.Exists(Join(remoteDir, candidate)));
                    dest = Join(remoteDir, unique);
                }
                else
                {
                    overwrite = true;
                }
            }

            var tempRemote = Join(remoteDir, $".{GetName(dest)}.xterminal-tmp");
            using (var fs = File.OpenRead(file))
            {
                if (overwrite)
                {
                    try
                    {
                        client.UploadFile(fs, tempRemote, canOverride: true);
                        try
                        {
                            if (client.Exists(dest))
                            {
                                client.DeleteFile(dest);
                            }

                            client.RenameFile(tempRemote, dest);
                        }
                        catch
                        {
                            try
                            {
                                if (client.Exists(tempRemote))
                                {
                                    client.DeleteFile(tempRemote);
                                }
                            }
                            catch { /* ignore */ }

                            fs.Position = 0;
                            client.UploadFile(fs, dest, canOverride: true);
                        }
                    }
                    catch
                    {
                        try
                        {
                            if (client.Exists(tempRemote))
                            {
                                client.DeleteFile(tempRemote);
                            }
                        }
                        catch { /* ignore */ }

                        throw;
                    }
                }
                else
                {
                    client.UploadFile(fs, dest, canOverride: false);
                }
            }

            job.TransferredBytes += fileSize;
            UpdateMetrics(job, sw);
            Publish(job);
        }

        foreach (var dir in Directory.EnumerateDirectories(localDir))
        {
            job.Token.ThrowIfCancellationRequested();
            if (SftpPathSafety.IsReparsePoint(dir))
            {
                continue;
            }

            UploadFolder(client, dir, Join(remoteDir, Path.GetFileName(dir)), job, destinationRoot);
        }
    }

    private void DownloadFileAtomic(SftpClient client, TransferJob job, string destinationRoot)
    {
        job.Token.ThrowIfCancellationRequested();

        // Single-file download: Destination is the final local path chosen by the user.
        var finalPath = job.Destination;
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        if (!ResolveDownloadConflictAsync(job, finalPath).GetAwaiter().GetResult())
        {
            return;
        }

        finalPath = job.Destination;
        var tempPath = SftpPathSafety.TempSiblingPath(finalPath);
        var sw = job.Stopwatch ?? Stopwatch.StartNew();

        try
        {
            using (var fs = File.Create(tempPath))
            {
                client.DownloadFile(job.Source, fs, downloaded =>
                {
                    job.TransferredBytes = (long)downloaded;
                    UpdateMetrics(job, sw);
                    Publish(job);
                });
            }

            job.Token.ThrowIfCancellationRequested();
            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }

            File.Move(tempPath, finalPath);

            if (job.TotalBytes >= 0)
            {
                job.TransferredBytes = job.TotalBytes;
            }
            else if (job.TransferredBytes <= 0)
            {
                try { job.TransferredBytes = new FileInfo(finalPath).Length; }
                catch { /* ignore */ }
            }

            UpdateMetrics(job, sw);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch { /* ignore */ }

            throw;
        }
    }

    private void DownloadFolder(SftpClient client, string remoteDir, string localDir, TransferJob job, string destinationRoot)
    {
        Directory.CreateDirectory(localDir);
        var sw = job.Stopwatch ?? Stopwatch.StartNew();

        foreach (var entry in client.ListDirectory(remoteDir).Where(e => e.Name is not "." and not ".."))
        {
            job.Token.ThrowIfCancellationRequested();

            var isSymlink = entry.IsSymbolicLink || entry.Attributes?.IsSymbolicLink == true;
            if (isSymlink)
            {
                // Delete-link / skip-following policy: do not recurse into or download symlink targets.
                continue;
            }

            string localPath;
            try
            {
                localPath = SftpPathSafety.ResolveLocalDownloadPath(destinationRoot, MakeRelativeUnder(destinationRoot, localDir, entry.Name));
            }
            catch
            {
                // Fallback for root folder name itself: combine safely under destinationRoot.
                localPath = SftpPathSafety.ResolveLocalDownloadPath(
                    destinationRoot,
                    Path.GetRelativePath(destinationRoot, Path.Combine(localDir, entry.Name)));
            }

            if (entry.IsDirectory)
            {
                DownloadFolder(client, entry.FullName, localPath, job, destinationRoot);
            }
            else
            {
                if (File.Exists(localPath))
                {
                    TransferConflictAction action;
                    try
                    {
                        var fi = new FileInfo(localPath);
                        action = ResolveConflictAsync(new TransferConflictInfo
                        {
                            SourcePath = entry.FullName,
                            DestinationPath = localPath,
                            SourceName = entry.Name,
                            DestinationName = entry.Name,
                            SourceSize = entry.Length,
                            DestinationSize = fi.Length,
                            SourceModifiedUtc = new DateTimeOffset(DateTime.SpecifyKind(entry.LastWriteTimeUtc, DateTimeKind.Utc)),
                            DestinationModifiedUtc = fi.LastWriteTimeUtc,
                            DestinationIsDirectory = false,
                            Direction = SftpTransferDirection.Download
                        }, job).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }

                    if (action == TransferConflictAction.Skip)
                    {
                        continue;
                    }

                    if (action == TransferConflictAction.Rename)
                    {
                        var dir = Path.GetDirectoryName(localPath) ?? localDir;
                        var unique = SftpPathSafety.UniqueName(
                            entry.Name,
                            candidate => File.Exists(Path.Combine(dir, candidate)));
                        localPath = Path.Combine(dir, unique);
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                var tempPath = SftpPathSafety.TempSiblingPath(localPath);
                try
                {
                    using (var fs = File.Create(tempPath))
                    {
                        client.DownloadFile(entry.FullName, fs);
                    }

                    if (File.Exists(localPath))
                    {
                        File.Delete(localPath);
                    }

                    File.Move(tempPath, localPath);
                }
                catch
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch { /* ignore */ }

                    throw;
                }

                job.TransferredBytes += entry.Length;
                UpdateMetrics(job, sw);
                Publish(job);
            }
        }
    }

    private static string MakeRelativeUnder(string destinationRoot, string localDir, string entryName)
    {
        var combined = Path.Combine(localDir, entryName);
        var relative = Path.GetRelativePath(destinationRoot, combined);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return entryName;
        }

        return relative;
    }

    private static void EnsureRemoteDirectory(SftpClient client, string path)
    {
        path = SftpPathSafety.NormalizeRemote(path);
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
        path = SftpPathSafety.NormalizeRemote(path);
        foreach (var entry in client.ListDirectory(path).Where(e => e.Name is not "." and not ".."))
        {
            token.ThrowIfCancellationRequested();

            // Never follow symlinks — delete the link itself.
            var isSymlink = entry.IsSymbolicLink || entry.Attributes?.IsSymbolicLink == true;
            if (isSymlink)
            {
                client.DeleteFile(entry.FullName);
                continue;
            }

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

    private static void UpdateMetrics(TransferJob job, Stopwatch sw)
    {
        job.Elapsed = sw.Elapsed;
        var bps = TransferProgress.BytesPerSecond(job.TransferredBytes, sw.Elapsed);
        job.BytesPerSecond = bps ?? 0;
        job.Percentage = TransferProgress.Percentage(job.TransferredBytes, job.TotalBytes);
        job.Eta = TransferProgress.EstimateRemaining(job.TransferredBytes, job.TotalBytes, bps);
    }

    private void Publish(TransferJob job, bool force = false)
    {
        if (!force)
        {
            var now = Environment.TickCount64;
            if (now - job.LastPublishTick < PublishThrottleMs)
            {
                return;
            }

            job.LastPublishTick = now;
        }
        else
        {
            job.LastPublishTick = Environment.TickCount64;
        }

        TransferUpdated?.Invoke(job.ToSnapshot());
    }

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
        if (_transportDisposed || _transport is null)
        {
            _transport = null;
            _transportDisposed = true;
            return;
        }

        _transportDisposed = true;
        var transport = _transport;
        _transport = null;
        await transport.DisposeAsync();
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

        short? modeBits = null;
        if (attrs is not null)
        {
            short m = 0;
            if (attrs.OwnerCanRead) m |= 0b100_000_000;
            if (attrs.OwnerCanWrite) m |= 0b010_000_000;
            if (attrs.OwnerCanExecute) m |= 0b001_000_000;
            if (attrs.GroupCanRead) m |= 0b000_100_000;
            if (attrs.GroupCanWrite) m |= 0b000_010_000;
            if (attrs.GroupCanExecute) m |= 0b000_001_000;
            if (attrs.OthersCanRead) m |= 0b000_000_100;
            if (attrs.OthersCanWrite) m |= 0b000_000_010;
            if (attrs.OthersCanExecute) m |= 0b000_000_001;
            modeBits = m;
        }

        var isSymlink = file.IsSymbolicLink || attrs?.IsSymbolicLink == true;

        return new SftpRemoteEntry
        {
            Name = file.Name,
            FullPath = NormalizePath(file.FullName),
            IsDirectory = file.IsDirectory && !isSymlink,
            IsSymlink = isSymlink,
            Size = file.IsDirectory ? 0 : file.Length,
            ModifiedUtc = DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc),
            Permissions = perms,
            Owner = owner,
            Group = attrs?.GroupId.ToString() ?? string.Empty,
            ModeBits = modeBits
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
            long total = 0;
            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                if (SftpPathSafety.IsReparsePoint(dir))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (SftpPathSafety.IsReparsePoint(file))
                    {
                        continue;
                    }

                    try { total += new FileInfo(file).Length; }
                    catch { /* skip */ }
                }

                foreach (var child in Directory.EnumerateDirectories(dir))
                {
                    if (!SftpPathSafety.IsReparsePoint(child))
                    {
                        stack.Push(child);
                    }
                }
            }

            return total;
        }
        catch
        {
            return TransferProgress.UnknownTotal;
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
        private SftpTransferState _state = SftpTransferState.Queued;

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
        public string FileName { get; set; }
        public string Source { get; }
        public string Destination { get; set; }
        public long TotalBytes { get; set; }
        public long TransferredBytes { get; set; }
        public double Percentage { get; set; }
        public double BytesPerSecond { get; set; }
        public TimeSpan? Eta { get; set; }
        public TimeSpan? Elapsed { get; set; }
        public DateTimeOffset? StartedUtc { get; set; }
        public DateTimeOffset? CompletedUtc { get; set; }
        public SftpTransferState State
        {
            get => _state;
            set => _state = value;
        }
        public string? ErrorMessage { get; set; }
        public SftpErrorKind ErrorKind { get; set; }
        public bool IsFolder { get; }
        public bool Overwrite { get; set; }
        public Stopwatch? Stopwatch { get; set; }
        public long LastPublishTick { get; set; }
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
            Elapsed = Elapsed ?? Stopwatch?.Elapsed,
            StartedUtc = StartedUtc,
            CompletedUtc = CompletedUtc,
            ErrorMessage = ErrorMessage,
            ErrorKind = ErrorKind,
            TotalSizeUnknown = TotalBytes == TransferProgress.UnknownTotal
        };
    }
}
