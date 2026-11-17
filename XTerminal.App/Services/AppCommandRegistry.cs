using System.Collections.ObjectModel;

namespace XTerminal.App.Services;

public enum CommandCategory
{
    Terminal, Ssh, Files, Editor, Sftp, Sessions, Ports, Snippets, History, Settings, Appearance, Application
}

public sealed class AppCommand
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required CommandCategory Category { get; init; }
    public string? Shortcut { get; set; }
    public Func<bool>? CanExecute { get; init; }
    public required Action Execute { get; init; }
    public string SearchText => $"{Category} {Title} {Id}".ToLowerInvariant();
}

public sealed class AppCommandRegistry
{
    private readonly List<AppCommand> _commands = new();
    private readonly List<string> _recentIds = new();

    public IReadOnlyList<AppCommand> Commands => _commands;
    public IReadOnlyList<string> RecentIds => _recentIds;

    public void Register(AppCommand command)
    {
        _commands.RemoveAll(c => c.Id == command.Id);
        _commands.Add(command);
    }

    public void RegisterMany(IEnumerable<AppCommand> commands)
    {
        foreach (var c in commands) Register(c);
    }

    public void MarkUsed(string id)
    {
        _recentIds.Remove(id);
        _recentIds.Insert(0, id);
        while (_recentIds.Count > 12) _recentIds.RemoveAt(_recentIds.Count - 1);
    }

    public IEnumerable<AppCommand> Search(string query)
    {
        query = (query ?? string.Empty).Trim().ToLowerInvariant();
        IEnumerable<AppCommand> source = _commands.Where(c => c.CanExecute?.Invoke() ?? true);

        if (string.IsNullOrEmpty(query))
        {
            var recent = _recentIds
                .Select(id => _commands.FirstOrDefault(c => c.Id == id))
                .Where(c => c is not null)!
                .Cast<AppCommand>();
            return recent.Concat(source).DistinctBy(c => c.Id).Take(40);
        }

        return source
            .Select(c => (cmd: c, score: FuzzyScore(c.SearchText, query)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.cmd.Title)
            .Select(x => x.cmd)
            .Take(40);
    }

    private static int FuzzyScore(string text, string query)
    {
        if (text.Contains(query, StringComparison.Ordinal)) return 100 + (100 - Math.Min(query.Length, 50));
        var ti = 0;
        var score = 0;
        foreach (var ch in query)
        {
            var idx = text.IndexOf(ch, ti);
            if (idx < 0) return 0;
            score += 10 - Math.Min(idx - ti, 9);
            ti = idx + 1;
        }

        return score;
    }
}

public sealed class QuickSwitchItem
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Kind { get; init; }
    public required Action Activate { get; init; }
    public string SearchText => $"{Kind} {Title} {Subtitle}".ToLowerInvariant();
}
