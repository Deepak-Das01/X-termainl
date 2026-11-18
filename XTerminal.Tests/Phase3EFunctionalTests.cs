using XTerminal.Core.Services;

namespace XTerminal.Tests;

public class Phase3EFunctionalTests
{
    [Fact]
    public void AppStoragePaths_override_isolates_root()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xt-appdata-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppStoragePaths.SetRootOverride(dir);
            Assert.Equal(Path.GetFullPath(dir), AppStoragePaths.Root);
            Assert.StartsWith(AppStoragePaths.Root, AppStoragePaths.Settings, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(AppStoragePaths.Root, AppStoragePaths.KnownHosts, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            AppStoragePaths.SetRootOverride(null);
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void LiveIntegrationGate_describe_without_enable()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("XTERMINAL_IT_ENABLE"), "1", StringComparison.Ordinal))
            return;
        Assert.Contains("disabled", LiveIntegrationGate.DescribeConfiguration(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Workspace_validator_rejects_secret_like_payloads()
    {
        Assert.True(WorkspaceLayoutValidator.ContainsSecrets("""{"x":"BEGIN RSA PRIVATE KEY"}"""));
        Assert.False(WorkspaceLayoutValidator.ContainsSecrets("""{"tabs":[{"kind":"terminal"}]}"""));
    }
}
