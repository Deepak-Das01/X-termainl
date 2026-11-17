using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTerminal.App.Helpers;
using XTerminal.App.Services;
using XTerminal.Core.Models.Sftp;
using XTerminal.Core.Models.Ssh;
using XTerminal.Core.Settings;
using XTerminal.Protocols.Ssh;

namespace XTerminal.App.ViewModels;

public partial class SftpWorkspaceViewModel : ObservableObject
{
    private readonly SftpSessionService _service;
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private bool _navigating;
    private FilesAppSettings _filesSettings = new();
    private NotificationService? _notifications;

    [ObservableProperty] private string _pathText = "/";
    [ObservableProperty] private string _statusText = "Select an SSH session to open SFTP.";
    [ObservableProperty] private string _sessionLabel = "No SFTP session";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private SftpEntryViewModel? _selectedEntry;
    [ObservableProperty] private SftpTransferItemViewModel? _selectedTransfer;
    [ObservableProperty] private string _errorBanner = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _hasTransfers;

    public ObservableCollection<SftpEntryViewModel> Entries { get; } = new();
    public ObservableCollection<SftpBreadcrumbItem> Breadcrumbs { get; } = new();
    public ObservableCollection<SftpTreeNode> TreeRoots { get; } = new();
    public ObservableCollection<SftpTransferItemViewModel> Transfers { get; } = new();

    public event Action<string /*remotePath*/, Guid /*sessionId*/, string /*sessionName*/>? OpenInEditorRequested;
    public event Action? PickSessionRequested;

    partial void OnErrorBannerChanged(string value) => HasError = !string.IsNullOrWhiteSpace(value);

    public SftpWorkspaceViewModel(SftpSessionService service)
    {
        _service = service;
        _service.ConnectionChanged += OnConnectionChanged;
        _service.TransfersChanged += RefreshTransfers;
        _service.TransferUpdated += OnTransferUpdated;
    }

    public void ApplyFilesSettings(FilesAppSettings settings)
    {
        _filesSettings = settings ?? new FilesAppSettings();
        _ = RefreshAsync();
    }

    public void AttachNotifications(NotificationService notifications) =>
        _notifications = notifications;

    public Guid? ActiveSessionId => _service.SessionId;
    public string? RemoteHost => _service.Host;
    public string? RemoteUser => _service.Username;

    public async Task OpenSessionAsync(SshSessionRecord session)
    {
        try
        {
            IsBusy = true;
            ErrorBanner = string.Empty;
            StatusText = "Connecting…";
            await _service.ConnectAsync(session.Id);
            SessionLabel = $"{session.Name} — {session.Username}@{session.Host}";
            _back.Clear();
            _forward.Clear();
            await NavigateToAsync(_service.HomeDirectory, pushHistory: false);
            await RebuildTreeRootAsync();
        }
        catch (Exception ex)
        {
            ErrorBanner = Sanitize(ex.Message);
            StatusText = ErrorBanner;
        }
        finally
        {
            IsBusy = false;
            IsConnected = _service.IsConnected;
        }
    }

    [RelayCommand]
    private void RequestPickSession() => PickSessionRequested?.Invoke();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_service.IsConnected) return;
        await NavigateToAsync(_service.WorkingDirectory, pushHistory: false);
    }

    [RelayCommand]
    private async Task GoHomeAsync()
    {
        if (!_service.IsConnected) return;
        await NavigateToAsync(_service.HomeDirectory, pushHistory: true);
    }

    [RelayCommand]
    private async Task GoParentAsync()
    {
        if (!_service.IsConnected) return;
        var parent = SftpSessionService.GetParent(_service.WorkingDirectory);
        await NavigateToAsync(parent, pushHistory: true);
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (_back.Count == 0 || !_service.IsConnected) return;
        var current = _service.WorkingDirectory;
        var target = _back.Pop();
        _forward.Push(current);
        await NavigateToAsync(target, pushHistory: false);
    }

    [RelayCommand]
    private async Task GoForwardAsync()
    {
        if (_forward.Count == 0 || !_service.IsConnected) return;
        var current = _service.WorkingDirectory;
        var target = _forward.Pop();
        _back.Push(current);
        await NavigateToAsync(target, pushHistory: false);
    }

    [RelayCommand]
    private async Task NavigateTypedPathAsync()
    {
        if (!_service.IsConnected) return;
        await NavigateToAsync(PathText, pushHistory: true);
    }

    [RelayCommand]
    private async Task OpenEntryAsync(SftpEntryViewModel? entry)
    {
        entry ??= SelectedEntry;
        if (entry is null || !_service.IsConnected) return;
        if (entry.IsDirectory)
        {
            await NavigateToAsync(entry.FullPath, pushHistory: true);
        }
    }

    [RelayCommand]
    private void UploadFile()
    {
        if (!_service.IsConnected) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Upload files" };
        if (dlg.ShowDialog() != true) return;
        foreach (var file in dlg.FileNames)
        {
            _service.EnqueueUploadFile(file, _service.WorkingDirectory);
        }
    }

    [RelayCommand]
    private void UploadFolder()
    {
        if (!_service.IsConnected) return;
        var path = FolderPicker.PickFolder(Application.Current?.MainWindow, "Select folder to upload");
        if (string.IsNullOrWhiteSpace(path)) return;
        _service.EnqueueUploadFolder(path, _service.WorkingDirectory);
    }

    [RelayCommand]
    private void Download()
    {
        if (!_service.IsConnected || SelectedEntry is null) return;

        var preferred = _filesSettings.DefaultDownloadDirectory;
        if (SelectedEntry.IsDirectory)
        {
            var folder = FolderPicker.PickFolder(Application.Current?.MainWindow, "Download folder to");
            if (string.IsNullOrWhiteSpace(folder))
                folder = Directory.Exists(preferred) ? preferred : null;
            if (string.IsNullOrWhiteSpace(folder)) return;
            var dest = Path.Combine(folder, SelectedEntry.Name);
            _service.EnqueueDownload(SelectedEntry.FullPath, dest, 0, isDirectory: true);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = SelectedEntry.Name,
            Title = "Download file",
            InitialDirectory = Directory.Exists(preferred) ? preferred : null
        };
        if (dlg.ShowDialog() != true) return;
        _service.EnqueueDownload(SelectedEntry.FullPath, dlg.FileName, SelectedEntry.Size, isDirectory: false);
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        if (!_service.IsConnected) return;
        var name = Prompt("New folder name", "New folder");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            await _service.CreateFolderAsync(_service.WorkingDirectory, name.Trim());
            await RefreshAsync();
        }
        catch (Exception ex) { ErrorBanner = Sanitize(ex.Message); }
    }

    [RelayCommand]
    private async Task CreateFileAsync()
    {
        if (!_service.IsConnected) return;
        var name = Prompt("New file name", "newfile.txt");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            await _service.CreateFileAsync(_service.WorkingDirectory, name.Trim());
            await RefreshAsync();
        }
        catch (Exception ex) { ErrorBanner = Sanitize(ex.Message); }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (!_service.IsConnected || SelectedEntry is null) return;
        var name = Prompt("Rename to", SelectedEntry.Name);
        if (string.IsNullOrWhiteSpace(name) || name == SelectedEntry.Name) return;
        try
        {
            await _service.RenameAsync(SelectedEntry.FullPath, name.Trim());
            await RefreshAsync();
        }
        catch (Exception ex) { ErrorBanner = Sanitize(ex.Message); }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!_service.IsConnected || SelectedEntry is null) return;
        var kind = SelectedEntry.IsDirectory ? "folder" : "file";
        var result = MessageBox.Show(
            $"Delete remote {kind} '{SelectedEntry.Name}'?\n\n{SelectedEntry.FullPath}\n\nThis cannot be undone.",
            "Confirm delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _service.DeleteAsync(SelectedEntry.FullPath, SelectedEntry.IsDirectory);
            await RefreshAsync();
        }
        catch (Exception ex) { ErrorBanner = Sanitize(ex.Message); }
    }

    [RelayCommand]
    private void CopyRemotePath()
    {
        if (SelectedEntry is null) return;
        Clipboard.SetText(SelectedEntry.FullPath);
        StatusText = "Remote path copied.";
    }

    [RelayCommand]
    private void ShowProperties()
    {
        if (SelectedEntry is null) return;
        MessageBox.Show(
            $"Name: {SelectedEntry.Name}\n" +
            $"Path: {SelectedEntry.FullPath}\n" +
            $"Type: {(SelectedEntry.IsDirectory ? "Directory" : "File")}\n" +
            $"Size: {SelectedEntry.SizeDisplay}\n" +
            $"Modified: {SelectedEntry.ModifiedDisplay}\n" +
            $"Permissions: {SelectedEntry.Permissions}\n" +
            $"Owner: {SelectedEntry.Owner}",
            "Properties",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private void OpenInEditor()
    {
        if (!_service.IsConnected || SelectedEntry is null || SelectedEntry.IsDirectory) return;
        if (_service.SessionId is null) return;
        OpenInEditorRequested?.Invoke(
            SelectedEntry.FullPath,
            _service.SessionId.Value,
            _service.SessionName ?? "SFTP");
    }

    public SftpSessionService Service => _service;

    [RelayCommand]
    private void CancelTransfer(SftpTransferItemViewModel? item)
    {
        item ??= SelectedTransfer;
        if (item is null) return;
        _service.CancelTransfer(item.Id);
    }

    [RelayCommand]
    private void RetryTransfer(SftpTransferItemViewModel? item)
    {
        item ??= SelectedTransfer;
        if (item is null) return;
        _service.RetryTransfer(item.Id);
        RefreshTransfers();
    }

    [RelayCommand]
    private void ClearCompletedTransfers()
    {
        _service.ClearCompletedTransfers();
        RefreshTransfers();
    }

    public async Task SaveRemoteFileAsync(string remotePath, string content)
    {
        await _service.WriteTextFileAsync(remotePath, content);
        if (string.Equals(SftpSessionService.GetParent(remotePath), _service.WorkingDirectory, StringComparison.Ordinal))
        {
            await RefreshAsync();
        }
    }

    public async Task ExpandTreeNodeAsync(SftpTreeNode node)
    {
        if (!_service.IsConnected || node.IsLoaded) return;
        try
        {
            var children = await _service.ListTreeChildrenAsync(node.FullPath);
            node.Children.Clear();
            foreach (var child in children)
            {
                node.Children.Add(new SftpTreeNode
                {
                    Name = child.Name,
                    FullPath = child.FullPath
                });
            }

            node.IsLoaded = true;
        }
        catch (Exception ex)
        {
            ErrorBanner = Sanitize(ex.Message);
        }
    }

    private async Task NavigateToAsync(string path, bool pushHistory)
    {
        if (_navigating || !_service.IsConnected) return;
        _navigating = true;
        IsBusy = true;
        ErrorBanner = string.Empty;
        try
        {
            if (pushHistory &&
                !string.Equals(_service.WorkingDirectory, path, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(_service.WorkingDirectory))
            {
                _back.Push(_service.WorkingDirectory);
                _forward.Clear();
            }

            var list = await _service.ListAsync(path);
            Entries.Clear();
            foreach (var e in list)
            {
                if (!_filesSettings.ShowHiddenFiles && e.Name.StartsWith('.'))
                    continue;
                Entries.Add(SftpEntryViewModel.From(e));
            }

            PathText = _service.WorkingDirectory;
            RebuildBreadcrumbs(_service.WorkingDirectory);
            StatusText = $"{Entries.Count} items — {_service.WorkingDirectory}";
        }
        catch (Exception ex)
        {
            ErrorBanner = Sanitize(ex.Message);
            StatusText = ErrorBanner;
        }
        finally
        {
            IsBusy = false;
            _navigating = false;
        }
    }

    private async Task RebuildTreeRootAsync()
    {
        TreeRoots.Clear();
        var root = new SftpTreeNode { Name = "/", FullPath = "/" };
        TreeRoots.Add(root);
        if (!string.Equals(_service.HomeDirectory, "/", StringComparison.Ordinal))
        {
            TreeRoots.Add(new SftpTreeNode
            {
                Name = "Home",
                FullPath = _service.HomeDirectory
            });
        }

        await ExpandTreeNodeAsync(root);
    }

    private void RebuildBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new SftpBreadcrumbItem { Label = "/", Path = "/" });
        var normalized = SftpSessionService.NormalizePath(path);
        if (normalized == "/") return;

        var parts = normalized.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        foreach (var part in parts)
        {
            current += "/" + part;
            Breadcrumbs.Add(new SftpBreadcrumbItem { Label = part, Path = current });
        }
    }

    private void OnConnectionChanged()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            IsConnected = _service.IsConnected;
            StatusText = _service.StatusMessage ?? StatusText;
            if (_service.IsConnected && _service.SessionName is not null)
            {
                SessionLabel = $"{_service.SessionName} — {_service.Username}@{_service.Host}";
            }
        });
    }

    private void RefreshTransfers()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Transfers.Clear();
            foreach (var t in _service.GetTransfers())
            {
                Transfers.Add(SftpTransferItemViewModel.From(t));
            }

            HasTransfers = Transfers.Count > 0;
        });
    }

    private void OnTransferUpdated(SftpTransferSnapshot snap)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var existing = Transfers.FirstOrDefault(t => t.Id == snap.Id);
            if (existing is null)
            {
                Transfers.Insert(0, SftpTransferItemViewModel.From(snap));
            }
            else
            {
                existing.Apply(snap);
            }

            HasTransfers = Transfers.Count > 0;
            if (_filesSettings.NotifyOnTransferComplete &&
                snap.State is SftpTransferState.Complete or SftpTransferState.Failed)
            {
                _notifications?.Show(
                    snap.State == SftpTransferState.Complete ? "Transfer complete" : "Transfer failed",
                    string.IsNullOrWhiteSpace(snap.FileName) ? snap.Destination : snap.FileName);
            }
        });
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await _service.DisconnectAsync();
        Entries.Clear();
        TreeRoots.Clear();
        IsConnected = false;
        SessionLabel = "No SFTP session";
        StatusText = "Disconnected";
    }

    private static string? Prompt(string title, string defaultValue)
    {
        var window = new Views.InputPromptWindow(title, defaultValue)
        {
            Owner = Application.Current?.MainWindow
        };
        return window.ShowDialog() == true ? window.Value : null;
    }

    private static string Sanitize(string message) =>
        SshErrorMapper.Sanitize(message);
}

public partial class SftpEntryViewModel : ObservableObject
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public string Permissions { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;

    public string SizeDisplay => IsDirectory ? "—" : FormatSize(Size);
    public string ModifiedDisplay => ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string TypeGlyph => IsDirectory ? "📁" : "📄";

    public static SftpEntryViewModel From(SftpRemoteEntry e) => new()
    {
        Name = e.Name,
        FullPath = e.FullPath,
        IsDirectory = e.IsDirectory,
        Size = e.Size,
        ModifiedUtc = e.ModifiedUtc,
        Permissions = e.Permissions,
        Owner = string.IsNullOrWhiteSpace(e.Owner) ? "—" : e.Owner
    };

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }

        return u == 0 ? $"{bytes} {units[u]}" : $"{v:0.##} {units[u]}";
    }
}

public partial class SftpTransferItemViewModel : ObservableObject
{
    [ObservableProperty] private SftpTransferState _state;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _source = string.Empty;
    [ObservableProperty] private string _destination = string.Empty;
    [ObservableProperty] private long _transferredBytes;
    [ObservableProperty] private long _totalBytes;
    [ObservableProperty] private double _percentage;
    [ObservableProperty] private double _bytesPerSecond;
    [ObservableProperty] private string _etaDisplay = "—";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _speedDisplay = "—";
    [ObservableProperty] private string _sizeDisplay = "—";

    public Guid Id { get; private set; }
    public SftpTransferDirection Direction { get; private set; }
    public string DirectionLabel => Direction == SftpTransferDirection.Upload ? "Upload" : "Download";
    public bool CanCancel => State is SftpTransferState.Queued or SftpTransferState.Transferring;
    public bool CanRetry => State is SftpTransferState.Failed or SftpTransferState.Cancelled;

    public static SftpTransferItemViewModel From(SftpTransferSnapshot s)
    {
        var vm = new SftpTransferItemViewModel { Id = s.Id, Direction = s.Direction };
        vm.Apply(s);
        return vm;
    }

    public void Apply(SftpTransferSnapshot s)
    {
        Id = s.Id;
        Direction = s.Direction;
        State = s.State;
        FileName = s.FileName;
        Source = s.Source;
        Destination = s.Destination;
        TransferredBytes = s.TransferredBytes;
        TotalBytes = s.TotalBytes;
        Percentage = s.Percentage;
        BytesPerSecond = s.BytesPerSecond;
        ErrorMessage = s.ErrorMessage ?? string.Empty;
        SpeedDisplay = s.BytesPerSecond <= 0 ? "—" : $"{FormatSize((long)s.BytesPerSecond)}/s";
        SizeDisplay = $"{FormatSize(s.TransferredBytes)} / {FormatSize(s.TotalBytes)}";
        EtaDisplay = s.Eta is null ? "—" : FormatEta(s.Eta.Value);
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(DirectionLabel));
    }

    private static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalHours >= 1) return $"{(int)eta.TotalHours}h {eta.Minutes}m";
        if (eta.TotalMinutes >= 1) return $"{eta.Minutes}m {eta.Seconds}s";
        return $"{Math.Max(1, (int)eta.TotalSeconds)}s";
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double v = Math.Max(0, bytes);
        var u = 0;
        while (v >= 1024 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }

        return u == 0 ? $"{bytes} {units[u]}" : $"{v:0.#} {units[u]}";
    }
}

public sealed class SftpBreadcrumbItem
{
    public required string Label { get; init; }
    public required string Path { get; init; }
}

public partial class SftpTreeNode : ObservableObject
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsLoaded { get; set; }
    public ObservableCollection<SftpTreeNode> Children { get; } = new();
}
