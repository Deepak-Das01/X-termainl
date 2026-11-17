using XTerminal.Core.Settings;

namespace XTerminal.Core.Services;

public enum BroadcastSelectionMode
{
    Off,
    AllPanes,
    SelectedPanes
}

/// <summary>Pure broadcast target selection and safety checks (no UI).</summary>
public static class BroadcastLogic
{
    public static IReadOnlyList<T> SelectTargets<T>(
        BroadcastSelectionMode mode,
        IEnumerable<T> panes,
        Func<T, bool> isConnected,
        Func<T, bool> isPrivateInput,
        Func<T, bool> isSelectedTarget)
    {
        if (mode == BroadcastSelectionMode.Off)
            return Array.Empty<T>();

        var list = new List<T>();
        foreach (var pane in panes)
        {
            if (!isConnected(pane)) continue;
            if (isPrivateInput(pane)) continue;
            if (mode == BroadcastSelectionMode.AllPanes || isSelectedTarget(pane))
                list.Add(pane);
        }

        return list;
    }

    public static bool IsMultiLine(string data) =>
        !string.IsNullOrEmpty(data) && (data.Contains('\n') || data.Contains('\r'));

    public static bool ShouldConfirmMultiLine(SecurityAppSettings security, string data, bool isBroadcasting) =>
        isBroadcasting &&
        security.ConfirmBroadcastMultiLinePaste &&
        IsMultiLine(data);

    public static bool ShouldWarnMultipleRemoteHosts(SecurityAppSettings security, int distinctRemoteHostCount) =>
        security.WarnBroadcastToMultipleHosts && distinctRemoteHostCount > 1;

    public static bool ShouldBlockForPrivateInput(SecurityAppSettings security, bool activePanePrivate) =>
        security.BlockBroadcastWhilePrivateInput && activePanePrivate;
}
