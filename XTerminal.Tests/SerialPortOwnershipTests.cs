using XTerminal.Protocols.Serial;

namespace XTerminal.Tests;

public class SerialPortOwnershipTests
{
    public SerialPortOwnershipTests() => SerialPortOwnership.ResetForTests();

    [Fact]
    public void Exclusive_ownership_blocks_second_session()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.True(SerialPortOwnership.TryAcquire("COM9", a, out _));
        Assert.False(SerialPortOwnership.TryAcquire("COM9", b, out var conflict));
        Assert.False(string.IsNullOrEmpty(conflict));
        SerialPortOwnership.Release("COM9", a);
        Assert.True(SerialPortOwnership.TryAcquire("COM9", b, out _));
        SerialPortOwnership.Release("COM9", b);
    }

    [Fact]
    public void Same_session_may_reacquire()
    {
        var id = Guid.NewGuid();
        Assert.True(SerialPortOwnership.TryAcquire("COM2", id, out _));
        Assert.True(SerialPortOwnership.TryAcquire("COM2", id, out _));
        SerialPortOwnership.Release("COM2", id);
        Assert.False(SerialPortOwnership.IsOwned("COM2"));
    }

    [Fact]
    public void Release_by_other_session_is_ignored()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.True(SerialPortOwnership.TryAcquire("COM4", a, out _));
        SerialPortOwnership.Release("COM4", b);
        Assert.True(SerialPortOwnership.IsOwned("COM4"));
        SerialPortOwnership.Release("COM4", a);
    }
}
