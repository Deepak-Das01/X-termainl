using XTerminal.Core.Models;

namespace XTerminal.Core.Services;

public interface ISessionStore
{
    Task<IReadOnlyList<SessionDefinition>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IEnumerable<SessionDefinition> sessions, CancellationToken cancellationToken = default);
}
