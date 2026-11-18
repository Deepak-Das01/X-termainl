using System.Text.RegularExpressions;
using System.Windows.Input;
using XTerminal.Core.Settings;

namespace XTerminal.App.Services;

public sealed class KeybindingConflict
{
    public required string Gesture { get; init; }
    public required string CommandIdA { get; init; }
    public required string CommandIdB { get; init; }
}

public enum ShortcutContext
{
    Global,
    Terminal,
    Editor,
    Sftp,
    Files,
    History,
    Sessions,
    Settings,
    Other
}

/// <summary>Centralized gesture helpers and conflict detection for application commands.</summary>
public sealed class KeybindingService
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alt+F4",
        "Ctrl+Alt+Delete",
        "Ctrl+Shift+Esc",
        "Win+L",
        "Win+D",
        "Ctrl+Shift+P" // palette — reassignment blocked to keep recovery path
    };

    private readonly Dictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _defaults;

    public KeybindingService(IEnumerable<(string CommandId, string Gesture)> defaults)
    {
        _defaults = defaults.ToDictionary(x => x.CommandId, x => x.Gesture, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string> Defaults => _defaults;

    public void LoadOverrides(IEnumerable<KeybindingEntry> entries)
    {
        _overrides.Clear();
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.CommandId)) continue;
            _overrides[e.CommandId] = e.Gesture?.Trim() ?? "";
        }
    }

    public List<KeybindingEntry> ExportOverrides() =>
        _overrides.Select(kv => new KeybindingEntry { CommandId = kv.Key, Gesture = kv.Value }).ToList();

    public string GetGesture(string commandId)
    {
        if (_overrides.TryGetValue(commandId, out var g))
            return g;
        return _defaults.TryGetValue(commandId, out var d) ? d : "";
    }

    public string GetDefaultGesture(string commandId) =>
        _defaults.TryGetValue(commandId, out var d) ? d : "";

    public bool IsReserved(string gesture) =>
        !string.IsNullOrWhiteSpace(gesture) && Reserved.Contains(Normalize(gesture));

    public bool TrySetGesture(string commandId, string gesture, bool allowReplaceConflict, out string? error)
    {
        error = null;
        gesture = string.IsNullOrWhiteSpace(gesture) ? "" : Normalize(gesture);
        if (!string.IsNullOrEmpty(gesture) && IsReserved(gesture))
        {
            error = $"“{gesture}” is reserved by the operating system or application recovery path.";
            return false;
        }

        if (!string.IsNullOrEmpty(gesture))
        {
            foreach (var id in AllCommandIds())
            {
                if (string.Equals(id, commandId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(GetGesture(id), gesture, StringComparison.OrdinalIgnoreCase)) continue;
                if (!allowReplaceConflict)
                {
                    error = $"Conflicts with “{id}”.";
                    return false;
                }

                _overrides[id] = "";
            }
        }

        if (string.IsNullOrWhiteSpace(gesture))
            _overrides[commandId] = "";
        else
            _overrides[commandId] = gesture;
        return true;
    }

    /// <summary>Assigns a gesture without clearing conflicts — use <see cref="TrySetGesture"/> for conflict-aware UX.</summary>
    public void SetGesture(string commandId, string gesture)
    {
        gesture = string.IsNullOrWhiteSpace(gesture) ? "" : Normalize(gesture);
        _overrides[commandId] = gesture;
    }

    public void Reset(string commandId) => _overrides.Remove(commandId);

    public void ResetAll() => _overrides.Clear();

    public IReadOnlyList<KeybindingConflict> DetectConflicts()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<KeybindingConflict>();
        foreach (var id in AllCommandIds())
        {
            var g = GetGesture(id);
            if (string.IsNullOrWhiteSpace(g)) continue;
            if (map.TryGetValue(g, out var other) &&
                !string.Equals(other, id, StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(new KeybindingConflict
                {
                    Gesture = g,
                    CommandIdA = other,
                    CommandIdB = id
                });
            }
            else
            {
                map[g] = id;
            }
        }

        return conflicts;
    }

    public bool TryMatch(Key key, ModifierKeys modifiers, out string commandId)
    {
        var gesture = ToGesture(key, modifiers);
        foreach (var id in AllCommandIds())
        {
            if (string.Equals(GetGesture(id), gesture, StringComparison.OrdinalIgnoreCase))
            {
                commandId = id;
                return true;
            }
        }

        commandId = "";
        return false;
    }

    public static bool IsModifierOnly(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.System;

    public static string ToGesture(Key key, ModifierKeys modifiers)
    {
        // WPF often reports Alt+letter as System + Key.
        if (key == Key.System)
            return "";

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key switch
        {
            Key.OemPlus or Key.Add => "+",
            Key.OemMinus or Key.Subtract => "-",
            Key.OemComma => ",",
            Key.D0 or Key.NumPad0 => "0",
            Key.D1 or Key.NumPad1 => "1",
            Key.D2 or Key.NumPad2 => "2",
            Key.D3 or Key.NumPad3 => "3",
            Key.D4 or Key.NumPad4 => "4",
            Key.D5 or Key.NumPad5 => "5",
            Key.D6 or Key.NumPad6 => "6",
            Key.D7 or Key.NumPad7 => "7",
            Key.D8 or Key.NumPad8 => "8",
            Key.D9 or Key.NumPad9 => "9",
            _ => key.ToString()
        });
        return string.Join("+", parts);
    }

    public static string Normalize(string gesture)
    {
        var parts = Regex.Split(gesture.Trim(), @"\s*\+\s*")
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();
        return string.Join("+", parts);
    }

    private IEnumerable<string> AllCommandIds() =>
        _defaults.Keys.Concat(_overrides.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Resolves which input surface owns keyboard routing.</summary>
public static class ShortcutContextResolver
{
    public static ShortcutContext Resolve(
        bool settingsOpen,
        string? activitySection,
        bool editorFocused,
        bool terminalFocused,
        bool textInputFocused)
    {
        if (settingsOpen) return ShortcutContext.Settings;
        if (textInputFocused) return ShortcutContext.Other;
        if (editorFocused) return ShortcutContext.Editor;
        if (terminalFocused) return ShortcutContext.Terminal;
        return activitySection?.ToLowerInvariant() switch
        {
            "sftp" => ShortcutContext.Sftp,
            "files" => ShortcutContext.Files,
            "history" => ShortcutContext.History,
            "sessions" => ShortcutContext.Sessions,
            "settings" => ShortcutContext.Settings,
            _ => ShortcutContext.Global
        };
    }

    /// <summary>
    /// Global palette / tab commands remain available across contexts;
    /// terminal-only shortcuts should not steal Files/Editor typing.
    /// </summary>
    public static bool AllowsCommand(ShortcutContext context, string commandId)
    {
        if (commandId.StartsWith("app.", StringComparison.OrdinalIgnoreCase) ||
            commandId.StartsWith("tab.", StringComparison.OrdinalIgnoreCase) ||
            commandId.StartsWith("settings.", StringComparison.OrdinalIgnoreCase) ||
            commandId.StartsWith("workspace.", StringComparison.OrdinalIgnoreCase))
            return true;

        return context switch
        {
            ShortcutContext.Terminal => commandId.StartsWith("term.", StringComparison.OrdinalIgnoreCase) ||
                                        commandId.StartsWith("pane.", StringComparison.OrdinalIgnoreCase) ||
                                        commandId.StartsWith("ssh.", StringComparison.OrdinalIgnoreCase) ||
                                        commandId.StartsWith("shell.", StringComparison.OrdinalIgnoreCase),
            ShortcutContext.Editor => commandId.StartsWith("editor.", StringComparison.OrdinalIgnoreCase),
            ShortcutContext.Sftp => commandId.StartsWith("sftp.", StringComparison.OrdinalIgnoreCase) ||
                                    commandId.StartsWith("ssh.", StringComparison.OrdinalIgnoreCase),
            ShortcutContext.Files => commandId.StartsWith("files.", StringComparison.OrdinalIgnoreCase),
            ShortcutContext.History => commandId.StartsWith("history.", StringComparison.OrdinalIgnoreCase),
            ShortcutContext.Sessions => commandId.StartsWith("ssh.", StringComparison.OrdinalIgnoreCase),
            ShortcutContext.Settings => false,
            ShortcutContext.Other => false,
            _ => true
        };
    }
}
