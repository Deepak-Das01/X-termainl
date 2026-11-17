using Renci.SshNet;
using XTerminal.Core.Models.Ssh;
using XTerminal.Protocols.Ssh;

namespace XTerminal.Tests;

public class SshConnectionManagerTests
{
    private static SshSessionRecord Rec(Guid id) => new()
    {
        Id = id,
        Name = "t",
        Host = "127.0.0.1",
        Port = 22,
        Username = "u"
    };

    [Fact]
    public void Release_shell_keeps_entry_while_second_shell_lease_held()
    {
        var manager = new SshConnectionManager();
        var id = Guid.NewGuid();
        using var client = new SshClient("127.0.0.1", "u", "p");
        var shell = manager.RegisterShell(id, client, Rec(id));
        var shell2 = manager.TryAcquireShell(id);
        Assert.NotNull(shell2);
        Assert.Equal(2, manager.TryGet(id)!.RefCount);

        shell.Dispose();
        Assert.NotNull(manager.TryGet(id));
        Assert.Equal(1, manager.TryGet(id)!.RefCount);

        shell2!.Dispose();
        Assert.Null(manager.TryGet(id));
    }

    [Fact]
    public void Duplicate_dispose_of_lease_is_safe()
    {
        var manager = new SshConnectionManager();
        var id = Guid.NewGuid();
        using var client = new SshClient("127.0.0.1", "u", "p");
        var lease = manager.RegisterShell(id, client, Rec(id));
        lease.Dispose();
        lease.Dispose();
        Assert.Null(manager.TryGet(id));
    }

    [Fact]
    public void Mismatched_identity_throws()
    {
        var manager = new SshConnectionManager();
        var id = Guid.NewGuid();
        using var client1 = new SshClient("127.0.0.1", "u", "p");
        _ = manager.RegisterShell(id, client1, Rec(id));
        using var client2 = new SshClient("10.0.0.1", "u", "p");
        var other = Rec(id);
        other.Host = "10.0.0.1";
        Assert.Throws<InvalidOperationException>(() => manager.RegisterShell(id, client2, other));
    }

    [Fact]
    public void Forward_acquire_requires_connected_client()
    {
        var manager = new SshConnectionManager();
        var id = Guid.NewGuid();
        using var client = new SshClient("127.0.0.1", "u", "p");
        _ = manager.RegisterShell(id, client, Rec(id));
        Assert.Null(manager.TryAcquireForward(id));
    }
}
