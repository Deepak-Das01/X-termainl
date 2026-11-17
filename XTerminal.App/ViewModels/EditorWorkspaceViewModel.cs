using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XTerminal.App.ViewModels;

public partial class EditorWorkspaceViewModel : ObservableObject
{
    [ObservableProperty] private EditorDocumentViewModel? _selectedDocument;
    [ObservableProperty] private string _statusText = "No file open";

    public ObservableCollection<EditorDocumentViewModel> Documents { get; } = new();

    public event Func<string, string, Task>? SaveRemoteRequested;

    public void OpenDocument(string remotePath, string content, Guid sessionId, string sessionName)
    {
        var existing = Documents.FirstOrDefault(d =>
            d.SessionId == sessionId &&
            string.Equals(d.RemotePath, remotePath, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.LoadContent(content);
            SelectedDocument = existing;
            StatusText = remotePath;
            return;
        }

        var doc = new EditorDocumentViewModel
        {
            Title = System.IO.Path.GetFileName(remotePath),
            RemotePath = remotePath,
            SessionId = sessionId,
            SessionName = sessionName
        };
        doc.LoadContent(content);
        Documents.Add(doc);
        SelectedDocument = doc;
        StatusText = remotePath;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedDocument is null || SaveRemoteRequested is null) return;
        await SaveRemoteRequested(SelectedDocument.RemotePath, SelectedDocument.Content);
        SelectedDocument.IsDirty = false;
        StatusText = $"Saved {SelectedDocument.RemotePath}";
    }

    [RelayCommand]
    private void CloseDocument(EditorDocumentViewModel? doc)
    {
        doc ??= SelectedDocument;
        if (doc is null) return;
        var idx = Documents.IndexOf(doc);
        Documents.Remove(doc);
        SelectedDocument = Documents.Count == 0
            ? null
            : Documents[Math.Clamp(idx, 0, Documents.Count - 1)];
        StatusText = SelectedDocument?.RemotePath ?? "No file open";
    }
}

public partial class EditorDocumentViewModel : ObservableObject
{
    private bool _suppressDirty;

    [ObservableProperty] private string _title = "Untitled";
    [ObservableProperty] private string _remotePath = string.Empty;
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _sessionName = string.Empty;

    public Guid SessionId { get; init; }

    public void LoadContent(string content)
    {
        _suppressDirty = true;
        Content = content;
        IsDirty = false;
        _suppressDirty = false;
    }

    partial void OnContentChanged(string value)
    {
        if (!_suppressDirty)
        {
            IsDirty = true;
        }
    }
}
