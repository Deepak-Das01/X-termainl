using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTerminal.App.Helpers;
using XTerminal.App.Views;
using XTerminal.Core.Models;
using XTerminal.Core.Services;
using XTerminal.Core.Settings;

namespace XTerminal.App.ViewModels;

public partial class FilesExplorerViewModel : ObservableObject
{
    private readonly IUiStateStore _uiState;
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private readonly HashSet<string> _expandedPaths = new(StringComparer.OrdinalIgnoreCase);
    private FilesAppSettings _filesSettings = new();

    [ObservableProperty] private string _pathText = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    [ObservableProperty] private LocalFileItem? _selectedItem;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _errorBanner;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;
    [ObservableProperty] private string _quickFilter = string.Empty;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _searchRecursive;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _searchStatus = string.Empty;
    [ObservableProperty] private bool _showSearchResults;

    public ObservableCollection<LocalFileItem> Entries { get; } = new();
    public ObservableCollection<LocalFileItem> SearchResults { get; } = new();
    public ObservableCollection<FolderTreeNode> TreeRoots { get; } = new();
    private List<LocalFileItem> _allEntries = new();
    private CancellationTokenSource? _searchCts;

    public event Action<string>? OpenFileRequested;
    public event Action<string, ShellKind>? OpenTerminalRequested;

    public FilesExplorerViewModel(IUiStateStore uiState) => _uiState = uiState;

    public void ApplyFilesSettings(FilesAppSettings settings)
    {
        _filesSettings = settings ?? new FilesAppSettings();
        _ = RefreshAsync();
    }

    public async Task InitializeAsync()
    {
        var state = await _uiState.LoadAsync();
        foreach (var p in state.FilesExpandedPaths)
            _expandedPaths.Add(p);

        var start = !string.IsNullOrWhiteSpace(state.FilesLastPath) && Directory.Exists(state.FilesLastPath)
            ? state.FilesLastPath!
            : (!string.IsNullOrWhiteSpace(_filesSettings.DefaultDownloadDirectory) &&
               Directory.Exists(_filesSettings.DefaultDownloadDirectory)
                ? _filesSettings.DefaultDownloadDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        await BuildDriveTreeAsync();
        await NavigateAsync(start, pushHistory: false);
    }

    [RelayCommand]
    private async Task RefreshAsync() => await NavigateAsync(PathText, pushHistory: false);

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (_back.Count == 0) return;
        _forward.Push(PathText);
        var target = _back.Pop();
        UpdateNavFlags();
        await NavigateAsync(target, pushHistory: false);
    }

    [RelayCommand]
    private async Task GoForwardAsync()
    {
        if (_forward.Count == 0) return;
        _back.Push(PathText);
        var target = _forward.Pop();
        UpdateNavFlags();
        await NavigateAsync(target, pushHistory: false);
    }

    [RelayCommand]
    private async Task GoUpAsync()
    {
        try
        {
            var parent = Directory.GetParent(PathText)?.FullName;
            if (!string.IsNullOrEmpty(parent))
                await NavigateAsync(parent, pushHistory: true);
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    [RelayCommand]
    private async Task GoHomeAsync() =>
        await NavigateAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), pushHistory: true);

    [RelayCommand]
    private async Task NavigateTypedPathAsync() => await NavigateAsync(PathText, pushHistory: true);

    [RelayCommand]
    private async Task OpenEntryAsync(LocalFileItem? item)
    {
        item ??= SelectedItem;
        if (item is null) return;
        if (item.IsDirectory)
            await NavigateAsync(item.FullPath, pushHistory: true);
        else
            OpenFileRequested?.Invoke(item.FullPath);
    }

    [RelayCommand]
    private void OpenInEditor(LocalFileItem? item)
    {
        item ??= SelectedItem;
        if (item is null || item.IsDirectory) return;
        OpenFileRequested?.Invoke(item.FullPath);
    }

    [RelayCommand]
    private void OpenTerminalHere(LocalFileItem? item)
    {
        item ??= SelectedItem;
        var dir = ResolveDirectory(item);
        if (dir is not null) OpenTerminalRequested?.Invoke(dir, ShellKind.PowerShell);
    }

    [RelayCommand]
    private void OpenPowerShellHere(LocalFileItem? item)
    {
        item ??= SelectedItem;
        var dir = ResolveDirectory(item);
        if (dir is not null) OpenTerminalRequested?.Invoke(dir, ShellKind.PowerShell);
    }

    [RelayCommand]
    private void OpenCmdHere(LocalFileItem? item)
    {
        item ??= SelectedItem;
        var dir = ResolveDirectory(item);
        if (dir is not null) OpenTerminalRequested?.Invoke(dir, ShellKind.Cmd);
    }

    [RelayCommand]
    private void RevealInExplorer(LocalFileItem? item)
    {
        item ??= SelectedItem;
        try
        {
            if (item is null)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", PathText) { UseShellExecute = true });
                return;
            }

            if (item.IsDirectory)
                Process.Start(new ProcessStartInfo("explorer.exe", item.FullPath) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    [RelayCommand]
    private void CopyPath(LocalFileItem? item)
    {
        item ??= SelectedItem;
        var path = item?.FullPath ?? PathText;
        try { System.Windows.Clipboard.SetText(path); } catch { /* ignore */ }
    }

    [RelayCommand]
    private void CopyFileName(LocalFileItem? item)
    {
        item ??= SelectedItem;
        if (item is null) return;
        try { System.Windows.Clipboard.SetText(item.Name); } catch { /* ignore */ }
    }

    [RelayCommand]
    private async Task NewFileAsync()
    {
        var window = new InputPromptWindow("New file", "untitled.txt")
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (window.ShowDialog() != true || string.IsNullOrWhiteSpace(window.Value)) return;
        try
        {
            var path = Path.Combine(PathText, window.Value.Trim());
            await File.WriteAllTextAsync(path, string.Empty);
            await RefreshAsync();
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        var window = new InputPromptWindow("New folder", "New folder")
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (window.ShowDialog() != true || string.IsNullOrWhiteSpace(window.Value)) return;
        try
        {
            Directory.CreateDirectory(Path.Combine(PathText, window.Value.Trim()));
            await RefreshAsync();
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    [RelayCommand]
    private async Task RenameAsync(LocalFileItem? item)
    {
        item ??= SelectedItem;
        if (item is null) return;
        var window = new InputPromptWindow("Rename", item.Name)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (window.ShowDialog() != true || string.IsNullOrWhiteSpace(window.Value)) return;
        try
        {
            var dest = Path.Combine(Path.GetDirectoryName(item.FullPath)!, window.Value.Trim());
            if (item.IsDirectory) Directory.Move(item.FullPath, dest);
            else File.Move(item.FullPath, dest);
            await RefreshAsync();
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    [RelayCommand]
    private async Task DeleteAsync(LocalFileItem? item)
    {
        item ??= SelectedItem;
        if (item is null) return;
        var result = System.Windows.MessageBox.Show(
            $"Move '{item.Name}' to the Recycle Bin?",
            "Confirm delete",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;
        try
        {
            await Task.Run(() => RecycleBin.Delete(item.FullPath));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            var permanent = System.Windows.MessageBox.Show(
                $"Recycle Bin failed: {ex.Message}\n\nDelete permanently?",
                "Delete failed",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (permanent != System.Windows.MessageBoxResult.Yes)
            {
                SetError(ex.Message);
                return;
            }

            try
            {
                if (item.IsDirectory) Directory.Delete(item.FullPath, recursive: true);
                else File.Delete(item.FullPath);
                await RefreshAsync();
            }
            catch (Exception ex2) { SetError(ex2.Message); }
        }
    }

    [RelayCommand]
    private void ShowProperties(LocalFileItem? item)
    {
        item ??= SelectedItem;
        if (item is null) return;
        try
        {
            var info = item.IsDirectory
                ? (FileSystemInfo)new DirectoryInfo(item.FullPath)
                : new FileInfo(item.FullPath);
            System.Windows.MessageBox.Show(
                $"{item.Name}\n\nPath: {item.FullPath}\nType: {item.TypeLabel}\nSize: {item.SizeDisplay}\nModified: {item.ModifiedDisplay}\nAttributes: {info.Attributes}",
                "Properties",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    public Task NavigateToPathAsync(string path) => NavigateAsync(path, pushHistory: true);

    public async Task ExpandFolderNodeAsync(FolderTreeNode node)
    {
        if (!node.IsFolder || node.ChildrenLoaded) return;
        node.IsLoading = true;
        try
        {
            var children = await Task.Run(() => EnumerateChildFolders(node.FullPath));
            node.Children.Clear();
            foreach (var child in children)
            {
                var n = new FolderTreeNode
                {
                    Name = child.Name,
                    FullPath = child.FullName,
                    IsFolder = true,
                    IsExpanded = _expandedPaths.Contains(child.FullName)
                };
                n.Children.Add(new FolderTreeNode { Name = "…", IsPlaceholder = true });
                node.Children.Add(n);
            }

            node.ChildrenLoaded = true;
            _expandedPaths.Add(node.FullPath);
            await PersistUiStateAsync();
        }
        catch (Exception ex)
        {
            node.Children.Clear();
            node.Children.Add(new FolderTreeNode { Name = $"({ex.Message})", IsPlaceholder = true });
        }
        finally
        {
            node.IsLoading = false;
        }
    }

    public void RememberCollapsed(string path)
    {
        _expandedPaths.Remove(path);
        _ = PersistUiStateAsync();
    }

    private async Task NavigateAsync(string path, bool pushHistory)
    {
        ClearError();
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(path))
        {
            SetError("Path is empty.");
            return;
        }

        if (!Directory.Exists(path))
        {
            SetError($"Path unavailable: {path}");
            return;
        }

        if (pushHistory &&
            !string.Equals(PathText, path, StringComparison.OrdinalIgnoreCase))
        {
            _back.Push(PathText);
            _forward.Clear();
        }

        IsLoading = true;
        try
        {
            var items = await Task.Run(() => EnumerateDirectory(path));
            _allEntries = items;
            ApplyQuickFilter();
            PathText = path;
            SelectedItem = null;
            UpdateNavFlags();
            await PersistUiStateAsync();
        }
        catch (UnauthorizedAccessException)
        {
            SetError("Access denied.");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task BuildDriveTreeAsync()
    {
        TreeRoots.Clear();
        var drives = await Task.Run(() =>
            DriveInfo.GetDrives().Where(d => d.IsReady).OrderBy(d => d.Name).ToList());

        foreach (var drive in drives)
        {
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name.TrimEnd('\\')
                : $"{drive.Name.TrimEnd('\\')} ({drive.VolumeLabel})";
            var node = new FolderTreeNode
            {
                Name = label,
                FullPath = drive.RootDirectory.FullName,
                IsFolder = true,
                IsExpanded = _expandedPaths.Contains(drive.RootDirectory.FullName)
            };
            node.Children.Add(new FolderTreeNode { Name = "…", IsPlaceholder = true });
            TreeRoots.Add(node);
        }
    }

    private List<LocalFileItem> EnumerateDirectory(string path)
    {
        var list = new List<LocalFileItem>();
        var dir = new DirectoryInfo(path);

        foreach (var d in dir.EnumerateDirectories().OrderBy(x => x.Name))
        {
            if (!_filesSettings.ShowHiddenFiles && IsHidden(d)) continue;
            list.Add(new LocalFileItem
            {
                Name = d.Name,
                FullPath = d.FullName,
                IsDirectory = true,
                TypeLabel = "Folder",
                SizeDisplay = "—",
                ModifiedDisplay = d.LastWriteTime.ToString("g"),
                AttributesDisplay = d.Attributes.ToString()
            });
        }

        foreach (var f in dir.EnumerateFiles().OrderBy(x => x.Name))
        {
            if (!_filesSettings.ShowHiddenFiles && IsHidden(f)) continue;
            list.Add(new LocalFileItem
            {
                Name = f.Name,
                FullPath = f.FullName,
                IsDirectory = false,
                TypeLabel = string.IsNullOrEmpty(f.Extension) ? "File" : f.Extension.TrimStart('.').ToUpperInvariant(),
                SizeBytes = f.Length,
                SizeDisplay = FormatSize(f.Length),
                ModifiedDisplay = f.LastWriteTime.ToString("g"),
                AttributesDisplay = f.Attributes.ToString()
            });
        }

        return list;
    }

    private static bool IsHidden(FileSystemInfo info) =>
        info.Attributes.HasFlag(FileAttributes.Hidden) || info.Name.StartsWith('.');

    private static List<DirectoryInfo> EnumerateChildFolders(string path)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateDirectories()
                .OrderBy(d => d.Name)
                .Take(500)
                .ToList();
        }
        catch
        {
            return new List<DirectoryInfo>();
        }
    }

    private static string? ResolveDirectory(LocalFileItem? item)
    {
        if (item is null) return null;
        return item.IsDirectory ? item.FullPath : Path.GetDirectoryName(item.FullPath);
    }

    private void UpdateNavFlags()
    {
        CanGoBack = _back.Count > 0;
        CanGoForward = _forward.Count > 0;
    }

    partial void OnQuickFilterChanged(string value) => ApplyQuickFilter();

    private void ApplyQuickFilter()
    {
        var q = QuickFilter?.Trim() ?? string.Empty;
        Entries.Clear();
        foreach (var i in _allEntries)
        {
            if (string.IsNullOrEmpty(q) ||
                i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.TypeLabel.Contains(q, StringComparison.OrdinalIgnoreCase))
                Entries.Add(i);
        }

        ItemCount = Entries.Count;
        StatusText = string.IsNullOrEmpty(q) ? $"{ItemCount} items" : $"{ItemCount} filtered";
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        var query = SearchQuery?.Trim() ?? string.Empty;
        SearchResults.Clear();
        if (string.IsNullOrEmpty(query))
        {
            SearchStatus = string.Empty;
            ShowSearchResults = false;
            return;
        }

        IsSearching = true;
        ShowSearchResults = true;
        SearchStatus = "Searching…";
        try
        {
            var root = PathText;
            var recursive = SearchRecursive;
            var results = await Task.Run(() =>
            {
                var list = new List<LocalFileItem>();
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                try
                {
                    foreach (var path in Directory.EnumerateFileSystemEntries(root, "*" + query + "*", option))
                    {
                        token.ThrowIfCancellationRequested();
                        if (list.Count >= 500) break;
                        try
                        {
                            var isDir = Directory.Exists(path);
                            var info = isDir ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
                            list.Add(new LocalFileItem
                            {
                                Name = info.Name,
                                FullPath = info.FullName,
                                IsDirectory = isDir,
                                TypeLabel = isDir ? "Folder" : (Path.GetExtension(path).TrimStart('.').ToUpperInvariant()),
                                SizeDisplay = isDir ? "—" : FormatSize(((FileInfo)info).Length),
                                ModifiedDisplay = info.LastWriteTime.ToString("g")
                            });
                        }
                        catch { /* skip inaccessible */ }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* root access */ }

                return list;
            }, token);

            SearchResults.Clear();
            foreach (var r in results) SearchResults.Add(r);
            SearchStatus = $"{SearchResults.Count} results";
            ShowSearchResults = true;
        }
        catch (OperationCanceledException)
        {
            SearchStatus = "Cancelled";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchCts?.Cancel();
        SearchQuery = string.Empty;
        SearchResults.Clear();
        SearchStatus = string.Empty;
        ShowSearchResults = false;
        IsSearching = false;
    }

    private async Task PersistUiStateAsync()
    {
        try
        {
            var state = await _uiState.LoadAsync();
            state.FilesLastPath = PathText;
            state.FilesExpandedPaths = _expandedPaths.Take(200).ToList();
            await _uiState.SaveAsync(state);
        }
        catch { /* ignore */ }
    }

    private void SetError(string message)
    {
        ErrorBanner = message;
        HasError = true;
    }

    private void ClearError()
    {
        ErrorBanner = null;
        HasError = false;
    }

    private string FormatSize(long bytes)
    {
        if (!_filesSettings.HumanReadableSizes)
            return $"{bytes} B";
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB"
        };
    }
}

public sealed class LocalFileItem
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public string TypeLabel { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string SizeDisplay { get; init; } = "—";
    public string ModifiedDisplay { get; init; } = string.Empty;
    public string AttributesDisplay { get; init; } = string.Empty;
}

public partial class FolderTreeNode : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public bool IsPlaceholder { get; set; }
    public bool ChildrenLoaded { get; set; }
    public ObservableCollection<FolderTreeNode> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isLoading;
}
