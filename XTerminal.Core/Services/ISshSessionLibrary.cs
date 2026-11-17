using XTerminal.Core.Models.Ssh;

namespace XTerminal.Core.Services;

public interface ISshSessionLibrary
{
    Task<SshLibraryDocument> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SshLibraryDocument document, CancellationToken cancellationToken = default);
}
