namespace XTerminal.Core.Services;

/// <summary>
/// Associates terminal CWD events with an SFTP workspace. Never parses prompt text.
/// Paths are advisory until confirmed via SFTP list/normalize.
/// </summary>
public sealed class SftpDirectorySync
{
    private DateTimeOffset _lastNavUtc = DateTimeOffset.MinValue;
    private string? _lastPath;

    public bool FollowTerminalDirectory { get; set; }
    public TimeSpan Debounce { get; set; } = TimeSpan.FromMilliseconds(350);

    public bool ShouldNavigate(
        Guid terminalSessionId,
        Guid? sftpSessionId,
        string? proposedPath,
        out string normalizedPath,
        out string? rejectReason)
    {
        normalizedPath = "";
        rejectReason = null;

        if (!FollowTerminalDirectory)
        {
            rejectReason = "Directory following is disabled.";
            return false;
        }

        if (sftpSessionId is null || sftpSessionId != terminalSessionId)
        {
            rejectReason = "Terminal and SFTP sessions do not match.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(proposedPath))
        {
            rejectReason = "No working directory event.";
            return false;
        }

        try
        {
            normalizedPath = SftpPathSafety.NormalizeRemote(proposedPath);
        }
        catch (Exception ex)
        {
            rejectReason = ex.Message;
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (string.Equals(normalizedPath, _lastPath, StringComparison.Ordinal) &&
            now - _lastNavUtc < Debounce)
        {
            rejectReason = "Debounced duplicate navigation.";
            return false;
        }

        _lastPath = normalizedPath;
        _lastNavUtc = now;
        return true;
    }

    public void Reset()
    {
        _lastPath = null;
        _lastNavUtc = DateTimeOffset.MinValue;
    }
}
