using XTerminal.Core.Services;

namespace XTerminal.Tests;

/// <summary>
/// Regression: SSH.NET ChangePermissions expects octal-looking decimals (644), not POSIX bitmasks (0o644=420).
/// Passing a bitmask caused live chmod 0644 to land as 0420 (420 interpreted as digits → 0o420=272).
/// </summary>
public class UnixPermissionsSshNetBoundaryTests
{
    [Theory]
    [InlineData("0000", 0, 0, "---------")]
    [InlineData("0600", 384, 600, "rw-------")]   // 0o600 = 384
    [InlineData("0644", 420, 644, "rw-r--r--")]   // 0o644 = 420
    [InlineData("0700", 448, 700, "rwx------")]   // 0o700 = 448
    [InlineData("0755", 493, 755, "rwxr-xr-x")]   // 0o755 = 493
    [InlineData("0777", 511, 777, "rwxrwxrwx")]   // 0o777 = 511
    [InlineData("1777", 1023, 1777, "rwxrwxrwt")] // 0o1777 = 1023
    [InlineData("2755", 1517, 2755, "rwxr-sr-x")] // 0o2755 = 1517
    [InlineData("4755", 2541, 4755, "rwsr-xr-x")] // 0o4755 = 2541
    public void Posix_bitmask_converts_to_sshnet_digit_mode_once(
        string octalLabel, int posixBitmask, int sshNetMode, string symbolic)
    {
        Assert.Equal(posixBitmask, Convert.ToInt32(octalLabel, 8));
        Assert.Equal((short)sshNetMode, UnixPermissionBits.ToSshNetPermissionsMode((short)posixBitmask));
        Assert.Equal((short)posixBitmask, UnixPermissionBits.FromSshNetPermissionsMode((short)sshNetMode));

        var bits = UnixPermissionBits.FromMode((short)posixBitmask);
        Assert.Equal(symbolic, bits.ToSymbolic());
        Assert.Equal((short)posixBitmask, bits.ToMode());
        Assert.Equal(Convert.ToInt32(octalLabel, 8), Convert.ToInt32(bits.ToOctalString(), 8));
    }

    [Fact]
    public void Double_conversion_reproduces_live_0644_failure_mode()
    {
        // Live failure: expected 420 (0o644), observed 272 (0o420).
        var posix644 = Convert.ToInt16("644", 8); // 420
        Assert.Equal(420, posix644);

        // Incorrectly treating the bitmask as SSH.NET digits yields 0420 → 272.
        var wrong = UnixPermissionBits.FromSshNetPermissionsMode(posix644);
        Assert.Equal(272, wrong);
        Assert.Equal(Convert.ToInt16("420", 8), wrong);

        // Correct boundary conversion.
        Assert.Equal(644, UnixPermissionBits.ToSshNetPermissionsMode(posix644));
        Assert.Equal(posix644, UnixPermissionBits.FromSshNetPermissionsMode(644));
    }

    [Theory]
    [InlineData("000")]
    [InlineData("600")]
    [InlineData("644")]
    [InlineData("700")]
    [InlineData("755")]
    [InlineData("777")]
    [InlineData("1777")]
    [InlineData("2755")]
    [InlineData("4755")]
    public void FromOctal_roundtrips_through_sshnet_boundary(string octal)
    {
        var bits = UnixPermissionBits.FromOctal(octal);
        var mode = bits.ToMode();
        var ssh = UnixPermissionBits.ToSshNetPermissionsMode(mode);
        Assert.Equal(mode, UnixPermissionBits.FromSshNetPermissionsMode(ssh));
        Assert.Equal(Convert.ToInt32(octal, 8), mode);
    }
}
