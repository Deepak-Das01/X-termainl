using System.Text;

namespace XTerminal.Core.Services;

public enum ShellIntegrationEventKind
{
    PromptStarted,
    PromptCompleted,
    CommandStarted,
    CommandFinished,
    Directory,
    Unknown
}

public enum ShellIntegrationState
{
    Disabled,
    Unavailable,
    Detecting,
    Active,
    Degraded
}

public sealed class ShellIntegrationEvent
{
    public ShellIntegrationEventKind Kind { get; init; }
    public int? ExitCode { get; init; }
    public string? Command { get; init; }
    public string? WorkingDirectory { get; init; }
    public TimeSpan? Duration { get; init; }
    public string Raw { get; init; } = "";
    public DateTimeOffset Utc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Bounded defensive parser for OSC 7 / 133 / 633 shell-integration sequences.
/// Terminal output is untrusted — events are advisory and never execute local actions,
/// mutate the clipboard, navigate the app, or start transfers.
/// </summary>
public sealed class ShellIntegrationParser
{
    public const int MaxSequenceLength = 2048;
    public const int MaxCarryLength = 4096;

    private readonly StringBuilder _carry = new();
    private DateTimeOffset? _commandStartedUtc;
    private bool _sawValidEvent;

    public bool Enabled { get; set; }
    public ShellIntegrationState State { get; private set; } = ShellIntegrationState.Disabled;

    public IReadOnlyList<ShellIntegrationEvent> Push(string chunk)
    {
        if (!Enabled)
        {
            State = ShellIntegrationState.Disabled;
            return Array.Empty<ShellIntegrationEvent>();
        }

        if (string.IsNullOrEmpty(chunk))
            return Array.Empty<ShellIntegrationEvent>();

        if (State is ShellIntegrationState.Disabled or ShellIntegrationState.Unavailable)
            State = ShellIntegrationState.Detecting;

        _carry.Append(chunk);
        if (_carry.Length > MaxCarryLength * 2)
        {
            // Flood protection — drop buffer and mark degraded.
            _carry.Clear();
            State = ShellIntegrationState.Degraded;
            return Array.Empty<ShellIntegrationEvent>();
        }

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
                var pending = text.Length - start;
                if (pending > MaxSequenceLength)
                {
                    // Oversized incomplete sequence — discard and continue after it.
                    consumed = start + MaxSequenceLength;
                    State = ShellIntegrationState.Degraded;
                    continue;
                }

                _carry.Clear();
                _carry.Append(text[start..]);
                return events;
            }

            if (end - start > MaxSequenceLength)
            {
                consumed = end + endLen;
                State = ShellIntegrationState.Degraded;
                continue;
            }

            var payload = text[(start + 2)..end];
            consumed = end + endLen;

            // Reject control characters / NULs inside payload.
            if (payload.IndexOf('\0') >= 0)
            {
                State = ShellIntegrationState.Degraded;
                continue;
            }

            var parsed = TryParse(payload);
            if (parsed is null) continue;

            // Never accept file:// or http URIs as actionable — directory is path metadata only.
            if (parsed.Kind == ShellIntegrationEventKind.Directory)
            {
                parsed = SanitizeDirectory(parsed);
                if (parsed is null) continue;
            }

            // Strip command text when empty/malformed; never treat as executable payload.
            if (parsed.Kind == ShellIntegrationEventKind.CommandStarted &&
                parsed.Command is { Length: > 4000 })
            {
                parsed = new ShellIntegrationEvent
                {
                    Kind = parsed.Kind,
                    Command = parsed.Command[..4000],
                    Raw = parsed.Raw,
                    Utc = parsed.Utc
                };
            }

            if (parsed.Kind == ShellIntegrationEventKind.CommandStarted)
                _commandStartedUtc = parsed.Utc;
            if (parsed.Kind == ShellIntegrationEventKind.CommandFinished && _commandStartedUtc is { } started)
            {
                var duration = parsed.Utc - started;
                parsed = new ShellIntegrationEvent
                {
                    Kind = parsed.Kind,
                    ExitCode = parsed.ExitCode,
                    Command = parsed.Command,
                    WorkingDirectory = parsed.WorkingDirectory,
                    Duration = duration < TimeSpan.Zero ? null : duration,
                    Raw = parsed.Raw,
                    Utc = parsed.Utc
                };
                _commandStartedUtc = null;
            }

            events.Add(parsed);
            _sawValidEvent = true;
            State = ShellIntegrationState.Active;
        }

        if (consumed > 0)
        {
            _carry.Clear();
            if (consumed < text.Length)
                _carry.Append(text[consumed..]);
        }

        if (_carry.Length > MaxCarryLength)
            _carry.Clear();

        if (!_sawValidEvent && State == ShellIntegrationState.Detecting)
        {
            // Remain Detecting until first valid event or timeout handled by host.
        }

        return events;
    }

    private static ShellIntegrationEvent? TryParse(string payload)
    {
        // OSC 133 — FinalTerm / VS Code style
        if (payload.StartsWith("133;", StringComparison.Ordinal))
        {
            var body = payload[4..];
            if (body.Length == 0) return null;
            var kindChar = body[0];
            var rest = body.Length > 1 && body[1] == ';' ? body[2..] : body.Length > 1 ? body[1..] : "";
            return kindChar switch
            {
                'A' => new ShellIntegrationEvent { Kind = ShellIntegrationEventKind.PromptStarted, Raw = payload },
                'B' => new ShellIntegrationEvent { Kind = ShellIntegrationEventKind.PromptCompleted, Raw = payload },
                'C' => new ShellIntegrationEvent
                {
                    Kind = ShellIntegrationEventKind.CommandStarted,
                    Command = string.IsNullOrWhiteSpace(rest) ? null : rest,
                    Raw = payload
                },
                'D' => new ShellIntegrationEvent
                {
                    Kind = ShellIntegrationEventKind.CommandFinished,
                    ExitCode = int.TryParse(rest.Split(';')[0], out var ec) ? ec : null,
                    Raw = payload
                },
                _ => new ShellIntegrationEvent { Kind = ShellIntegrationEventKind.Unknown, Raw = payload }
            };
        }

        // OSC 633 — VS Code Windows Terminal / PowerShell integration (subset)
        if (payload.StartsWith("633;", StringComparison.Ordinal))
        {
            var body = payload[4..];
            if (body.StartsWith("E;", StringComparison.Ordinal))
            {
                return new ShellIntegrationEvent
                {
                    Kind = ShellIntegrationEventKind.CommandStarted,
                    Command = body.Length > 2 ? body[2..] : null,
                    Raw = payload
                };
            }

            if (body.StartsWith("D;", StringComparison.Ordinal) || body == "D")
            {
                var rest = body.Length > 2 ? body[2..] : "";
                return new ShellIntegrationEvent
                {
                    Kind = ShellIntegrationEventKind.CommandFinished,
                    ExitCode = int.TryParse(rest.Split(';')[0], out var ec) ? ec : null,
                    Raw = payload
                };
            }

            if (body.StartsWith("P;Cwd=", StringComparison.OrdinalIgnoreCase))
            {
                return new ShellIntegrationEvent
                {
                    Kind = ShellIntegrationEventKind.Directory,
                    WorkingDirectory = body["P;Cwd=".Length..],
                    Raw = payload
                };
            }

            if (body is "A" or "B")
            {
                return new ShellIntegrationEvent
                {
                    Kind = body == "A"
                        ? ShellIntegrationEventKind.PromptStarted
                        : ShellIntegrationEventKind.PromptCompleted,
                    Raw = payload
                };
            }

            return new ShellIntegrationEvent { Kind = ShellIntegrationEventKind.Unknown, Raw = payload };
        }

        // OSC 7 — current directory
        if (payload.StartsWith("7;", StringComparison.Ordinal))
        {
            return new ShellIntegrationEvent
            {
                Kind = ShellIntegrationEventKind.Directory,
                WorkingDirectory = payload[2..],
                Raw = payload
            };
        }

        if (payload.StartsWith("1337;CurrentDir=", StringComparison.OrdinalIgnoreCase))
        {
            return new ShellIntegrationEvent
            {
                Kind = ShellIntegrationEventKind.Directory,
                WorkingDirectory = payload["1337;CurrentDir=".Length..],
                Raw = payload
            };
        }

        return null;
    }

    private static ShellIntegrationEvent? SanitizeDirectory(ShellIntegrationEvent ev)
    {
        var raw = ev.WorkingDirectory ?? "";
        // Strip file://hostname prefixes; reject http/https/javascript and UNC escapes.
        if (raw.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains('\0'))
            return null;

        try
        {
            if (raw.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(raw);
                raw = Uri.UnescapeDataString(uri.AbsolutePath);
            }
            else
            {
                raw = Uri.UnescapeDataString(raw);
            }
        }
        catch
        {
            return null;
        }

        raw = raw.Replace('\\', '/').Trim();
        if (raw.Length is 0 or > 1024) return null;
        // Advisory path only — no automatic filesystem access.
        return new ShellIntegrationEvent
        {
            Kind = ShellIntegrationEventKind.Directory,
            WorkingDirectory = raw,
            Raw = ev.Raw,
            Utc = ev.Utc
        };
    }

    public void MarkUnavailable() => State = ShellIntegrationState.Unavailable;

    public void Reset()
    {
        _carry.Clear();
        _commandStartedUtc = null;
        _sawValidEvent = false;
        State = Enabled ? ShellIntegrationState.Detecting : ShellIntegrationState.Disabled;
    }
}
