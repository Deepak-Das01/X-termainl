using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using XTerminal.App.Views;
using XTerminal.Core.Models.Snippets;
using XTerminal.Core.Services;

namespace XTerminal.App.ViewModels;

public partial class SnippetsViewModel : ObservableObject
{
    private static readonly Regex VariableRegex = new(@"\$\{([a-zA-Z_][a-zA-Z0-9_]*)\}", RegexOptions.Compiled);
    private readonly ISnippetStore _store;
    private SnippetDocument _document = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private SnippetRecord? _selectedSnippet;
    [ObservableProperty] private SnippetFolderRecord? _selectedFolder;
    [ObservableProperty] private bool _hasSnippets;
    [ObservableProperty] private string _shellFilter = "Any";

    public ObservableCollection<SnippetTreeNode> Roots { get; } = new();
    public ObservableCollection<SnippetRecord> FlatSnippets { get; } = new();
    public IReadOnlyList<string> ShellFilters { get; } = ["Any", "PowerShell", "CMD", "Bash", "WSL", "SSH"];

    public event Action<string, bool>? ApplySnippetRequested;

    public SnippetsViewModel(ISnippetStore store) => _store = store;

    public async Task InitializeAsync()
    {
        _document = await _store.LoadAsync();
        Rebuild();
    }

    [RelayCommand]
    private async Task NewSnippetAsync()
    {
        var editor = new SnippetEditorViewModel(new SnippetRecord { FolderId = SelectedFolder?.Id }, _document.Folders);
        var dlg = new SnippetEditorWindow(editor) { Owner = System.Windows.Application.Current?.MainWindow };
        if (dlg.ShowDialog() != true || !dlg.Saved) return;
        _document.Snippets.Add(editor.ToRecord());
        await PersistAsync();
    }

    [RelayCommand]
    private async Task EditSnippetAsync(SnippetRecord? snippet)
    {
        snippet ??= SelectedSnippet;
        if (snippet is null) return;
        var editor = new SnippetEditorViewModel(Clone(snippet), _document.Folders);
        var dlg = new SnippetEditorWindow(editor) { Owner = System.Windows.Application.Current?.MainWindow };
        if (dlg.ShowDialog() != true || !dlg.Saved) return;
        var updated = editor.ToRecord();
        var idx = _document.Snippets.FindIndex(s => s.Id == updated.Id);
        if (idx >= 0) _document.Snippets[idx] = updated;
        await PersistAsync();
    }

    [RelayCommand]
    private async Task DuplicateSnippetAsync(SnippetRecord? snippet)
    {
        snippet ??= SelectedSnippet;
        if (snippet is null) return;
        var copy = Clone(snippet);
        copy.Id = Guid.NewGuid();
        copy.Name += " (copy)";
        _document.Snippets.Add(copy);
        await PersistAsync();
    }

    [RelayCommand]
    private async Task DeleteSnippetAsync(SnippetRecord? snippet)
    {
        snippet ??= SelectedSnippet;
        if (snippet is null) return;
        if (MessageBox.Show($"Delete snippet '{snippet.Name}'?", "Confirm delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _document.Snippets.RemoveAll(s => s.Id == snippet.Id);
        await PersistAsync();
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(SnippetRecord? snippet)
    {
        snippet ??= SelectedSnippet;
        if (snippet is null) return;
        snippet.IsFavorite = !snippet.IsFavorite;
        await PersistAsync();
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        var window = new InputPromptWindow("New folder", "New folder")
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (window.ShowDialog() != true || string.IsNullOrWhiteSpace(window.Value)) return;
        _document.Folders.Add(new SnippetFolderRecord { Name = window.Value.Trim() });
        await PersistAsync();
    }

    [RelayCommand]
    private void InsertSnippet(SnippetRecord? snippet) => Apply(snippet, run: false);

    [RelayCommand]
    private void RunSnippet(SnippetRecord? snippet) => Apply(snippet, run: true);

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "snippets-export.json" };
        if (dlg.ShowDialog() != true) return;
        var json = JsonSerializer.Serialize(_document, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        });
        await File.WriteAllTextAsync(dlg.FileName, json);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog() != true) return;
        await using var stream = File.OpenRead(dlg.FileName);
        var imported = await JsonSerializer.DeserializeAsync<SnippetDocument>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        });
        if (imported is null) return;
        foreach (var f in imported.Folders)
            if (_document.Folders.All(x => x.Id != f.Id)) _document.Folders.Add(f);
        foreach (var s in imported.Snippets)
            if (_document.Snippets.All(x => x.Id != s.Id)) _document.Snippets.Add(s);
        await PersistAsync();
    }

    public void Apply(SnippetRecord? snippet, bool run)
    {
        snippet ??= SelectedSnippet;
        if (snippet is null) return;
        var text = ExpandVariables(snippet.Command);
        if (text is null) return;
        ApplySnippetRequested?.Invoke(text, run);
    }

    public string? ExpandVariables(string command)
    {
        var names = VariableRegex.Matches(command).Select(m => m.Groups[1].Value).Distinct().ToList();
        if (names.Count == 0) return command;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var window = new InputPromptWindow($"Variable: {name}", string.Empty,
                $"Enter a value for ${{{name}}}")
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            if (window.ShowDialog() != true) return null;
            values[name] = window.Value ?? string.Empty;
        }

        return VariableRegex.Replace(command, m =>
            values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    public async Task AddFromHistoryAsync(string command, string? name = null)
    {
        _document.Snippets.Add(new SnippetRecord
        {
            Name = name ?? (command.Length > 40 ? command[..40] + "…" : command),
            Command = command,
            ShellCompatibility = "Any"
        });
        await PersistAsync();
    }

    partial void OnSearchTextChanged(string value) => Rebuild();
    partial void OnFavoritesOnlyChanged(bool value) => Rebuild();
    partial void OnShellFilterChanged(string value) => Rebuild();

    private async Task PersistAsync()
    {
        await _store.SaveAsync(_document);
        Rebuild();
    }

    private void Rebuild()
    {
        var query = SearchText?.Trim() ?? string.Empty;
        Roots.Clear();
        FlatSnippets.Clear();

        Roots.Add(new SnippetTreeNode { Title = "All snippets", IsFolder = true });
        Roots.Add(new SnippetTreeNode { Title = "Favorites", IsFolder = true, IsFavoritesNode = true });
        foreach (var folder in _document.Folders.OrderBy(f => f.Name))
            Roots.Add(new SnippetTreeNode { Title = folder.Name, IsFolder = true, Folder = folder });

        foreach (var s in _document.Snippets.OrderBy(s => s.Name))
        {
            if (FavoritesOnly && !s.IsFavorite) continue;
            if (SelectedFolder is not null && s.FolderId != SelectedFolder.Id) continue;
            if (!string.IsNullOrEmpty(query) &&
                !s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !s.Command.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !(s.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                continue;
            if (!string.Equals(ShellFilter, "Any", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(s.ShellCompatibility, "Any", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(s.ShellCompatibility, ShellFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            FlatSnippets.Add(s);
        }

        HasSnippets = _document.Snippets.Count > 0;
        SelectedSnippet = FlatSnippets.FirstOrDefault(s => s.Id == SelectedSnippet?.Id);
    }

    public void SelectTreeNode(SnippetTreeNode? node)
    {
        if (node is null || node.Title == "All snippets")
        {
            SelectedFolder = null;
            FavoritesOnly = false;
        }
        else if (node.IsFavoritesNode)
        {
            SelectedFolder = null;
            FavoritesOnly = true;
        }
        else
        {
            FavoritesOnly = false;
            SelectedFolder = node.Folder;
        }

        Rebuild();
    }

    private static SnippetRecord Clone(SnippetRecord s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Description = s.Description,
        Command = s.Command,
        ShellCompatibility = s.ShellCompatibility,
        Tags = s.Tags.ToList(),
        FolderId = s.FolderId,
        IsFavorite = s.IsFavorite,
        CreatedUtc = s.CreatedUtc,
        ModifiedUtc = s.ModifiedUtc
    };
}

public sealed class SnippetTreeNode
{
    public string Title { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public bool IsFavoritesNode { get; set; }
    public SnippetFolderRecord? Folder { get; set; }
}

public partial class SnippetEditorViewModel : ObservableObject
{
    public SnippetEditorViewModel(SnippetRecord record, IReadOnlyList<SnippetFolderRecord> folders)
    {
        Id = record.Id;
        Name = record.Name;
        Description = record.Description ?? string.Empty;
        Command = record.Command;
        ShellCompatibility = record.ShellCompatibility;
        TagsText = string.Join(", ", record.Tags);
        IsFavorite = record.IsFavorite;
        Folders = folders;
        SelectedFolder = folders.FirstOrDefault(f => f.Id == record.FolderId);
        ShellOptions = new[] { "Any", "PowerShell", "CMD", "Bash", "WSL", "SSH" };
    }

    public Guid Id { get; }
    public IReadOnlyList<SnippetFolderRecord> Folders { get; }
    public IReadOnlyList<string> ShellOptions { get; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _command = string.Empty;
    [ObservableProperty] private string _shellCompatibility = "Any";
    [ObservableProperty] private string _tagsText = string.Empty;
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private SnippetFolderRecord? _selectedFolder;
    [ObservableProperty] private string? _validationError;

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) { ValidationError = "Name is required."; return false; }
        if (string.IsNullOrWhiteSpace(Command)) { ValidationError = "Command is required."; return false; }
        ValidationError = null;
        return true;
    }

    public SnippetRecord ToRecord() => new()
    {
        Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
        Name = Name.Trim(),
        Description = Description.Trim(),
        Command = Command,
        ShellCompatibility = ShellCompatibility,
        Tags = TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        FolderId = SelectedFolder?.Id,
        IsFavorite = IsFavorite,
        ModifiedUtc = DateTimeOffset.UtcNow
    };
}
