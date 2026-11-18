namespace XTerminal.Tests;

/// <summary>
/// Detects a unique test marker in interactive shell output without accepting a quoted
/// echo of the typed command (e.g. printf '%s\n' 'MARKER').
/// </summary>
internal static class ShellOutputMarker
{
    public static bool ContainsUnquotedLine(string text, string marker)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(marker))
            return false;

        var start = 0;
        while (start <= text.Length - marker.Length)
        {
            var idx = text.IndexOf(marker, start, StringComparison.Ordinal);
            if (idx < 0)
                return false;

            var before = idx == 0 ? '\n' : text[idx - 1];
            var afterIdx = idx + marker.Length;
            var after = afterIdx >= text.Length ? '\n' : text[afterIdx];

            // Whole-line (or buffer edge) occurrence, not the quoted argument in an echoed command.
            var lineBounded = (before is '\n' or '\r') && (after is '\n' or '\r');
            var notQuoted = before != '\'' && after != '\'';
            if (lineBounded && notQuoted)
                return true;

            start = idx + 1;
        }

        return false;
    }
}
