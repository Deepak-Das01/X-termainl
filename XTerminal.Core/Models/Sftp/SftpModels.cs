namespace XTerminal.Core.Models.Sftp;

public enum SftpTransferState
{
    Queued,
    Transferring,
    Complete,
    Failed,
    Cancelled
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
    public long Size { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public string Permissions { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
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
    public long TotalBytes { get; init; }
    public double Percentage { get; init; }
    public double BytesPerSecond { get; init; }
    public TimeSpan? Eta { get; init; }
    public string? ErrorMessage { get; init; }
    public SftpErrorKind ErrorKind { get; init; }
}
