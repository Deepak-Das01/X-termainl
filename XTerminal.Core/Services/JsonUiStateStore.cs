using XTerminal.Core.Models.Ui;

namespace XTerminal.Core.Services;

public interface IUiStateStore
{
    Task<UiStateDocument> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(UiStateDocument document, CancellationToken cancellationToken = default);
}

public sealed class JsonUiStateStore : JsonDocumentStore<UiStateDocument>, IUiStateStore
{
    public JsonUiStateStore(string? path = null)
        : base(path ?? AppStoragePaths.UiState)
    {
    }
}
