using XTerminal.Core.Services;

namespace XTerminal.Tests;

/// <summary>Phase 3B follow-up: preserve setuid/setgid/sticky when editing base permissions.</summary>
public class Phase3BFollowUpTests
{
    [Fact]
    public void UnixPermissions_preserve_special_bits_when_applying_base_mode()
    {
        // 4755 = setuid + rwxr-xr-x
        var original = UnixPermissionBits.FromMode(Convert.ToInt16("4755", 8));
        Assert.True(original.SetUid);
        Assert.Equal(755, original.ToOctal());

        var edited = UnixPermissionBits.FromOctal("644");
        var applied = edited.ToModePreservingSpecial(original.ToMode());
        var result = UnixPermissionBits.FromMode(applied);
        Assert.True(result.SetUid);
        Assert.False(result.SetGid);
        Assert.False(result.Sticky);
        Assert.Equal(644, result.ToOctal());
    }

    [Fact]
    public void UnixPermissions_from_mode_reads_setgid_and_sticky()
    {
        var bits = UnixPermissionBits.FromMode(Convert.ToInt16("3777", 8));
        Assert.True(bits.Sticky);
        Assert.True(bits.SetGid);
        Assert.False(bits.SetUid);
        Assert.Contains('t', bits.ToSymbolic());
    }

    [Fact]
    public void UnixPermissions_four_digit_octal_roundtrip()
    {
        var bits = UnixPermissionBits.FromOctal("2755");
        Assert.True(bits.SetGid);
        Assert.Equal(755, bits.ToOctal());
        Assert.Equal(Convert.ToInt16("2755", 8), bits.ToMode());
    }
}
