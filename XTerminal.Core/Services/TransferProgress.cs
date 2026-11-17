namespace XTerminal.Core.Services;

/// <summary>Pure transfer metric helpers — no invented speeds; N/A when data is insufficient.</summary>
public static class TransferProgress
{
    public const long UnknownTotal = -1;

    public static double Percentage(long transferred, long total)
    {
        if (total == UnknownTotal) return 0;
        if (total <= 0) return transferred <= 0 ? 100 : 0;
        return Math.Clamp(transferred * 100.0 / total, 0, 99.9);
    }

    /// <summary>Final percentage only after successful completion.</summary>
    public static double CompletedPercentage(long transferred, long total)
    {
        if (total == UnknownTotal) return 100;
        if (total <= 0) return 100;
        return Math.Clamp(transferred * 100.0 / total, 0, 100);
    }

    public static double? BytesPerSecond(long transferred, TimeSpan elapsed)
    {
        if (transferred < 0) return null;
        if (elapsed.TotalSeconds < 0.05) return null;
        return transferred / elapsed.TotalSeconds;
    }

    public static TimeSpan? EstimateRemaining(long transferred, long total, double? bytesPerSecond)
    {
        if (total is UnknownTotal or <= 0) return null;
        if (bytesPerSecond is null or <= 0) return null;
        if (transferred >= total) return TimeSpan.Zero;
        var remain = total - transferred;
        var seconds = remain / bytesPerSecond.Value;
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return null;
        if (seconds > TimeSpan.FromDays(7).TotalSeconds) return null;
        return TimeSpan.FromSeconds(seconds);
    }

    public static string FormatSpeed(double? bytesPerSecond) =>
        bytesPerSecond is null or <= 0 ? "N/A" : FormatBytes((long)bytesPerSecond.Value) + "/s";

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "N/A";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }

        return u == 0 ? $"{bytes} {units[u]}" : $"{v:0.##} {units[u]}";
    }
}
