using XTerminal.Core.Models.PortForwards;

namespace XTerminal.Core.Services;

public interface IPortForwardStore
{
    Task<PortForwardDocument> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(PortForwardDocument document, CancellationToken cancellationToken = default);
}

public sealed class JsonPortForwardStore : JsonDocumentStore<PortForwardDocument>, IPortForwardStore
{
    public JsonPortForwardStore(string? path = null)
        : base(path ?? AppStoragePaths.PortForwards)
    {
    }
}
