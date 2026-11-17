using XTerminal.Protocols.Ssh;

namespace XTerminal.Tests;

public class SshHostKeyPolicyTests
{
    private sealed class ScriptedPrompt : ISshHostKeyPrompt
    {
        private readonly Queue<HostKeyPromptResult> _results;
        public List<HostKeyPromptRequest> Seen { get; } = new();

        public ScriptedPrompt(params HostKeyPromptResult[] results) =>
            _results = new Queue<HostKeyPromptResult>(results);

        public HostKeyPromptResult Prompt(HostKeyPromptRequest request)
        {
            Seen.Add(request);
            return _results.Count > 0 ? _results.Dequeue() : HostKeyPromptResult.Reject;
        }
    }

    private static (KnownHostsStore Store, SshHostKeyPolicy Policy, ScriptedPrompt Prompt) Create(
        params HostKeyPromptResult[] results)
    {
        var path = Path.Combine(Path.GetTempPath(), "xterminal-tests", Guid.NewGuid().ToString("N"), "known_hosts");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var store = new KnownHostsStore(path);
        var prompt = new ScriptedPrompt(results);
        return (store, new SshHostKeyPolicy(store, prompt), prompt);
    }

    [Fact]
    public void Reject_unknown_denies()
    {
        var (_, policy, prompt) = Create(HostKeyPromptResult.Reject);
        Assert.False(policy.EvaluateAndDecide("u.example", 22, new byte[] { 5, 5, 5 }, true));
        Assert.Single(prompt.Seen);
        Assert.Equal(HostKeyStatus.Unknown, prompt.Seen[0].Status);
    }

    [Fact]
    public void TrustAndSave_persists_unknown()
    {
        var (store, policy, _) = Create(HostKeyPromptResult.TrustAndSave);
        var key = new byte[] { 7, 7, 7 };
        Assert.True(policy.EvaluateAndDecide("save.example", 22, key, true));
        Assert.Equal(HostKeyStatus.Trusted, store.Evaluate("save.example", 22, key, out _, out _));
    }

    [Fact]
    public void TrustOnce_does_not_persist()
    {
        var (store, policy, _) = Create(HostKeyPromptResult.TrustOnce);
        var key = new byte[] { 8, 8, 8 };
        Assert.True(policy.EvaluateAndDecide("once.example", 22, key, true));
        Assert.Equal(HostKeyStatus.Unknown, store.Evaluate("once.example", 22, key, out _, out _));
    }

    [Fact]
    public void Changed_key_rejects_unless_ReplaceAndSave()
    {
        var (store, policy, _) = Create(HostKeyPromptResult.TrustAndSave);
        store.Trust("chg.example", 22, new byte[] { 1 });
        Assert.False(policy.EvaluateAndDecide("chg.example", 22, new byte[] { 2 }, true));

        var (store2, policy2, _) = Create(HostKeyPromptResult.ReplaceAndSave);
        store2.Trust("chg.example", 22, new byte[] { 1 });
        Assert.True(policy2.EvaluateAndDecide("chg.example", 22, new byte[] { 2 }, true));
        Assert.Equal(HostKeyStatus.Trusted, store2.Evaluate("chg.example", 22, new byte[] { 2 }, out _, out _));
    }
}
