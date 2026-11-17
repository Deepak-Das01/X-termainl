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

public sealed class KeybindingService
{
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

    public void SetGesture(string commandId, string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            _overrides[commandId] = "";
        else
            _overrides[commandId] = Normalize(gesture);
    }

    public void Reset(string commandId) => _overrides.Remove(commandId);

    public void ResetAll() => _overrides.Clear();

    public IReadOnlyList<KeybindingConflict> DetectConflicts()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<KeybindingConflict>();
        foreach (var id in _defaults.Keys.Concat(_overrides.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
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
        foreach (var id in _defaults.Keys.Concat(_overrides.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
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

    public static string ToGesture(Key key, ModifierKeys modifiers)
    {
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
}
