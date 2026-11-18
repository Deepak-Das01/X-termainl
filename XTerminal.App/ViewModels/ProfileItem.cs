using XTerminal.Core.Models;

namespace XTerminal.App.ViewModels;

public sealed class ProfileItem
{
    public required string Name { get; init; }
    public required string Subtitle { get; init; }
    public required string Glyph { get; init; }
    public required string Accent { get; init; }
    public required ShellKind Shell { get; init; }
    public SessionKind Kind { get; init; } = SessionKind.LocalShell;
}

public static class ShellPresentation
{
    public static string Glyph(ShellKind shell) => shell switch
    {
        ShellKind.Cmd => "▣",
        ShellKind.Wsl => "🐧",
        ShellKind.GitBash => "❯",
        _ => "❯"
    };

    public static string Accent(ShellKind shell) => shell switch
    {
        ShellKind.Cmd => "#F0C000",
        ShellKind.Wsl => "#DD4814",
        ShellKind.GitBash => "#F05032",
        _ => "#5391FE"
    };

    public static string GlyphFor(SessionDefinition definition) =>
        definition.Kind == SessionKind.LocalShell
            ? Glyph(definition.Shell)
            : definition.Kind switch
            {
                SessionKind.Ssh => "🔐",
                SessionKind.Sftp => "📁",
                SessionKind.Telnet => "⚠",
                SessionKind.Rdp => "🖥",
                SessionKind.Vnc => "🖥",
                SessionKind.Serial => "⛓",
                SessionKind.X11 => "✦",
                _ => "•"
            };

    public static string AccentFor(SessionDefinition definition) =>
        definition.Kind == SessionKind.LocalShell
            ? Accent(definition.Shell)
            : definition.Kind switch
            {
                SessionKind.Telnet => "#E8A838",
                SessionKind.Serial => "#8BD18B",
                _ => "#60CDFF"
            };
}
