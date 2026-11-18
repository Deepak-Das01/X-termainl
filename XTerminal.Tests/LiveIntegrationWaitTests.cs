namespace XTerminal.Tests;

public class LiveIntegrationWaitTests
{
    [Fact]
    public async Task ExpectAsync_completes_when_tcs_set()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = LiveIntegrationWait.ExpectAsync(tcs, TimeSpan.FromSeconds(2), "should not timeout");
        tcs.TrySetResult(true);
        await wait;
    }

    [Fact]
    public async Task ExpectAsync_fails_with_sanitized_timeout_message()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await LiveIntegrationWait.ExpectAsync(
                tcs,
                TimeSpan.FromMilliseconds(80),
                "Timed out waiting for OSC event."));
        Assert.Contains("Timed out waiting for OSC event", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("password", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithTimeoutAsync_fails_when_action_ignores_cancellation_too_long()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await LiveIntegrationWait.WithTimeoutAsync(
                async ct => await Task.Delay(TimeSpan.FromSeconds(30), ct),
                TimeSpan.FromMilliseconds(50),
                "operation timed out"));
        Assert.Contains("operation timed out", ex.Message, StringComparison.Ordinal);
    }
}
