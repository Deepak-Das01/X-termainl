using XTerminal.Core.Services;

namespace XTerminal.Tests;

public class CommandHistoryPrivateInputTests
{
    [Fact]
    public void Suppress_discards_private_input_and_emits_nothing()
    {
        var buffer = new CommandLineCaptureBuffer { Suppress = true };
        var flushed = buffer.Process("secret-password\r");
        Assert.Empty(flushed);

        buffer.Suppress = false;
        flushed = buffer.Process("ls -la\r");
        Assert.Equal(["ls -la"], flushed);
    }

    [Fact]
    public void Switching_to_suppress_clears_partial_line()
    {
        var buffer = new CommandLineCaptureBuffer();
        buffer.Process("partial");
        buffer.Suppress = true;
        Assert.Empty(buffer.Process("x\r"));

        buffer.Suppress = false;
        Assert.Empty(buffer.Process("\r")); // no leftover
        Assert.Equal(["whoami"], buffer.Process("whoami\r"));
    }
}
