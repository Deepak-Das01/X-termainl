using XTerminal.Protocols.Ssh;

namespace XTerminal.Tests;

public class KnownHostsStoreTests
{
    private static KnownHostsStore CreateStore()
    {
        var path = Path.Combine(Path.GetTempPath(), "xterminal-tests", Guid.NewGuid().ToString("N"), "known_hosts");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new KnownHostsStore(path);
    }

    [Fact]
    public void Unknown_host_evaluates_as_Unknown()
    {
        var store = CreateStore();
        var key = new byte[] { 1, 2, 3, 4 };
        var status = store.Evaluate("example.test", 22, key, out var fp, out var prev);
        Assert.Equal(HostKeyStatus.Unknown, status);
        Assert.False(string.IsNullOrEmpty(fp));
        Assert.Null(prev);
    }

    [Fact]
    public void Trust_then_evaluate_Trusted()
    {
        var store = CreateStore();
        var key = new byte[] { 9, 8, 7, 6 };
        store.Trust("host.example", 22, key);
        var status = store.Evaluate("HOST.EXAMPLE", 22, key, out _, out _);
        Assert.Equal(HostKeyStatus.Trusted, status);
    }

    [Fact]
    public void Changed_fingerprint_does_not_auto_trust()
    {
        var store = CreateStore();
        store.Trust("host.example", 22, new byte[] { 1, 1, 1, 1 });
        var status = store.Evaluate("host.example", 22, new byte[] { 2, 2, 2, 2 }, out _, out var prev);
        Assert.Equal(HostKeyStatus.Changed, status);
        Assert.False(string.IsNullOrEmpty(prev));
        Assert.False(store.IsTrusted("host.example", 22, new byte[] { 2, 2, 2, 2 }, out var isNew));
        Assert.False(isNew);
    }

    [Fact]
    public void Replace_requires_explicit_Trust_call()
    {
        var store = CreateStore();
        var oldKey = new byte[] { 1, 1, 1, 1 };
        var newKey = new byte[] { 3, 3, 3, 3 };
        store.Trust("a.b", 2222, oldKey);
        Assert.Equal(HostKeyStatus.Changed, store.Evaluate("a.b", 2222, newKey, out _, out _));
        store.Trust("a.b", 2222, newKey);
        Assert.Equal(HostKeyStatus.Trusted, store.Evaluate("a.b", 2222, newKey, out _, out _));
    }
}
