using XTerminal.Core.Models.Ui;

namespace XTerminal.Core.Services;

public sealed class WorkspaceValidationResult
{
    public bool IsValid { get; init; }
    public WorkspaceLayoutDocument? Document { get; init; }
    public string? Error { get; init; }
    public bool UsedFallback { get; init; }
}

/// <summary>Validates and migrates workspace metadata before restore.</summary>
public static class WorkspaceLayoutValidator
{
    public static WorkspaceValidationResult ValidateAndNormalize(WorkspaceLayoutDocument? raw)
    {
        if (raw is null)
            return new WorkspaceValidationResult { IsValid = false, Error = "Document is null.", UsedFallback = true };

        try
        {
            var doc = Migrate(raw);
            if (doc.Tabs.Count > WorkspaceLayoutDocument.MaxTabs)
                doc.Tabs = doc.Tabs.Take(WorkspaceLayoutDocument.MaxTabs).ToList();

            var seenTabIds = new HashSet<Guid>();
            var seenPaneIds = new HashSet<Guid>();
            var cleaned = new List<WorkspaceTabLayout>();

            foreach (var tab in doc.Tabs)
            {
                if (tab.TabId == Guid.Empty) tab.TabId = Guid.NewGuid();
                if (!seenTabIds.Add(tab.TabId))
                    tab.TabId = Guid.NewGuid();

                tab.Title = (tab.Title ?? "").Trim();
                if (tab.Title.Length > 120) tab.Title = tab.Title[..120];
                tab.Kind = string.IsNullOrWhiteSpace(tab.Kind) ? "terminal" : tab.Kind.Trim().ToLowerInvariant();
                if (tab.Kind is not ("terminal" or "editor")) continue;

                if (tab.Kind == "editor")
                {
                    if (string.IsNullOrWhiteSpace(tab.EditorPath)) continue;
                    // Never keep remote editor content; path + session id only.
                    cleaned.Add(tab);
                    continue;
                }

                if (tab.SplitRoot is null)
                {
                    tab.SplitRoot = new SplitLayoutNode
                    {
                        Type = "leaf",
                        PaneTitle = tab.Title,
                        LocalShell = tab.LocalShell,
                        WorkingDirectory = tab.WorkingDirectory,
                        SshSessionId = tab.SshSessionId,
                        PaneSessionId = tab.SshSessionId
                    };
                }

                if (!NormalizeSplit(tab.SplitRoot, seenPaneIds, depth: 0, out var paneCount) ||
                    paneCount is < 1 or > WorkspaceLayoutDocument.MaxPanesPerTab)
                    continue;

                cleaned.Add(tab);
            }

            doc.Tabs = cleaned;
            if (doc.ActiveTabId is Guid aid && cleaned.All(t => t.TabId != aid))
                doc.ActiveTabId = cleaned.FirstOrDefault()?.TabId;

            if (doc.WorkspaceId == Guid.Empty) doc.WorkspaceId = Guid.NewGuid();
            if (string.IsNullOrWhiteSpace(doc.WorkspaceName))
                doc.WorkspaceName = "Previous session";

            return new WorkspaceValidationResult { IsValid = true, Document = doc };
        }
        catch (Exception ex)
        {
            return new WorkspaceValidationResult
            {
                IsValid = false,
                Error = ex.Message,
                UsedFallback = true
            };
        }
    }

    private static WorkspaceLayoutDocument Migrate(WorkspaceLayoutDocument doc)
    {
        if (doc.Version < 1) doc.Version = 1;
        if (doc.Version < 2)
        {
            doc.Version = 2;
            if (doc.WorkspaceId == Guid.Empty) doc.WorkspaceId = Guid.NewGuid();
            foreach (var tab in doc.Tabs)
            {
                if (tab.TabId == Guid.Empty) tab.TabId = Guid.NewGuid();
            }
        }

        if (doc.Version > WorkspaceLayoutDocument.CurrentSchemaVersion)
        {
            // Forward-compatible: keep known fields; clamp version for writers.
            doc.Version = WorkspaceLayoutDocument.CurrentSchemaVersion;
        }

        return doc;
    }

    private static bool NormalizeSplit(SplitLayoutNode node, HashSet<Guid> paneIds, int depth, out int paneCount)
    {
        paneCount = 0;
        if (depth > WorkspaceLayoutDocument.MaxSplitDepth) return false;

        if (string.Equals(node.Type, "branch", StringComparison.OrdinalIgnoreCase))
        {
            if (node.First is null || node.Second is null) return false;
            node.Ratio = Math.Clamp(node.Ratio <= 0 ? 0.5 : node.Ratio, 0.15, 0.85);
            if (string.IsNullOrWhiteSpace(node.Orientation))
                node.Orientation = "Vertical";
            if (!NormalizeSplit(node.First, paneIds, depth + 1, out var a)) return false;
            if (!NormalizeSplit(node.Second, paneIds, depth + 1, out var b)) return false;
            paneCount = a + b;
            return true;
        }

        node.Type = "leaf";
        if (node.PaneId == Guid.Empty) node.PaneId = Guid.NewGuid();
        if (!paneIds.Add(node.PaneId))
            node.PaneId = Guid.NewGuid();
        paneCount = 1;
        return true;
    }

    public static bool ContainsSecrets(string json) =>
        json.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        json.Contains("passphrase", StringComparison.OrdinalIgnoreCase) ||
        json.Contains("BEGIN OPENSSH PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
        json.Contains("BEGIN RSA PRIVATE KEY", StringComparison.OrdinalIgnoreCase);
}
