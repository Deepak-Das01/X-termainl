namespace XTerminal.Core.Services;

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

    public int ToOctal()
    {
        var owner = (OwnerRead ? 4 : 0) | (OwnerWrite ? 2 : 0) | (OwnerExecute ? 1 : 0);
        var group = (GroupRead ? 4 : 0) | (GroupWrite ? 2 : 0) | (GroupExecute ? 1 : 0);
        var other = (OtherRead ? 4 : 0) | (OtherWrite ? 2 : 0) | (OtherExecute ? 1 : 0);
        return owner * 100 + group * 10 + other;
    }

    public short ToMode()
    {
        short mode = 0;
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

    public static UnixPermissionBits FromMode(short mode)
    {
        return new UnixPermissionBits
        {
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
        $"{(OwnerRead ? 'r' : '-')}{(OwnerWrite ? 'w' : '-')}{(OwnerExecute ? 'x' : '-')}" +
        $"{(GroupRead ? 'r' : '-')}{(GroupWrite ? 'w' : '-')}{(GroupExecute ? 'x' : '-')}" +
        $"{(OtherRead ? 'r' : '-')}{(OtherWrite ? 'w' : '-')}{(OtherExecute ? 'x' : '-')}";
}
