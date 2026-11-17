namespace XTerminal.Core.Models.Sftp;

public enum SftpTransferState
{
    Queued,
    Transferring,
    Complete,
    Failed,
    Cancelled,
    Skipped
}

public enum SftpTransferDirection
{
    Upload,
    Download
}

public enum SftpErrorKind
{
    None,
    PermissionDenied,
    PathNotFound,
    ConnectionLost,
    DiskUnavailable,
    Timeout,
    Cancelled,
    Unknown
}

public sealed class SftpRemoteEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsSymlink { get; init; }
    public long Size { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public string Permissions { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public short? ModeBits { get; init; }
}

public sealed class SftpTransferSnapshot
{
    public Guid Id { get; init; }
    public SftpTransferState State { get; init; }
    public SftpTransferDirection Direction { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public long TransferredBytes { get; init; }
    /// <summary>-1 means unknown total size.</summary>
    public long TotalBytes { get; init; }
    public double Percentage { get; init; }
    public double BytesPerSecond { get; init; }
    public TimeSpan? Eta { get; init; }
    public TimeSpan? Elapsed { get; init; }
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public SftpErrorKind ErrorKind { get; init; }
    public bool TotalSizeUnknown { get; init; }
}

public sealed class TransferConflictInfo
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required string SourceName { get; init; }
    public required string DestinationName { get; init; }
    public long? SourceSize { get; init; }
    public long? DestinationSize { get; init; }
    public DateTimeOffset? SourceModifiedUtc { get; init; }
    public DateTimeOffset? DestinationModifiedUtc { get; init; }
    public bool DestinationIsDirectory { get; init; }
    public SftpTransferDirection Direction { get; init; }
}
