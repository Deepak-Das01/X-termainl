using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Security;
using XTerminal.Core.Services;
using XTerminal.Protocols.Ssh;

namespace XTerminal.App.ViewModels;

public partial class SessionsViewModel : ObservableObject
{
    private readonly ISshSessionLibrary _library;
    private readonly ISecretStore _secrets;
    private readonly IUiStateStore _uiState;
    private readonly SshHostKeyPolicy _hostKeys;
    private readonly HashSet<Guid> _collapsedFolders = new();
    private SshLibraryDocument _document = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private bool _recentOnly;
    [ObservableProperty] private SshSessionRecord? _selectedSession;
    [ObservableProperty] private SshFolderRecord? _selectedFolder;
    [ObservableProperty] private bool _hasSessions;

    public ObservableCollection<SessionTreeNode> Roots { get; } = new();
    public ObservableCollection<SshSessionRecord> FlatSessions { get; } = new();
    public event Action<SshSessionRecord>? ConnectRequested;
    public event Action<SshSessionRecord>? SftpRequested;
    public event Action<SshSessionEditorViewModel, bool>? EditRequested; // bool = isNew

    public SessionsViewModel(
        ISshSessionLibrary library,
        ISecretStore secrets,
        SshHostKeyPolicy hostKeys,
        IUiStateStore? uiState = null)
    {
        _library = library;
        _secrets = secrets;
        _hostKeys = hostKeys;
        _uiState = uiState ?? new JsonUiStateStore();
    }

    public async Task LoadAsync()
    {
        var ui = await _uiState.LoadAsync();
        _collapsedFolders.Clear();
        foreach (var id in ui.CollapsedSessionFolderIds)
            _collapsedFolders.Add(id);

        _document = await _library.LoadAsync();
        RebuildTree();
    }

    public IReadOnlyList<SshFolderOption> GetFolderOptions()
    {
        var list = new List<SshFolderOption> { new() { Id = null, Name = "(Uncategorized)" } };
        foreach (var f in _document.Folders.OrderBy(x => x.Name))
            list.Add(new SshFolderOption { Id = f.Id, Name = f.Name });
        return list;
    }

    public async Task EnsureFolderAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_document.Folders.Any(f => f.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))) return;
        _document.Folders.Add(new SshFolderRecord { Name = name.Trim() });
        await _library.SaveAsync(_document);
        RebuildTree();
    }

    public async Task EnsureFolderWithIdAsync(Guid id, string name)
    {
        if (_document.Folders.Any(f => f.Id == id)) return;
        var byName = _document.Folders.FirstOrDefault(f =>
            f.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return;
        _document.Folders.Add(new SshFolderRecord { Id = id, Name = name.Trim() });
        await _library.SaveAsync(_document);
        RebuildTree();
    }

    [RelayCommand]
    private void Connect(SshSessionRecord? session)
    {
        session ??= SelectedSession;
        if (session is not null) ConnectRequested?.Invoke(session);
    }

    [RelayCommand]
    private void OpenSftp(SshSessionRecord? session)
    {
        session ??= SelectedSession;
        if (session is not null) SftpRequested?.Invoke(session);
    }

    /// <summary>Last sanitized SSH diagnostic report text (for Copy diagnostics).</summary>
    public string? LastDiagnosticClipboard { get; private set; }

    [RelayCommand]
    private Task DiagnoseSessionAsync(SshSessionRecord? session)
    {
        session ??= SelectedSession;
        if (session is null) return Task.CompletedTask;

        var window = new Views.DiagnosticsWindow(session, _secrets, _hostKeys)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.ShowDialog();
        if (window.DataContext is Views.DiagnosticsViewModel vm &&
            !string.IsNullOrWhiteSpace(vm.ClipboardText))
            LastDiagnosticClipboard = vm.ClipboardText;
        return Task.CompletedTask;
    }

    public SshSessionRecord? FindSession(Guid id) =>
        _document.Sessions.FirstOrDefault(s => s.Id == id);

    [RelayCommand]
    private void NewSession()
    {
        var editor = new SshSessionEditorViewModel(_secrets, _hostKeys);
        EditRequested?.Invoke(editor, true);
    }

    [RelayCommand]
    private void EditSession(SshSessionRecord? session)
    {
        session ??= SelectedSession;
        if (session is null) return;
        var editor = new SshSessionEditorViewModel(_secrets, _hostKeys, session);
        EditRequested?.Invoke(editor, false);
    }

    [RelayCommand]
    private async Task DuplicateSessionAsync(SshSessionRecord? session)
    {
        session ??= SelectedSession;
        if (session is null) return;

        var copy = CloneSession(session);
        copy.Id = Guid.NewGuid();
        copy.Name = session.Name + " (copy)";
        copy.CreatedUtc = DateTimeOffset.UtcNow;
        copy.ModifiedUtc = DateTimeOffset.UtcNow;
        _document.Sessions.Add(copy);
        await _library.SaveAsync(_document);
        RebuildTree();
    }

    [RelayCommand]
    private async Task DeleteSessionAsync(SshSessionRecord? session)
    {
        session ??= SelectedSession;
        if (session is null) return;
        _document.Sessions.RemoveAll(s => s.Id == session.Id);
        await _secrets.DeleteSecretAsync(SshSecretKeys.Password(session.Id));
        await _secrets.DeleteSecretAsync(SshSecretKeys.Passphrase(session.Id));
        await _secrets.DeleteSecretAsync(SshSecretKeys.JumpPassword(session.Id));
        await _secrets.DeleteSecretAsync(SshSecretKeys.JumpPassphrase(session.Id));
        await _library.SaveAsync(_document);
        SelectedSession = null;
        RebuildTree();
    }

    [RelayCommand]
    private void CopyHost(SshSessionRecord? session)
    {
        session ??= SelectedSession;
        if (session is null) return;
        try
        {
            System.Windows.Clipboard.SetText($"{session.Host}:{session.Port}");
        }
        catch
        {
            // clipboard may fail if another process owns it
        }
    }

    [RelayCommand]
    private async Task RenameSessionAsync(SshSessionRecord? session)
    {
        session ??= SelectedSession;
        if (session is null) return;
        var window = new Views.InputPromptWindow("Rename session", session.Name)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (window.ShowDialog() != true || string.IsNullOrWhiteSpace(window.Value)) return;
        session.Name = window.Value.Trim();
        session.ModifiedUtc = DateTimeOffset.UtcNow;
        await _library.SaveAsync(_document);
        RebuildTree();
        SelectedSession = session;
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(SshSessionRecord? session)
    {
        session ??= SelectedSession;
        if (session is null) return;
        session.IsFavorite = !session.IsFavorite;
        session.ModifiedUtc = DateTimeOffset.UtcNow;
        await _library.SaveAsync(_document);
        RebuildTree();
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        _document.Folders.Add(new SshFolderRecord
        {
            Name = SelectedFolder is null ? "New folder" : "New subfolder",
            ParentId = SelectedFolder?.Id
        });
        await _library.SaveAsync(_document);
        RebuildTree();
    }

    [RelayCommand]
    private async Task MoveToFolderAsync(SshSessionRecord? session)
    {
        session ??= SelectedSession;
        if (session is null) return;

        var folderNames = _document.Folders.OrderBy(f => f.Name).Select(f => f.Name).ToList();
        var hint = SelectedFolder?.Name ?? (folderNames.Count > 0 ? folderNames[0] : string.Empty);
        var window = new Views.InputPromptWindow(
            "Move to folder",
            hint,
            folderNames.Count > 0
                ? "Folder name (leave blank for Uncategorized):\n" + string.Join(", ", folderNames)
                : "No folders yet — leave blank for Uncategorized.")
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (window.ShowDialog() != true) return;

        var name = window.Value?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            session.FolderId = null;
        }
        else
        {
            var folder = _document.Folders.FirstOrDefault(f =>
                f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (folder is null)
            {
                folder = new SshFolderRecord { Name = name };
                _document.Folders.Add(folder);
            }

            session.FolderId = folder.Id;
        }

        session.ModifiedUtc = DateTimeOffset.UtcNow;
        await _library.SaveAsync(_document);
        RebuildTree();
        SelectedSession = session;
    }

    public void RememberFolderExpanded(Guid folderId, bool expanded)
    {
        if (expanded) _collapsedFolders.Remove(folderId);
        else _collapsedFolders.Add(folderId);
        _ = PersistFolderExpandStateAsync();
    }

    private async Task PersistFolderExpandStateAsync()
    {
        try
        {
            var ui = await _uiState.LoadAsync();
            ui.CollapsedSessionFolderIds = _collapsedFolders.ToList();
            await _uiState.SaveAsync(ui);
        }
        catch { /* ignore */ }
    }

    [RelayCommand]
    private async Task ExportSessionsAsync()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export sessions",
            Filter = "X-Terminal sessions (*.json)|*.json",
            FileName = $"xterminal-sessions-{DateTime.Now:yyyyMMdd}.json"
        };
        if (dlg.ShowDialog() != true) return;

        var selected = SelectedSession is null
            ? _document.Sessions
            : _document.Sessions.Where(s => s.Id == SelectedSession.Id).ToList();
        if (selected.Count == 0) selected = _document.Sessions;

        var doc = SessionImportExport.CreateExport(selected, _document.Folders);
        var json = SessionImportExport.Serialize(doc);
        if (SessionImportExport.ContainsSecretField(json))
            throw new InvalidOperationException("Refusing to export content that appears to contain secrets.");

        await File.WriteAllTextAsync(dlg.FileName, json);
        System.Windows.MessageBox.Show(
            $"Exported {doc.Sessions.Count} session(s) and {doc.Folders.Count} folder(s).\nPasswords and private key material were excluded.",
            "Export complete",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task ImportOpenSshConfigAsync()
    {
        var defaultPath = OpenSshConfigParser.DefaultConfigPath;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import OpenSSH config",
            Filter = "OpenSSH config|config|All files (*.*)|*.*",
            FileName = File.Exists(defaultPath) ? "config" : string.Empty,
            InitialDirectory = File.Exists(defaultPath)
                ? Path.GetDirectoryName(defaultPath)
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dlg.ShowDialog() != true) return;

        OpenSshImportPreview preview;
        try
        {
            preview = OpenSshConfigParser.ParseFile(dlg.FileName);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "OpenSSH import failed",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        var importable = preview.Entries.Where(OpenSshConfigParser.CanImport).ToList();
        var unsupported = preview.GlobalUnsupported
            .Concat(preview.Entries.SelectMany(e => e.UnsupportedDirectives))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unsupportedSummary = unsupported.Count == 0
            ? "No unsupported directives reported."
            : "Unsupported / blocked directives:\n• " + string.Join("\n• ", unsupported.Take(12))
              + (unsupported.Count > 12 ? $"\n• …and {unsupported.Count - 12} more" : string.Empty);

        var confirm = System.Windows.MessageBox.Show(
            $"OpenSSH config: {preview.SourcePath}\n\n" +
            $"Importable hosts: {importable.Count} of {preview.Entries.Count}\n\n" +
            $"{unsupportedSummary}\n\n" +
            "Yes = import importable entries (rename on name conflicts)\n" +
            "Secrets are never imported.\n\nCancel = abort",
            "Import OpenSSH config",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        var existingNames = new HashSet<string>(
            _document.Sessions.Select(s => s.Name),
            StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var entry in importable)
        {
            var record = OpenSshConfigParser.ToSession(entry);
            // Never import secrets — ToSession only carries key paths / connection metadata.
            record.Id = Guid.NewGuid();
            if (existingNames.Contains(record.Name))
            {
                record.Name = SessionImportExportUniqueName(record.Name, existingNames);
            }

            existingNames.Add(record.Name);
            record.Tags ??= new List<string>();
            if (!record.Tags.Contains("openssh-import", StringComparer.OrdinalIgnoreCase))
                record.Tags.Add("openssh-import");
            _document.Sessions.Add(record);
            added++;
        }

        await _library.SaveAsync(_document);
        RebuildTree();

        System.Windows.MessageBox.Show(
            $"Imported {added} OpenSSH host(s). Passwords and key material were not imported.",
            "OpenSSH import complete",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private static string SessionImportExportUniqueName(string baseName, HashSet<string> existing)
    {
        if (!existing.Contains(baseName)) return baseName;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!existing.Contains(candidate)) return candidate;
        }

        return $"{baseName}-{Guid.NewGuid():N}";
    }

    [RelayCommand]
    private async Task ImportSessionsAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import sessions",
            Filter = "X-Terminal sessions (*.json)|*.json|JSON files (*.json)|*.json"
        };
        if (dlg.ShowDialog() != true) return;

        SessionExportDocument doc;
        try
        {
            var json = await File.ReadAllTextAsync(dlg.FileName);
            doc = SessionImportExport.Deserialize(json);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Import failed",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        var preview = System.Windows.MessageBox.Show(
            $"Import {doc.Sessions.Count} session(s) and {doc.Folders.Count} folder(s)?\n\n" +
            "Yes = Rename duplicates\nNo = open conflict policy dialog\nCancel = Abort\n\n" +
            "Secrets are never imported.",
            "Confirm import",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question);

        if (preview == System.Windows.MessageBoxResult.Cancel) return;

        ImportConflictPolicy policy;
        if (preview == System.Windows.MessageBoxResult.Yes)
        {
            policy = ImportConflictPolicy.Rename;
        }
        else
        {
            var choice = System.Windows.MessageBox.Show(
                "Conflict policy for matching sessions:\n\n" +
                "Yes = Replace existing\nNo = Skip duplicates\nCancel = Abort",
                "Import conflict policy",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Question);
            if (choice == System.Windows.MessageBoxResult.Cancel) return;
            policy = choice == System.Windows.MessageBoxResult.Yes
                ? ImportConflictPolicy.Replace
                : ImportConflictPolicy.Skip;
        }

        var (sessions, folders) = SessionImportExport.Merge(doc, _document.Sessions, _document.Folders, policy);
        _document.Sessions = sessions;
        _document.Folders = folders;
        await _library.SaveAsync(_document);
        RebuildTree();
    }

    [RelayCommand]
    private async Task ExportMetadataAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var doc = SessionImportExport.CreateExport(_document.Sessions, _document.Folders);
        await File.WriteAllTextAsync(path, SessionImportExport.Serialize(doc));
    }

    [RelayCommand]
    private async Task ImportMetadataAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var doc = SessionImportExport.Deserialize(await File.ReadAllTextAsync(path));
        var (sessions, folders) = SessionImportExport.Merge(
            doc, _document.Sessions, _document.Folders, ImportConflictPolicy.Rename);
        _document.Sessions = sessions;
        _document.Folders = folders;
        await _library.SaveAsync(_document);
        RebuildTree();
    }

    public async Task SaveEditorAsync(SshSessionEditorViewModel editor, bool isNew)
    {
        var record = editor.ToRecord();
        if (isNew)
        {
            record.CreatedUtc = DateTimeOffset.UtcNow;
            _document.Sessions.Add(record);
        }
        else
        {
            var idx = _document.Sessions.FindIndex(s => s.Id == record.Id);
            if (idx >= 0)
            {
                record.CreatedUtc = _document.Sessions[idx].CreatedUtc;
                record.LastConnectedUtc = _document.Sessions[idx].LastConnectedUtc;
                _document.Sessions[idx] = record;
            }
            else
            {
                _document.Sessions.Add(record);
            }
        }

        await editor.PersistSecretsAsync();
        await _library.SaveAsync(_document);
        RebuildTree();
    }

    partial void OnSearchTextChanged(string value) => RebuildTree();
    partial void OnFavoritesOnlyChanged(bool value)
    {
        if (value) RecentOnly = false;
        RebuildTree();
    }

    partial void OnRecentOnlyChanged(bool value)
    {
        if (value) FavoritesOnly = false;
        RebuildTree();
    }

    partial void OnSelectedFolderChanged(SshFolderRecord? value) => RebuildFlatList();

    private void RebuildTree()
    {
        Roots.Clear();
        HasSessions = _document.Sessions.Count > 0;
        var query = SearchText?.Trim() ?? string.Empty;

        var recent = _document.Sessions
            .Where(s => s.LastConnectedUtc is not null)
            .OrderByDescending(s => s.LastConnectedUtc)
            .Take(8)
            .Where(s => Match(s, query))
            .ToList();

        if (recent.Count > 0)
        {
            var recentNode = new SessionTreeNode { Title = "Recent", IsFolder = true };
            foreach (var s in recent) recentNode.Children.Add(SessionNode(s));
            Roots.Add(recentNode);
        }

        var favorites = _document.Sessions.Where(s => s.IsFavorite && Match(s, query)).OrderBy(s => s.Name).ToList();
        if (favorites.Count > 0)
        {
            var favNode = new SessionTreeNode { Title = "Favorites", IsFolder = true };
            foreach (var s in favorites) favNode.Children.Add(SessionNode(s));
            Roots.Add(favNode);
        }

        foreach (var folder in _document.Folders.Where(f => f.ParentId is null).OrderBy(f => f.Name))
        {
            Roots.Add(BuildFolderNode(folder, query));
        }

        var orphan = _document.Sessions
            .Where(s => s.FolderId is null || _document.Folders.All(f => f.Id != s.FolderId))
            .Where(s => Match(s, query))
            .Where(MatchesFilterExtras)
            .OrderBy(s => s.Name);

        var uncategorized = new SessionTreeNode { Title = "Uncategorized", IsFolder = true };
        foreach (var s in orphan) uncategorized.Children.Add(SessionNode(s));
        if (uncategorized.Children.Count > 0) Roots.Add(uncategorized);

        RebuildFlatList();
    }

    private void RebuildFlatList()
    {
        FlatSessions.Clear();
        IEnumerable<SshSessionRecord> source = _document.Sessions;

        if (SelectedFolder is not null)
        {
            source = source.Where(s => s.FolderId == SelectedFolder.Id);
        }

        var query = SearchText?.Trim() ?? string.Empty;
        foreach (var session in source
                     .Where(s => Match(s, query))
                     .Where(MatchesFilterExtras)
                     .OrderBy(s => s.Name))
        {
            FlatSessions.Add(session);
        }
    }

    private bool MatchesFilterExtras(SshSessionRecord s)
    {
        if (FavoritesOnly && !s.IsFavorite) return false;
        if (RecentOnly && s.LastConnectedUtc is null) return false;
        return true;
    }

    private SessionTreeNode BuildFolderNode(SshFolderRecord folder, string query)
    {
        var node = new SessionTreeNode
        {
            Title = folder.Name,
            IsFolder = true,
            Folder = folder,
            IsExpanded = !_collapsedFolders.Contains(folder.Id)
        };
        node.ExpandedChanged = RememberFolderExpanded;
        foreach (var child in _document.Folders.Where(f => f.ParentId == folder.Id).OrderBy(f => f.Name))
        {
            node.Children.Add(BuildFolderNode(child, query));
        }

        foreach (var session in _document.Sessions
                     .Where(s => s.FolderId == folder.Id)
                     .Where(s => Match(s, query))
                     .Where(MatchesFilterExtras)
                     .OrderBy(s => s.Name))
        {
            node.Children.Add(SessionNode(session));
        }

        return node;
    }

    private static SessionTreeNode SessionNode(SshSessionRecord session) => new()
    {
        Title = session.Name,
        Subtitle = $"{session.Username}@{session.Host}:{session.Port}",
        IsFolder = false,
        Session = session
    };

    private static bool Match(SshSessionRecord session, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return session.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || session.Host.Contains(query, StringComparison.OrdinalIgnoreCase)
               || session.Username.Contains(query, StringComparison.OrdinalIgnoreCase)
               || session.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static SshSessionRecord CloneSession(SshSessionRecord s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Host = s.Host,
        Port = s.Port,
        Username = s.Username,
        AuthMethod = s.AuthMethod,
        PrivateKeyPath = s.PrivateKeyPath,
        StartupDirectory = s.StartupDirectory,
        StartupCommand = s.StartupCommand,
        EnvironmentVariables = new Dictionary<string, string>(s.EnvironmentVariables),
        ConnectionTimeoutSeconds = s.ConnectionTimeoutSeconds,
        KeepAliveIntervalSeconds = s.KeepAliveIntervalSeconds,
        Compression = s.Compression,
        TerminalType = s.TerminalType,
        ProxyJumpHost = s.ProxyJumpHost,
        ProxyJumpPort = s.ProxyJumpPort,
        ProxyJumpUsername = s.ProxyJumpUsername,
        ProxyJumpAuthMethod = s.ProxyJumpAuthMethod,
        ProxyJumpPrivateKeyPath = s.ProxyJumpPrivateKeyPath,
        StrictHostKeyChecking = s.StrictHostKeyChecking,
        AutoReconnect = s.AutoReconnect,
        MaxReconnectAttempts = s.MaxReconnectAttempts,
        PortForwards = s.PortForwards.Select(f => new SshPortForwardRecord
        {
            Id = f.Id,
            Enabled = f.Enabled,
            Type = f.Type,
            BindAddress = f.BindAddress,
            ListenPort = f.ListenPort,
            DestinationHost = f.DestinationHost,
            DestinationPort = f.DestinationPort,
            DisplayName = f.DisplayName
        }).ToList(),
        FolderId = s.FolderId,
        IsFavorite = s.IsFavorite,
        Tags = s.Tags.ToList(),
        LastConnectedUtc = s.LastConnectedUtc,
        CreatedUtc = s.CreatedUtc,
        ModifiedUtc = s.ModifiedUtc
    };
}

public partial class SessionTreeNode : ObservableObject
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public bool IsFolder { get; set; }
    public SshFolderRecord? Folder { get; set; }
    public SshSessionRecord? Session { get; set; }
    public ObservableCollection<SessionTreeNode> Children { get; } = new();
    public Action<Guid, bool>? ExpandedChanged { get; set; }

    [ObservableProperty] private bool _isExpanded = true;

    partial void OnIsExpandedChanged(bool value)
    {
        if (Folder is not null) ExpandedChanged?.Invoke(Folder.Id, value);
    }
}

public sealed class SshFolderOption
{
    public Guid? Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
