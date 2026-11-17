using XTerminal.Core.Models.History;
using XTerminal.Core.Settings;

namespace XTerminal.Core.Services;

public interface IHistoryStore
{
    Task<HistoryDocument> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(HistoryDocument document, CancellationToken cancellationToken = default);
}

public sealed class JsonHistoryStore : JsonDocumentStore<HistoryDocument>, IHistoryStore
{
    public JsonHistoryStore(string? path = null)
        : base(path ?? AppStoragePaths.History)
    {
    }

    public static void ApplyRetention(HistoryDocument doc, HistorySettings settings)
    {
        doc.Entries ??= new List<HistoryEntry>();
        if (settings.RetentionDays > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-settings.RetentionDays);
            doc.Entries.RemoveAll(e => e.TimestampUtc < cutoff);
        }

        if (settings.MaxEntries > 0 && doc.Entries.Count > settings.MaxEntries)
        {
            doc.Entries = doc.Entries
                .OrderByDescending(e => e.TimestampUtc)
                .Take(settings.MaxEntries)
                .OrderBy(e => e.TimestampUtc)
                .ToList();
        }
    }
}
