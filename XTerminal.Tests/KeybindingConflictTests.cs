using XTerminal.App.Services;
using XTerminal.Core.Settings;

namespace XTerminal.Tests;

public class KeybindingConflictTests
{
    [Fact]
    public void DetectConflicts_finds_duplicate_gestures()
    {
        var svc = new KeybindingService(new (string, string)[]
        {
            ("a", "Ctrl+X"),
            ("b", "Ctrl+Y")
        });
        svc.SetGesture("b", "Ctrl+X");
        var conflicts = svc.DetectConflicts();
        Assert.Contains(conflicts, c =>
            c.Gesture.Equals("Ctrl+X", StringComparison.OrdinalIgnoreCase) &&
            (c.CommandIdA is "a" or "b") &&
            (c.CommandIdB is "a" or "b"));
    }

    [Fact]
    public void DetectConflicts_ignores_empty_gestures()
    {
        var svc = new KeybindingService(new (string, string)[]
        {
            ("a", "Ctrl+X"),
            ("b", "Ctrl+Y")
        });
        svc.SetGesture("b", "");
        Assert.Empty(svc.DetectConflicts());
    }
}
