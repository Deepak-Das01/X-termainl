using Renci.SshNet.Common;
using XTerminal.Core.Models.Sftp;

namespace XTerminal.Protocols.Ssh;

public static class SshErrorMapper
{
    internal static (SftpErrorKind Kind, string Message) Map(Exception ex)
    {
        var raw = ex.Message ?? "Unknown error";
        var safe = Sanitize(raw);

        if (ex is OperationCanceledException)
        {
            return (SftpErrorKind.Cancelled, "Operation cancelled.");
        }

        if (ex is TimeoutException || safe.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return (SftpErrorKind.Timeout, "The operation timed out.");
        }

        if (ex is SshConnectionException ||
            safe.Contains("connection", StringComparison.OrdinalIgnoreCase) &&
            (safe.Contains("lost", StringComparison.OrdinalIgnoreCase) ||
             safe.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
             safe.Contains("broken", StringComparison.OrdinalIgnoreCase)))
        {
            return (SftpErrorKind.ConnectionLost, "Connection lost.");
        }

        if (safe.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
            safe.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
            safe.Contains("access", StringComparison.OrdinalIgnoreCase))
        {
            return (SftpErrorKind.PermissionDenied, "Permission denied.");
        }

        if (safe.Contains("no such file", StringComparison.OrdinalIgnoreCase) ||
            safe.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            safe.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            return (SftpErrorKind.PathNotFound, "Path does not exist.");
        }

        if (safe.Contains("no space", StringComparison.OrdinalIgnoreCase) ||
            safe.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
            safe.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
            safe.Contains("ENOSPC", StringComparison.OrdinalIgnoreCase))
        {
            return (SftpErrorKind.DiskUnavailable, "Disk unavailable or full.");
        }

        return (SftpErrorKind.Unknown, safe);
    }

    public static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "Unknown error";
        }

        return message
            .Replace("password", "********", StringComparison.OrdinalIgnoreCase)
            .Replace("passphrase", "********", StringComparison.OrdinalIgnoreCase);
    }
}
