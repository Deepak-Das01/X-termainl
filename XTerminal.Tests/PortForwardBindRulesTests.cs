using XTerminal.Core.Services;

namespace XTerminal.Tests;

public class PortForwardBindRulesTests
{
    [Fact]
    public void Detects_duplicate_local_bind()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var existing = new[]
        {
            (id1, "127.0.0.1", 8080, false)
        };

        Assert.True(PortForwardBindRules.HasDuplicateBind(existing, id2, "localhost", 8080, isRemote: false));
        Assert.False(PortForwardBindRules.HasDuplicateBind(existing, id1, "127.0.0.1", 8080, isRemote: false));
        Assert.False(PortForwardBindRules.HasDuplicateBind(existing, id2, "127.0.0.1", 8081, isRemote: false));
        Assert.False(PortForwardBindRules.HasDuplicateBind(existing, id2, "127.0.0.1", 8080, isRemote: true));
    }
}
