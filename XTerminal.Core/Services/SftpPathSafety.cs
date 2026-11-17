namespace XTerminal.Core.Services;

public enum TransferConflictAction
{
    Ask,
    Overwrite,
    Skip,
    Rename
}

/// <summary>Path validation for untrusted remote names and local download destinations.</summary>
public static class SftpPathSafety
{
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string NormalizeRemote(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        path = path.Replace('\\', '/').Trim();
        if (!path.StartsWith('/')) path = "/" + path;

        var parts = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (parts.Count == 0)
                    throw new InvalidOperationException("Path escapes remote root.");
                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            if (segment.Contains('\0') || segment.Contains(':'))
                throw new InvalidOperationException("Path contains invalid characters.");

            parts.Add(segment);
        }

        return parts.Count == 0 ? "/" : "/" + string.Join('/', parts);
    }

    public static string CombineRemote(string basePath, string name)
    {
        basePath = NormalizeRemote(basePath);
        ValidateRemoteFileName(name);
        name = name.Replace('\\', '/').Trim('/');
        if (name.Contains('/') || name.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid remote file name.");
        return basePath == "/" ? "/" + name : basePath + "/" + name;
    }

    public static void ValidateRemoteFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("File name is empty.");
        if (name.Contains('\0') || name.Contains('/') || name.Contains('\\'))
            throw new InvalidOperationException("File name contains invalid characters.");
        if (name is "." or "..")
            throw new InvalidOperationException("Invalid file name.");
    }

    public static string UniqueName(string name, Func<string, bool> exists)
    {
        if (!exists(name)) return name;
        var ext = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!exists(candidate)) return candidate;
        }

        return $"{stem}-{Guid.NewGuid():N}{ext}";
    }

    public static bool IsSymlink(string? permissionsOrAttributes) =>
        !string.IsNullOrEmpty(permissionsOrAttributes) &&
        (permissionsOrAttributes.StartsWith('l') ||
         permissionsOrAttributes.Contains("link", StringComparison.OrdinalIgnoreCase));

    public static bool IsDangerousRemoteDeleteTarget(string path)
    {
        path = NormalizeRemote(path);
        return path is "/" or "/." or "/.."
               || path.Equals("/root", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/home", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/etc", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/usr", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/bin", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/sbin", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/boot", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/var", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures a download destination stays under <paramref name="destinationRoot"/> and uses a safe file name.
    /// Rejects absolute path injection, ADS streams, and reserved Windows device names.
    /// </summary>
    public static string ResolveLocalDownloadPath(string destinationRoot, string relativeOrFileName)
    {
        if (string.IsNullOrWhiteSpace(destinationRoot))
            throw new InvalidOperationException("Download destination root is required.");

        var rootFull = Path.GetFullPath(destinationRoot);
        if (relativeOrFileName.Contains(':') && !Path.IsPathRooted(relativeOrFileName))
        {
            // Alternate data stream style "file:stream" — reject.
            throw new InvalidOperationException("Alternate data streams are not allowed in download names.");
        }

        if (Path.IsPathRooted(relativeOrFileName))
            throw new InvalidOperationException("Absolute download paths are not allowed as relative names.");

        var normalized = relativeOrFileName.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        foreach (var segment in normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
                throw new InvalidOperationException("Download path escapes the destination directory.");
            ValidateLocalFileName(segment);
        }

        var combined = Path.GetFullPath(Path.Combine(rootFull, normalized));
        var rootWithSep = rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Download path escapes the destination directory.");
        }

        return combined;
    }

    public static void ValidateLocalFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("File name is empty.");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException($"Invalid local file name: {name}");
        if (name.Contains(':'))
            throw new InvalidOperationException("Alternate data streams are not allowed.");

        var stem = Path.GetFileNameWithoutExtension(name);
        if (ReservedWindowsNames.Contains(stem) || ReservedWindowsNames.Contains(name))
            throw new InvalidOperationException($"Reserved Windows device name: {name}");
    }

    public static string TempSiblingPath(string finalPath) =>
        finalPath + ".xterminal-partial";

    public static bool IsReparsePoint(string localPath)
    {
        try
        {
            if (!Directory.Exists(localPath) && !File.Exists(localPath)) return false;
            var attrs = File.GetAttributes(localPath);
            return attrs.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }
}
