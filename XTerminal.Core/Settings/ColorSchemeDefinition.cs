namespace XTerminal.Core.Settings;

public sealed class ColorSchemeDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Background { get; init; }
    public required string Foreground { get; init; }
    public required string Cursor { get; init; }
    public required string Selection { get; init; }
    public required string Black { get; init; }
    public required string Red { get; init; }
    public required string Green { get; init; }
    public required string Yellow { get; init; }
    public required string Blue { get; init; }
    public required string Magenta { get; init; }
    public required string Cyan { get; init; }
    public required string White { get; init; }
    public required string BrightBlack { get; init; }
    public required string BrightRed { get; init; }
    public required string BrightGreen { get; init; }
    public required string BrightYellow { get; init; }
    public required string BrightBlue { get; init; }
    public required string BrightMagenta { get; init; }
    public required string BrightCyan { get; init; }
    public required string BrightWhite { get; init; }
}

public enum AppThemeMode
{
    System = 0,
    Dark = 1,
    Light = 2
}

public enum WindowMaterialMode
{
    Mica = 0,
    Acrylic = 1,
    Solid = 2
}

public enum CursorStyleMode
{
    Bar = 0,
    Block = 1,
    Underline = 2
}

public enum ActivitySection
{
    Terminal = 0,
    Sessions = 1,
    Sftp = 2,
    Files = 3,
    Editor = 4,
    PortForwarding = 5,
    Snippets = 6,
    History = 7,
    Settings = 8
}
