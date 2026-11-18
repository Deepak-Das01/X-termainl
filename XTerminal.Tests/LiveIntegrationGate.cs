namespace XTerminal.Tests;

/// <summary>
/// Opt-in gate for private-server integration tests. Never enabled in CI by default.
/// </summary>
public static class LiveIntegrationGate
{
    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("XTERMINAL_IT_ENABLE"), "1", StringComparison.Ordinal);

    public static string? Host => NullIfEmpty(Environment.GetEnvironmentVariable("XTERMINAL_IT_HOST"));
    public static string? User => NullIfEmpty(Environment.GetEnvironmentVariable("XTERMINAL_IT_USER"));
    public static int Port =>
        int.TryParse(Environment.GetEnvironmentVariable("XTERMINAL_IT_PORT"), out var p) && p is > 0 and <= 65535
            ? p
            : 22;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
