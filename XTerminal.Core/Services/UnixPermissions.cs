namespace XTerminal.Core.Services;

/// <summary>
/// Unix permission bits for chmod UI. Special bits (setuid/setgid/sticky) are tracked so
/// permission edits do not silently clear them when applying owner/group/other changes.
/// <para>
/// Internal representation is a POSIX bitmask (e.g. 0644 == 420 decimal).
/// SSH.NET <c>ChangePermissions</c>/<c>SetPermissions</c> expects a different encoding:
/// decimal digits that look like octal (e.g. 644, 4755) — convert only at that API boundary
/// via <see cref="ToSshNetPermissionsMode"/>.
/// </para>
/// </summary>
public sealed class UnixPermissionBits
{
    public bool OwnerRead { get; set; }
    public bool OwnerWrite { get; set; }
    public bool OwnerExecute { get; set; }
    public bool GroupRead { get; set; }
    public bool GroupWrite { get; set; }
    public bool GroupExecute { get; set; }
    public bool OtherRead { get; set; }
    public bool OtherWrite { get; set; }
    public bool OtherExecute { get; set; }

    public bool SetUid { get; set; }
    public bool SetGid { get; set; }
    public bool Sticky { get; set; }

    public int ToOctal()
    {
        var owner = (OwnerRead ? 4 : 0) | (OwnerWrite ? 2 : 0) | (OwnerExecute ? 1 : 0);
        var group = (GroupRead ? 4 : 0) | (GroupWrite ? 2 : 0) | (GroupExecute ? 1 : 0);
        var other = (OtherRead ? 4 : 0) | (OtherWrite ? 2 : 0) | (OtherExecute ? 1 : 0);
        return owner * 100 + group * 10 + other;
    }

    /// <summary>Full mode including special bits (setuid=04000, setgid=02000, sticky=01000).</summary>
    public short ToMode()
    {
        short mode = 0;
        if (SetUid) mode |= 0b100_000_000_000;
        if (SetGid) mode |= 0b010_000_000_000;
        if (Sticky) mode |= 0b001_000_000_000;
        if (OwnerRead) mode |= 0b100_000_000;
        if (OwnerWrite) mode |= 0b010_000_000;
        if (OwnerExecute) mode |= 0b001_000_000;
        if (GroupRead) mode |= 0b000_100_000;
        if (GroupWrite) mode |= 0b000_010_000;
        if (GroupExecute) mode |= 0b000_001_000;
        if (OtherRead) mode |= 0b000_000_100;
        if (OtherWrite) mode |= 0b000_000_010;
        if (OtherExecute) mode |= 0b000_000_001;
        return mode;
    }

    /// <summary>Applies owner/group/other bits while preserving existing special bits from <paramref name="existingMode"/>.</summary>
    public short ToModePreservingSpecial(short existingMode)
    {
        const short SpecialMask = 0b111_000_000_000;
        var special = (short)(existingMode & SpecialMask);
        var baseMode = (short)(ToMode() & ~SpecialMask);
        return (short)(baseMode | special);
    }

    public static UnixPermissionBits FromMode(short mode)
    {
        return new UnixPermissionBits
        {
            SetUid = (mode & 0b100_000_000_000) != 0,
            SetGid = (mode & 0b010_000_000_000) != 0,
            Sticky = (mode & 0b001_000_000_000) != 0,
            OwnerRead = (mode & 0b100_000_000) != 0,
            OwnerWrite = (mode & 0b010_000_000) != 0,
            OwnerExecute = (mode & 0b001_000_000) != 0,
            GroupRead = (mode & 0b000_100_000) != 0,
            GroupWrite = (mode & 0b000_010_000) != 0,
            GroupExecute = (mode & 0b000_001_000) != 0,
            OtherRead = (mode & 0b000_000_100) != 0,
            OtherWrite = (mode & 0b000_000_010) != 0,
            OtherExecute = (mode & 0b000_000_001) != 0
        };
    }

    public static UnixPermissionBits FromOctal(string octal)
    {
        if (string.IsNullOrWhiteSpace(octal))
            throw new ArgumentException("Octal permission is required.");
        octal = octal.Trim();
        if (octal.Length == 4)
        {
            // Optional leading special-digit form 4755
            if (!int.TryParse(octal, out var full) || full is < 0 or > 7777)
                throw new ArgumentException($"Invalid octal permission: {octal}");
            return FromMode((short)Convert.ToInt32(octal, 8));
        }

        if (octal.Length == 4 && octal[0] == '0')
            octal = octal[1..];
        if (octal.Length != 3 || !int.TryParse(octal, out var value) || value is < 0 or > 777)
            throw new ArgumentException($"Invalid octal permission: {octal}");

        var owner = value / 100;
        var group = (value / 10) % 10;
        var other = value % 10;
        return new UnixPermissionBits
        {
            OwnerRead = (owner & 4) != 0,
            OwnerWrite = (owner & 2) != 0,
            OwnerExecute = (owner & 1) != 0,
            GroupRead = (group & 4) != 0,
            GroupWrite = (group & 2) != 0,
            GroupExecute = (group & 1) != 0,
            OtherRead = (other & 4) != 0,
            OtherWrite = (other & 2) != 0,
            OtherExecute = (other & 1) != 0
        };
    }

    public string ToSymbolic() =>
        $"{(OwnerRead ? 'r' : '-')}{(OwnerWrite ? 'w' : '-')}{(OwnerExecute ? (SetUid ? 's' : 'x') : (SetUid ? 'S' : '-'))}" +
        $"{(GroupRead ? 'r' : '-')}{(GroupWrite ? 'w' : '-')}{(GroupExecute ? (SetGid ? 's' : 'x') : (SetGid ? 'S' : '-'))}" +
        $"{(OtherRead ? 'r' : '-')}{(OtherWrite ? 'w' : '-')}{(OtherExecute ? (Sticky ? 't' : 'x') : (Sticky ? 'T' : '-'))}";

    /// <summary>
    /// Human-facing octal digit string including special bits when present (e.g. "644", "4755").
    /// </summary>
    public string ToOctalString()
    {
        var mode = ToMode();
        var special = (mode >> 9) & 7;
        var user = (mode >> 6) & 7;
        var group = (mode >> 3) & 7;
        var other = mode & 7;
        return special == 0
            ? $"{user}{group}{other}"
            : $"{special}{user}{group}{other}";
    }

    /// <summary>
    /// Converts a POSIX permission bitmask to the encoding required by SSH.NET
    /// <c>SftpClient.ChangePermissions</c> / <c>SftpFileAttributes.SetPermissions</c>.
    /// Those APIs interpret the short as decimal digits 0–7 (644, 755, 1777), not as 0o644.
    /// </summary>
    public static short ToSshNetPermissionsMode(short posixBitmask)
    {
        var special = (posixBitmask >> 9) & 7;
        var user = (posixBitmask >> 6) & 7;
        var group = (posixBitmask >> 3) & 7;
        var other = posixBitmask & 7;
        return (short)(special * 1000 + user * 100 + group * 10 + other);
    }

    /// <summary>Inverse of <see cref="ToSshNetPermissionsMode"/> for tests and interop.</summary>
    public static short FromSshNetPermissionsMode(short sshNetMode)
    {
        if (sshNetMode is < 0 or > 7777)
            throw new ArgumentOutOfRangeException(nameof(sshNetMode));
        var special = sshNetMode / 1000;
        var rem = sshNetMode % 1000;
        var user = rem / 100;
        rem %= 100;
        var group = rem / 10;
        var other = rem % 10;
        if (special is > 7 || user is > 7 || group is > 7 || other is > 7)
            throw new ArgumentOutOfRangeException(nameof(sshNetMode), "SSH.NET mode digits must be 0–7.");
        return (short)((special << 9) | (user << 6) | (group << 3) | other);
    }
}
