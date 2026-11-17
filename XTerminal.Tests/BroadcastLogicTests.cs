using XTerminal.Core.Services;
using XTerminal.Core.Settings;

namespace XTerminal.Tests;

public class BroadcastLogicTests
{
    private sealed record Pane(bool Connected, bool Private, bool Selected, string Host);

    [Fact]
    public void SelectTargets_all_skips_private_and_disconnected()
    {
        var panes = new[]
        {
            new Pane(true, false, false, "a"),
            new Pane(true, true, false, "b"),
            new Pane(false, false, false, "c"),
            new Pane(true, false, true, "d")
        };

        var targets = BroadcastLogic.SelectTargets(
            BroadcastSelectionMode.AllPanes,
            panes,
            p => p.Connected,
            p => p.Private,
            p => p.Selected);

        Assert.Equal(2, targets.Count);
        Assert.DoesNotContain(targets, t => t.Private);
        Assert.DoesNotContain(targets, t => !t.Connected);
    }

    [Fact]
    public void SelectTargets_selected_mode_respects_flag()
    {
        var panes = new[]
        {
            new Pane(true, false, false, "a"),
            new Pane(true, false, true, "b")
        };

        var targets = BroadcastLogic.SelectTargets(
            BroadcastSelectionMode.SelectedPanes,
            panes,
            p => p.Connected,
            p => p.Private,
            p => p.Selected);

        Assert.Single(targets);
        Assert.Equal("b", targets[0].Host);
    }

    [Fact]
    public void Safety_flags_detect_multiline_and_multihost()
    {
        var sec = new SecurityAppSettings
        {
            ConfirmBroadcastMultiLinePaste = true,
            WarnBroadcastToMultipleHosts = true,
            BlockBroadcastWhilePrivateInput = true
        };

        Assert.True(BroadcastLogic.ShouldConfirmMultiLine(sec, "echo 1\necho 2", true));
        Assert.False(BroadcastLogic.ShouldConfirmMultiLine(sec, "echo 1", true));
        Assert.True(BroadcastLogic.ShouldWarnMultipleRemoteHosts(sec, 2));
        Assert.True(BroadcastLogic.ShouldBlockForPrivateInput(sec, true));
    }
}
