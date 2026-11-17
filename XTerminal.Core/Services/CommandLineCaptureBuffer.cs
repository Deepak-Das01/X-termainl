using System.Text;

namespace XTerminal.Core.Services;

/// <summary>
/// Conservative line buffer used by command history. When <see cref="Suppress"/> is true
/// (Private Input Mode), buffered content is discarded and nothing is emitted.
/// </summary>
public sealed class CommandLineCaptureBuffer
{
    private readonly StringBuilder _line = new();

    public bool Suppress { get; set; }

    public void Clear() => _line.Clear();

    public IReadOnlyList<string> Process(string data)
    {
        if (string.IsNullOrEmpty(data)) return Array.Empty<string>();
        if (Suppress)
        {
            _line.Clear();
            return Array.Empty<string>();
        }

        var flushed = new List<string>();
        foreach (var ch in data)
        {
            if (ch is '\r' or '\n')
            {
                var command = _line.ToString().Trim();
                _line.Clear();
                if (!string.IsNullOrWhiteSpace(command) && !LooksLikeSecretPromptEcho(command))
                    flushed.Add(command);
                continue;
            }

            if (ch is '\b' or '\u007f')
            {
                if (_line.Length > 0) _line.Length--;
                continue;
            }

            if (ch < 32) continue;
            _line.Append(ch);
            if (_line.Length > 4000)
                _line.Clear();
        }

        return flushed;
    }

    private static bool LooksLikeSecretPromptEcho(string command)
    {
        var lower = command.ToLowerInvariant();
        return lower.Contains("password:") || lower.Contains("passphrase:");
    }
}
