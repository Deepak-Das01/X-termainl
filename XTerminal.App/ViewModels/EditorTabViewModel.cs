using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using XTerminal.App.Services;
using XTerminal.Core.Settings;

namespace XTerminal.App.ViewModels;

public partial class EditorTabViewModel : ObservableObject, IWorkspaceTab, IAsyncDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _autoSaveCts;
    private bool _suppressDirty;
    private string _savedSnapshot = string.Empty;

    [ObservableProperty] private string _title = "Untitled";
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private string _languageId = "plaintext";
    [ObservableProperty] private string _encoding = "UTF-8";
    [ObservableProperty] private int _cursorLine = 1;
    [ObservableProperty] private int _cursorColumn = 1;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string? _localPath;
    [ObservableProperty] private EditorDocumentKind _kind = EditorDocumentKind.Untitled;

    public Guid Id { get; } = Guid.NewGuid();
    public string Glyph => "✎";
    public string Accent => "#C586C0";
    public bool IsEditor => true;
    public bool IsTerminalGroup => false;
    public RemoteEditSession? Remote { get; private set; }
    public string DisplayLanguage => EditorLanguageCatalog.DisplayName(LanguageId);
    public string DisplayPath => Kind switch
    {
        EditorDocumentKind.Local => LocalPath ?? "Untitled",
        EditorDocumentKind.RemoteSftp => Remote?.RemotePath ?? "Remote",
        _ => "Untitled"
    };

    public event Action? MetaChanged;
    public event Action? ContentExternallyUpdated;
    public event Func<EditorTabViewModel, Task>? AutoSaveRequested;

    public static EditorTabViewModel CreateUntitled(string? seed = null)
    {
        var tab = new EditorTabViewModel();
        tab.LoadContent(seed ?? string.Empty, markClean: true);
        tab.RefreshTitle();
        return tab;
    }

    public static EditorTabViewModel CreateLocal(string path, string content)
    {
        var tab = new EditorTabViewModel
        {
            Kind = EditorDocumentKind.Local,
            LocalPath = path,
            LanguageId = EditorLanguageCatalog.FromPath(path)
        };
        tab.LoadContent(content, markClean: true);
        tab.RefreshTitle();
        return tab;
    }

    public static EditorTabViewModel CreateRemote(RemoteEditSession remote)
    {
        var tab = new EditorTabViewModel
        {
            Kind = EditorDocumentKind.RemoteSftp,
            Remote = remote,
            LocalPath = remote.LocalTempPath,
            LanguageId = EditorLanguageCatalog.FromPath(remote.RemotePath)
        };
        tab.LoadContent(remote.Content, markClean: true);
        tab.RefreshTitle();
        return tab;
    }

    public void LoadContent(string content, bool markClean)
    {
        _suppressDirty = true;
        Content = content ?? string.Empty;
        if (markClean)
        {
            _savedSnapshot = Content;
            IsDirty = false;
        }

        _suppressDirty = false;
        MetaChanged?.Invoke();
    }

    public void ApplyHostContent(string content)
    {
        _suppressDirty = true;
        Content = content ?? string.Empty;
        IsDirty = !string.Equals(Content, _savedSnapshot, StringComparison.Ordinal);
        _suppressDirty = false;
        RefreshTitle();
        MetaChanged?.Invoke();
        _ = ScheduleAutoSaveAsync();
    }

    public void MarkSaved(string? newLocalPath = null)
    {
        if (newLocalPath is not null)
        {
            LocalPath = newLocalPath;
            Kind = EditorDocumentKind.Local;
            LanguageId = EditorLanguageCatalog.FromPath(newLocalPath);
        }

        _savedSnapshot = Content;
        IsDirty = false;
        StatusMessage = "Saved";
        RefreshTitle();
        MetaChanged?.Invoke();
    }

    public void MarkRemoteSaved(RemoteEditSession session)
    {
        Remote = session;
        Kind = EditorDocumentKind.RemoteSftp;
        LocalPath = session.LocalTempPath;
        _savedSnapshot = Content;
        IsDirty = false;
        StatusMessage = "Saved to remote";
        RefreshTitle();
        MetaChanged?.Invoke();
    }

    public void SetCursor(int line, int column)
    {
        CursorLine = Math.Max(1, line);
        CursorColumn = Math.Max(1, column);
        MetaChanged?.Invoke();
    }

    public void SetLanguage(string languageId)
    {
        if (!EditorLanguageCatalog.Supported.Contains(languageId))
        {
            languageId = "plaintext";
        }

        LanguageId = languageId;
        RefreshTitle();
        MetaChanged?.Invoke();
        ContentExternallyUpdated?.Invoke();
    }

    partial void OnContentChanged(string value)
    {
        if (_suppressDirty)
        {
            return;
        }

        IsDirty = !string.Equals(value, _savedSnapshot, StringComparison.Ordinal);
        RefreshTitle();
        MetaChanged?.Invoke();
        _ = ScheduleAutoSaveAsync();
    }

    private void RefreshTitle()
    {
        var name = Kind switch
        {
            EditorDocumentKind.RemoteSftp when Remote is not null =>
                string.IsNullOrWhiteSpace(Remote.RemoteHost)
                    ? $"{Path.GetFileName(Remote.RemotePath)} @ {Remote.SessionName}"
                    : $"{Path.GetFileName(Remote.RemotePath)} @ {Remote.RemoteHost}",
            EditorDocumentKind.Local when !string.IsNullOrEmpty(LocalPath) => Path.GetFileName(LocalPath),
            _ => "Untitled"
        };

        Title = IsDirty ? $"• {name}" : name;
    }

    private async Task ScheduleAutoSaveAsync()
    {
        // Auto-save is honored by MainViewModel via AutoSaveRequested when settings enable it.
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;
        try
        {
            await Task.Delay(400, token);
            if (!token.IsCancellationRequested && IsDirty && AutoSaveRequested is not null)
            {
                await AutoSaveRequested(this);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
    }

    public ValueTask DisposeAsync()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
