namespace XTerminal.Core.Services;

/// <summary>
/// Unix permission bits for chmod UI. Special bits (setuid/setgid/sticky) are tracked so
/// permission edits do not silently clear them when applying owner/group/other changes.
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
}
