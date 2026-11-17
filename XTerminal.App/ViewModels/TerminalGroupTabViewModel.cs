using System.Collections.ObjectModel;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTerminal.Core.Models;
using XTerminal.Core.Services;
using XTerminal.Core.Settings;
using XTerminal.Protocols;

namespace XTerminal.App.ViewModels;

public enum BroadcastMode
{
    Off,
    AllPanes,
    SelectedPanes
}

/// <summary>Workspace tab that hosts one or more terminal panes with optional splits.</summary>
public partial class TerminalGroupTabViewModel : ObservableObject, IWorkspaceTab, IAsyncDisposable
{
    private readonly SessionHostFactory _factory;

    [ObservableProperty] private string _title = "Terminal";
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private TerminalTabViewModel? _activePane;
    [ObservableProperty] private SplitNode _root = null!;
    [ObservableProperty] private bool _broadcastEnabled;
    [ObservableProperty] private int _broadcastCount;
    [ObservableProperty] private bool _isMaximized;
    [ObservableProperty] private BroadcastMode _broadcastMode = BroadcastMode.Off;
    [ObservableProperty] private SplitNode? _maximizedLeaf;
    private bool _multiHostWarned;

    public Guid Id { get; } = Guid.NewGuid();
    public string Glyph { get; private set; } = "❯";
    public string Accent { get; private set; } = "#5391FE";
    public bool IsDirty => false;
    public bool IsEditor => false;
    public bool IsTerminalGroup => true;

    public ObservableCollection<TerminalTabViewModel> AllPanes { get; } = new();

    public event Action? StatusChanged;
    public event Action? LayoutChanged;

    public TerminalGroupTabViewModel(SessionDefinition definition, SessionHostFactory factory)
    {
        _factory = factory;
        var pane = CreatePane(definition);
        Root = new LeafNode(pane);
        ActivePane = pane;
        SyncChrome();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        foreach (var pane in AllPanes.ToList())
            await pane.StartAsync(ct);
    }

    public async Task SplitAsync(Orientation orientation)
    {
        if (ActivePane is null) return;
        var leaf = FindLeaf(Root, ActivePane);
        if (leaf is null) return;

        var clone = CloneDefinition(ActivePane.Definition);
        clone.Name = ActivePane.Title;
        var newPane = CreatePane(clone);
        await newPane.StartAsync();

        var branch = new BranchNode
        {
            Orientation = orientation,
            First = leaf,
            Second = new LeafNode(newPane),
            Ratio = 0.5
        };

        Root = ReplaceNode(Root, leaf, branch);
        ActivePane = newPane;
        IsMaximized = false;
        MaximizedLeaf = null;
        RefreshBroadcast();
        LayoutChanged?.Invoke();
        SyncChrome();
    }

    public async Task DuplicateActivePaneAsync()
    {
        if (ActivePane is null) return;
        await SplitAsync(Orientation.Vertical);
    }

    public async Task CloseActivePaneAsync()
    {
        if (ActivePane is null) return;
        if (AllPanes.Count <= 1) return; // close tab instead at UI layer

        var leaf = FindLeaf(Root, ActivePane);
        if (leaf is null) return;

        var sibling = FindSibling(Root, leaf);
        AllPanes.Remove(ActivePane);
        await ActivePane.DisposeAsync();

        if (sibling is not null)
        {
            Root = ReplaceNode(Root, FindParentBranch(Root, leaf) ?? Root, sibling);
            ActivePane = FirstLeaf(Root)?.Session;
        }

        IsMaximized = false;
        MaximizedLeaf = null;
        RefreshBroadcast();
        LayoutChanged?.Invoke();
        SyncChrome();
        StatusChanged?.Invoke();
    }

    public void FocusNextPane(bool reverse)
    {
        if (AllPanes.Count == 0) return;
        var idx = ActivePane is null ? 0 : AllPanes.IndexOf(ActivePane);
        if (idx < 0) idx = 0;
        idx = reverse
            ? (idx - 1 + AllPanes.Count) % AllPanes.Count
            : (idx + 1) % AllPanes.Count;
        ActivePane = AllPanes[idx];
        SyncChrome();
        StatusChanged?.Invoke();
    }

    public void ToggleMaximize()
    {
        if (IsMaximized)
        {
            IsMaximized = false;
            MaximizedLeaf = null;
        }
        else if (ActivePane is not null)
        {
            MaximizedLeaf = FindLeaf(Root, ActivePane);
            IsMaximized = MaximizedLeaf is not null;
        }

        LayoutChanged?.Invoke();
    }

    public void SetBroadcast(bool enabled) =>
        SetBroadcastMode(enabled ? BroadcastMode.AllPanes : BroadcastMode.Off);

    public void SetBroadcastMode(BroadcastMode mode)
    {
        BroadcastMode = mode;
        BroadcastEnabled = mode != BroadcastMode.Off;
        _multiHostWarned = false;
        if (mode == BroadcastMode.Off)
        {
            foreach (var p in AllPanes)
                p.IsBroadcastTarget = false;
        }
        else if (mode == BroadcastMode.AllPanes)
        {
            foreach (var p in AllPanes)
                p.IsBroadcastTarget = p.ConnectionState == ConnectionState.Connected && !p.PrivateInputMode;
        }

        RefreshBroadcast();
        StatusChanged?.Invoke();
        LayoutChanged?.Invoke();
    }

    public void ToggleBroadcastTarget(TerminalTabViewModel pane)
    {
        if (BroadcastMode != BroadcastMode.SelectedPanes) return;
        pane.IsBroadcastTarget = !pane.IsBroadcastTarget;
        RefreshBroadcast();
        StatusChanged?.Invoke();
        LayoutChanged?.Invoke();
    }

    public IEnumerable<TerminalTabViewModel> GetBroadcastTargets()
    {
        var mode = BroadcastMode switch
        {
            BroadcastMode.AllPanes => BroadcastSelectionMode.AllPanes,
            BroadcastMode.SelectedPanes => BroadcastSelectionMode.SelectedPanes,
            _ => BroadcastSelectionMode.Off
        };
        return BroadcastLogic.SelectTargets(
            mode,
            AllPanes,
            p => p.ConnectionState == ConnectionState.Connected,
            p => p.PrivateInputMode,
            p => p.IsBroadcastTarget);
    }

    /// <summary>Optional security prefs used for broadcast confirmations.</summary>
    public SecurityAppSettings? SecuritySettings { get; set; }

    public void WriteToActiveOrBroadcast(string data)
    {
        if (BroadcastMode == BroadcastMode.Off)
        {
            ActivePane?.WriteInput(data);
            return;
        }

        // Never broadcast while private input is active (security flag reinforces this).
        var security = SecuritySettings ?? new SecurityAppSettings();
        if (BroadcastLogic.ShouldBlockForPrivateInput(security, ActivePane?.PrivateInputMode == true) ||
            ActivePane?.PrivateInputMode == true)
        {
            ActivePane?.WriteInput(data);
            return;
        }

        var targets = GetBroadcastTargets().ToList();
        if (targets.Count == 0)
        {
            ActivePane?.WriteInput(data);
            return;
        }

        if (BroadcastLogic.ShouldConfirmMultiLine(security, data, isBroadcasting: true))
        {
            var confirm = System.Windows.MessageBox.Show(
                $"Paste {data.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length} line(s) to {targets.Count} pane(s)?",
                "Confirm broadcast paste",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes)
                return;
        }

        var remoteHosts = targets
            .Where(t => t.Definition.Kind == SessionKind.Ssh)
            .Select(t => $"{t.Definition.Host}:{t.Definition.Port}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Warn once per broadcast session (not every keystroke).
        if (!_multiHostWarned &&
            BroadcastLogic.ShouldWarnMultipleRemoteHosts(security, remoteHosts.Count))
        {
            var warn = System.Windows.MessageBox.Show(
                $"Broadcast will send input to {remoteHosts.Count} different remote hosts:\n" +
                string.Join("\n", remoteHosts.Take(8)) +
                (remoteHosts.Count > 8 ? "\n…" : ""),
                "Broadcast to multiple hosts",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (warn != System.Windows.MessageBoxResult.Yes)
            {
                SetBroadcastMode(BroadcastMode.Off);
                ActivePane?.WriteInput(data);
                return;
            }

            _multiHostWarned = true;
        }

        foreach (var pane in targets)
            pane.WriteInput(data);
    }

    public int PaneIndex => ActivePane is null ? 0 : AllPanes.IndexOf(ActivePane) + 1;
    public int PaneCount => AllPanes.Count;

    private TerminalTabViewModel CreatePane(SessionDefinition definition)
    {
        var pane = new TerminalTabViewModel(definition, _factory);
        pane.StatusChanged += () => StatusChanged?.Invoke();
        AllPanes.Add(pane);
        return pane;
    }

    private void SyncChrome()
    {
        if (ActivePane is null) return;
        Title = ActivePane.Title;
        Glyph = ActivePane.Glyph;
        Accent = ActivePane.Accent;
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(Accent));
        OnPropertyChanged(nameof(PaneIndex));
        OnPropertyChanged(nameof(PaneCount));
    }

    private void RefreshBroadcast()
    {
        BroadcastCount = GetBroadcastTargets().Count();
        BroadcastEnabled = BroadcastMode != BroadcastMode.Off && BroadcastCount > 0;
    }

    partial void OnActivePaneChanged(TerminalTabViewModel? value) => SyncChrome();

    private static SessionDefinition CloneDefinition(SessionDefinition s) => new()
    {
        Name = s.Name,
        Kind = s.Kind,
        Host = s.Host,
        Port = s.Port,
        Username = s.Username,
        Group = s.Group,
        Shell = s.Shell,
        CustomCommand = s.CustomCommand,
        WorkingDirectory = s.WorkingDirectory,
        SshSessionId = s.SshSessionId
    };

    private static LeafNode? FindLeaf(SplitNode node, TerminalTabViewModel pane) => node switch
    {
        LeafNode l when ReferenceEquals(l.Session, pane) => l,
        BranchNode b => FindLeaf(b.First, pane) ?? FindLeaf(b.Second, pane),
        _ => null
    };

    private static LeafNode? FirstLeaf(SplitNode node) => node switch
    {
        LeafNode l => l,
        BranchNode b => FirstLeaf(b.First) ?? FirstLeaf(b.Second),
        _ => null
    };

    private static SplitNode? FindSibling(SplitNode root, SplitNode target)
    {
        if (root is BranchNode b)
        {
            if (ReferenceEquals(b.First, target)) return b.Second;
            if (ReferenceEquals(b.Second, target)) return b.First;
            return FindSibling(b.First, target) ?? FindSibling(b.Second, target);
        }

        return null;
    }

    private static BranchNode? FindParentBranch(SplitNode root, SplitNode child)
    {
        if (root is BranchNode b)
        {
            if (ReferenceEquals(b.First, child) || ReferenceEquals(b.Second, child)) return b;
            return FindParentBranch(b.First, child) ?? FindParentBranch(b.Second, child);
        }

        return null;
    }

    private static SplitNode ReplaceNode(SplitNode root, SplitNode oldNode, SplitNode newNode)
    {
        if (ReferenceEquals(root, oldNode)) return newNode;
        if (root is BranchNode b)
        {
            if (ReferenceEquals(b.First, oldNode)) { b.First = newNode; return b; }
            if (ReferenceEquals(b.Second, oldNode)) { b.Second = newNode; return b; }
            ReplaceNode(b.First, oldNode, newNode);
            ReplaceNode(b.Second, oldNode, newNode);
        }

        return root;
    }

    public async ValueTask DisposeAsync()
    {
        SetBroadcastMode(BroadcastMode.Off);
        foreach (var pane in AllPanes.ToList())
            await pane.DisposeAsync();
        AllPanes.Clear();
    }
}

public abstract class SplitNode : ObservableObject;

public partial class LeafNode : SplitNode
{
    public LeafNode(TerminalTabViewModel session) => Session = session;
    public TerminalTabViewModel Session { get; }
}

public partial class BranchNode : SplitNode
{
    public Orientation Orientation { get; set; } = Orientation.Vertical;
    public SplitNode First { get; set; } = null!;
    public SplitNode Second { get; set; } = null!;
    [ObservableProperty] private double _ratio = 0.5;
}
