using System.Text;

namespace XTerminal.Core.Services;

public enum ShellIntegrationEventKind
{
    Prompt,
    CommandStart,
    CommandFinished,
    Directory,
    Unknown
}

public sealed class ShellIntegrationEvent
{
    public ShellIntegrationEventKind Kind { get; init; }
    public int? ExitCode { get; init; }
    public string? Command { get; init; }
    public string? WorkingDirectory { get; init; }
    public string Raw { get; init; } = "";
}

/// <summary>
/// Parses a conservative subset of FinalTerm / iTerm2 OSC 133 sequences.
/// Terminal output is treated as untrusted — events never execute local actions.
/// </summary>
public sealed class ShellIntegrationParser
{
    private readonly StringBuilder _carry = new();

    public bool Enabled { get; set; }

    public IReadOnlyList<ShellIntegrationEvent> Push(string chunk)
    {
        if (!Enabled || string.IsNullOrEmpty(chunk))
            return Array.Empty<ShellIntegrationEvent>();

        _carry.Append(chunk);
        var text = _carry.ToString();
        var events = new List<ShellIntegrationEvent>();
        var consumed = 0;

        while (true)
        {
            var start = text.IndexOf("\u001b]", consumed, StringComparison.Ordinal);
            if (start < 0) break;

            var endBel = text.IndexOf('\u0007', start);
            var endSt = text.IndexOf("\u001b\\", start, StringComparison.Ordinal);
            var end = -1;
            var endLen = 1;
            if (endBel >= 0 && (endSt < 0 || endBel < endSt))
            {
                end = endBel;
                endLen = 1;
            }
            else if (endSt >= 0)
            {
                end = endSt;
                endLen = 2;
            }

            if (end < 0)
            {
                // Incomplete sequence — keep from start.
                _carry.Clear();
                _carry.Append(text[start..]);
                return events;
            }

            var payload = text[(start + 2)..end];
            consumed = end + endLen;
            if (payload.StartsWith("133;", StringComparison.Ordinal))
            {
                var body = payload[4..];
                var kindChar = body.Length > 0 ? body[0] : '?';
                var rest = body.Length > 1 && body[1] == ';' ? body[2..] : "";
                switch (kindChar)
                {
                    case 'A':
                    case 'B':
                        events.Add(new ShellIntegrationEvent { Kind = ShellIntegrationEventKind.Prompt, Raw = payload });
                        break;
                    case 'C':
                        events.Add(new ShellIntegrationEvent
                        {
                            Kind = ShellIntegrationEventKind.CommandStart,
                            Command = rest,
                            Raw = payload
                        });
                        break;
                    case 'D':
                        int? code = null;
                        if (int.TryParse(rest.Split(';')[0], out var ec))
                            code = ec;
                        events.Add(new ShellIntegrationEvent
                        {
                            Kind = ShellIntegrationEventKind.CommandFinished,
                            ExitCode = code,
                            Raw = payload
                        });
                        break;
                    default:
                        events.Add(new ShellIntegrationEvent { Kind = ShellIntegrationEventKind.Unknown, Raw = payload });
                        break;
                }
            }
            else if (payload.StartsWith("7;", StringComparison.Ordinal) ||
                     payload.StartsWith("1337;CurrentDir=", StringComparison.OrdinalIgnoreCase))
            {
                var dir = payload.StartsWith("7;", StringComparison.Ordinal)
                    ? Uri.UnescapeDataString(payload[2..].Replace("file://", ""))
                    : payload["1337;CurrentDir=".Length..];
                events.Add(new ShellIntegrationEvent
                {
                    Kind = ShellIntegrationEventKind.Directory,
                    WorkingDirectory = dir,
                    Raw = payload
                });
            }
        }

        if (consumed > 0)
        {
            _carry.Clear();
            if (consumed < text.Length)
                _carry.Append(text[consumed..]);
        }

        // Bound carry buffer against malicious flood
        if (_carry.Length > 8192)
            _carry.Clear();

        return events;
    }

    public void Reset() => _carry.Clear();
}
