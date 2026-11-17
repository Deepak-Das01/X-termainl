using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTerminal.Core.Models.History;
using XTerminal.Core.Services;
using XTerminal.Core.Settings;

namespace XTerminal.App.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryStore _store;
    private HistoryDocument _document = new();
    private HistorySettings _settings = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _rangeFilter = "30 Days";
    [ObservableProperty] private string _sourceFilter = "All";
    [ObservableProperty] private string _shellFilter = "All";
    [ObservableProperty] private HistoryEntry? _selectedEntry;
    [ObservableProperty] private bool _hasEntries;
    [ObservableProperty] private bool _historyEnabled = true;

    public ObservableCollection<HistoryEntry> Entries { get; } = new();
    public IReadOnlyList<string> RangeFilters { get; } = ["Today", "7 Days", "30 Days", "All"];
    public IReadOnlyList<string> SourceFilters { get; } = ["All", "Local", "SSH"];
    public IReadOnlyList<string> ShellFilters { get; } = ["All", "PowerShell", "CMD", "WSL", "Bash", "SSH"];

    public event Action<string, bool>? ApplyCommandRequested;
    public event Action<string>? CreateSnippetRequested;

    public HistoryViewModel(IHistoryStore store) => _store = store;

    public async Task InitializeAsync(HistorySettings settings)
    {
        _settings = settings;
        HistoryEnabled = settings.Enabled;
        _document = await _store.LoadAsync();
        JsonHistoryStore.ApplyRetention(_document, _settings);
        Rebuild();
    }

    public void ApplySettings(HistorySettings settings)
    {
        _settings = settings;
        HistoryEnabled = settings.Enabled;
        Rebuild();
        _ = PersistAsync();
    }

    public async Task RecordAsync(HistoryEntry entry)
    {
        if (!_settings.Enabled) return;
        if (_settings.ExcludeSshSessions && entry.IsSsh) return;
        if (ShouldExclude(entry.Command)) return;

        var last = _document.Entries.LastOrDefault();
        if (last is not null &&
            last.Command == entry.Command &&
            last.SessionName == entry.SessionName &&
            (entry.TimestampUtc - last.TimestampUtc).TotalSeconds < 2)
            return;

        _document.Entries.Add(entry);
        JsonHistoryStore.ApplyRetention(_document, _settings);
        await _store.SaveAsync(_document);
        if (ActiveSectionShowsHistory()) Rebuild();
    }

    private bool ActiveSectionShowsHistory() => true;

    [RelayCommand]
    private void Insert(HistoryEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is not null) ApplyCommandRequested?.Invoke(entry.Command, false);
    }

    [RelayCommand]
    private void RunAgain(HistoryEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is not null) ApplyCommandRequested?.Invoke(entry.Command, true);
    }

    [RelayCommand]
    private void Copy(HistoryEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is null) return;
        try { System.Windows.Clipboard.SetText(entry.Command); } catch { /* ignore */ }
    }

    [RelayCommand]
    private void FavoriteAsSnippet(HistoryEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is not null) CreateSnippetRequested?.Invoke(entry.Command);
    }

    [RelayCommand]
    private async Task DeleteAsync(HistoryEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry is null) return;
        _document.Entries.RemoveAll(e => e.Id == entry.Id);
        await PersistAsync();
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (System.Windows.MessageBox.Show(
                "Clear all command history?",
                "Clear History",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
            return;
        _document.Entries.Clear();
        await PersistAsync();
    }

    partial void OnSearchTextChanged(string value) => Rebuild();
    partial void OnRangeFilterChanged(string value) => Rebuild();
    partial void OnSourceFilterChanged(string value) => Rebuild();
    partial void OnShellFilterChanged(string value) => Rebuild();

    private async Task PersistAsync()
    {
        JsonHistoryStore.ApplyRetention(_document, _settings);
        await _store.SaveAsync(_document);
        Rebuild();
    }

    private void Rebuild()
    {
        Entries.Clear();
        var query = SearchText?.Trim() ?? string.Empty;
        var now = DateTimeOffset.UtcNow;

        foreach (var e in _document.Entries.OrderByDescending(x => x.TimestampUtc))
        {
            if (!MatchesRange(e, now)) continue;
            if (SourceFilter == "Local" && e.IsSsh) continue;
            if (SourceFilter == "SSH" && !e.IsSsh) continue;
            if (ShellFilter != "All" &&
                !(e.ShellType?.Contains(ShellFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                continue;
            if (!string.IsNullOrEmpty(query) &&
                !(e.Command?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) &&
                !(e.SessionName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) &&
                !(e.WorkingDirectory?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) &&
                !(e.Hostname?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) &&
                !(e.ShellType?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                continue;

            Entries.Add(e);
            if (Entries.Count >= 500) break; // virtualization-friendly cap for UI
        }

        HasEntries = Entries.Count > 0 || _document.Entries.Count > 0;
        // empty state: HasEntries false only when document empty
        HasEntries = _document.Entries.Count > 0;
    }

    private bool MatchesRange(HistoryEntry e, DateTimeOffset now) => RangeFilter switch
    {
        "Today" => e.TimestampUtc.Date == now.Date,
        "7 Days" => e.TimestampUtc >= now.AddDays(-7),
        "30 Days" => e.TimestampUtc >= now.AddDays(-30),
        _ => true
    };

    private bool ShouldExclude(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return true;
        foreach (var pattern in _settings.ExcludePatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
