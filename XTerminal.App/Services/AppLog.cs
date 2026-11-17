using System.Collections.Concurrent;
using System.IO;

namespace XTerminal.App.Services;

public enum LogCategory
{
    App, Terminal, Ssh, Sftp, Files, Editor, Ports, History, Snippets
}

/// <summary>Local rotating file logger. Never writes secrets.</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly ConcurrentQueue<string> Recent = new();
    private static string? _path;
    private static int _maxLogFiles = 14;
    private static bool _enabled = true;

    public static void Configure(bool enabled, int maxLogFiles)
    {
        _enabled = enabled;
        _maxLogFiles = Math.Clamp(maxLogFiles, 1, 90);
    }

    public static void Info(LogCategory category, string message) => Write("INFO", category, message);
    public static void Warn(LogCategory category, string message) => Write("WARN", category, message);
    public static void Error(LogCategory category, string message, Exception? ex = null) =>
        Write("ERROR", category, ex is null ? message : $"{message}: {Sanitize(ex.Message)}");

    private static void Write(string level, LogCategory category, string message)
    {
        if (!_enabled) return;
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] [{category}] {Sanitize(message)}";
        Recent.Enqueue(line);
        while (Recent.Count > 200 && Recent.TryDequeue(out _)) { }

        try
        {
            lock (Gate)
            {
                EnsurePath();
                File.AppendAllText(_path!, line + Environment.NewLine);
                TrimOldLogs();
            }
        }
        catch
        {
            // never throw from logging
        }
    }

    private static void EnsurePath()
    {
        if (_path is not null) return;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTerminal", "logs");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, $"xterminal-{DateTime.Now:yyyy-MM-dd}.log");
    }

    private static void TrimOldLogs()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (dir is null || !Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, "xterminal-*.log").OrderByDescending(f => f).Skip(_maxLogFiles).ToList();
            foreach (var f in files) try { File.Delete(f); } catch { /* ignore */ }
        }
        catch { /* ignore */ }
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        return message
            .Replace("password", "********", StringComparison.OrdinalIgnoreCase)
            .Replace("passphrase", "********", StringComparison.OrdinalIgnoreCase)
            .Replace("private key", "********", StringComparison.OrdinalIgnoreCase);
    }
}
