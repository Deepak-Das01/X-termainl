using XTerminal.Core.Models.Snippets;

namespace XTerminal.Core.Services;

public interface ISnippetStore
{
    Task<SnippetDocument> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SnippetDocument document, CancellationToken cancellationToken = default);
}

public sealed class JsonSnippetStore : JsonDocumentStore<SnippetDocument>, ISnippetStore
{
    public JsonSnippetStore(string? path = null)
        : base(path ?? AppStoragePaths.Snippets, () => new SnippetDocument
        {
            Snippets =
            [
                new SnippetRecord
                {
                    Name = "Kubectl Pods",
                    Description = "List pods across namespaces",
                    Command = "kubectl get pods -A",
                    ShellCompatibility = "Bash",
                    Tags = ["k8s", "kubectl"]
                },
                new SnippetRecord
                {
                    Name = "System Health",
                    Description = "Quick host health",
                    Command = "uptime && free -h && df -h",
                    ShellCompatibility = "Bash",
                    Tags = ["linux"]
                },
                new SnippetRecord
                {
                    Name = "Docker Containers",
                    Description = "Running containers",
                    Command = "docker ps",
                    ShellCompatibility = "Any",
                    Tags = ["docker"]
                }
            ]
        })
    {
    }
}
