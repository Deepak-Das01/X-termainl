using XTerminal.Core.Models;
using XTerminal.Core.Settings;
using XTerminal.Protocols;

namespace XTerminal.Tests;

/// <summary>
/// Pure routing helpers extracted as documentation of intended Ctrl+F / protocol gating behavior.
/// Full WPF PreviewKeyDown requires UI tests; these guard the decision tables.
/// </summary>
public class ShortcutRoutingTests
{
    public static bool ShouldWindowHandleCtrlF(ActivitySection section, bool editorTab, bool terminalTab) =>
        section switch
        {
            ActivitySection.Files => false,
            ActivitySection.History => false,
            _ when editorTab => true,
            _ when terminalTab && section == ActivitySection.Terminal => true,
            _ => false
        };

    public static bool ShouldWindowHandleAltArrows(
        ActivitySection section, bool editorTab, int paneCount) =>
        section == ActivitySection.Terminal && !editorTab && paneCount > 1;

    [Theory]
    [InlineData(ActivitySection.Files, false, false, false)]
    [InlineData(ActivitySection.History, false, false, false)]
    [InlineData(ActivitySection.Terminal, true, false, true)]
    [InlineData(ActivitySection.Terminal, false, true, true)]
    public void CtrlF_routing(ActivitySection section, bool editor, bool terminal, bool expectHandle) =>
        Assert.Equal(expectHandle, ShouldWindowHandleCtrlF(section, editor, terminal));

    [Theory]
    [InlineData(ActivitySection.Terminal, false, 2, true)]
    [InlineData(ActivitySection.Terminal, false, 1, false)]
    [InlineData(ActivitySection.Files, false, 2, false)]
    [InlineData(ActivitySection.Terminal, true, 2, false)]
    public void AltArrow_routing(ActivitySection section, bool editor, int panes, bool expect) =>
        Assert.Equal(expect, ShouldWindowHandleAltArrows(section, editor, panes));

    [Fact]
    public void Unsupported_protocols_are_gated()
    {
        Assert.True(SessionHostFactory.IsTerminalSessionSupported(SessionKind.LocalShell));
        Assert.True(SessionHostFactory.IsTerminalSessionSupported(SessionKind.Ssh));
        Assert.False(SessionHostFactory.IsTerminalSessionSupported(SessionKind.Telnet));
        Assert.False(SessionHostFactory.IsTerminalSessionSupported(SessionKind.Rdp));
        Assert.False(SessionHostFactory.IsTerminalSessionSupported(SessionKind.Sftp));
    }
}
