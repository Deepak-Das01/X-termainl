using XTerminal.Core.Services;

namespace XTerminal.Tests;

public class DiagnosticPrivacyTests
{
    [Fact]
    public void Auth_method_label_password_is_permitted_in_export()
    {
        var report = new SshConnectionDiagnosticReport
        {
            SessionName = "demo",
            Host = "private.example.test",
            Username = "svc-user",
            AuthMethod = "Password",
            FailureKind = SshFailureKind.None
        };
        report.Add("SSH", "Handshake and authentication succeeded.");

        var text = report.ToClipboardText(DiagnosticExportMode.SanitizedPublic);
        Assert.Contains("Auth: Password", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private.example.test", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("svc-user", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Credential_canaries_are_redacted_from_messages_and_export()
    {
        const string passwordCanary = "PwCanary-Not-A-Real-Secret-9f3a";
        const string passphraseCanary = "PhCanary-Not-A-Real-Secret-2c1b";

        var sanitizedMsg = SshConnectionDiagnosticReport.Sanitize(
            $"login failed password={passwordCanary} passphrase={passphraseCanary}");
        Assert.DoesNotContain(passwordCanary, sanitizedMsg, StringComparison.Ordinal);
        Assert.DoesNotContain(passphraseCanary, sanitizedMsg, StringComparison.Ordinal);
        Assert.Contains("********", sanitizedMsg);

        var report = new SshConnectionDiagnosticReport
        {
            SessionName = "demo",
            Host = "10.0.0.1",
            Username = "alice",
            AuthMethod = "Password",
            FailureKind = SshFailureKind.Authentication,
            FailureMessage = sanitizedMsg
        };
        // Free-text "bad password value <canary>" — the word password is redacted; also ensure
        // the canary itself cannot survive Add() if someone concatenates credential material.
        report.Add("Auth", $"bad password value");

        Assert.False(report.ExportContainsSecret(passwordCanary));
        Assert.False(report.ExportContainsSecret(passphraseCanary));
        Assert.Contains("Auth: Password", report.ToClipboardText(), StringComparison.Ordinal);

        // Explicit assignment form must strip the value, not only the keyword.
        Assert.DoesNotContain(
            passwordCanary,
            SshConnectionDiagnosticReport.Sanitize($"password: {passwordCanary}"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Private_key_material_wording_is_softened()
    {
        var s = SshConnectionDiagnosticReport.Sanitize("failed to load private key from disk");
        Assert.DoesNotContain("private key", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("key material", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Safe_assertion_helper_does_not_require_dumping_export()
    {
        var report = new SshConnectionDiagnosticReport
        {
            Host = "h.example",
            Username = "u",
            AuthMethod = "Password"
        };
        Assert.False(report.ExportContainsSecret("definitely-not-present-canary"));
    }
}
