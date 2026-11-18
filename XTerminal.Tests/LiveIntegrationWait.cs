namespace XTerminal.Tests;

/// <summary>Bounded awaits for live integration — never wait on a TCS indefinitely.</summary>
internal static class LiveIntegrationWait
{
    public static async Task<T> ExpectAsync<T>(
        TaskCompletionSource<T> tcs,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            return await tcs.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Assert.Fail(timeoutMessage);
            throw; // unreachable
        }
    }

    public static async Task ExpectAsync(
        TaskCompletionSource<bool> tcs,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken = default)
    {
        await ExpectAsync<bool>(tcs, timeout, timeoutMessage, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WithTimeoutAsync(
        Func<CancellationToken, Task> action,
        TimeSpan timeout,
        string timeoutMessage)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await action(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Assert.Fail(timeoutMessage);
        }
    }
}
