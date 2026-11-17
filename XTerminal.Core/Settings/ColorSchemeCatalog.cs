namespace XTerminal.Core.Settings;

public static class ColorSchemeCatalog
{
    public static IReadOnlyList<ColorSchemeDefinition> All { get; } =
    [
        Create("xterminal-dark", "X-Terminal Dark", "#0C0C0C", "#CCCCCC", "#FFFFFF", "#FFFFFF40",
            "#0C0C0C", "#C50F1F", "#13A10E", "#C19C00", "#0037DA", "#881798", "#3A96DD", "#CCCCCC",
            "#767676", "#E74856", "#16C60C", "#F9F1A5", "#3B78FF", "#B4009E", "#61D6D6", "#F2F2F2"),
        Create("windows-classic", "Windows Classic", "#000000", "#C0C0C0", "#FFFFFF", "#FFFFFF40",
            "#000000", "#800000", "#008000", "#808000", "#000080", "#800080", "#008080", "#C0C0C0",
            "#808080", "#FF0000", "#00FF00", "#FFFF00", "#0000FF", "#FF00FF", "#00FFFF", "#FFFFFF"),
        Create("one-dark", "One Dark", "#282C34", "#ABB2BF", "#528BFF", "#3E445280",
            "#282C34", "#E06C75", "#98C379", "#E5C07B", "#61AFEF", "#C678DD", "#56B6C2", "#ABB2BF",
            "#5C6370", "#E06C75", "#98C379", "#E5C07B", "#61AFEF", "#C678DD", "#56B6C2", "#FFFFFF"),
        Create("dracula", "Dracula", "#282A36", "#F8F8F2", "#F8F8F2", "#44475A80",
            "#21222C", "#FF5555", "#50FA7B", "#F1FA8C", "#BD93F9", "#FF79C6", "#8BE9FD", "#F8F8F2",
            "#6272A4", "#FF6E6E", "#69FF94", "#FFFFA5", "#D6ACFF", "#FF92DF", "#A4FFFF", "#FFFFFF"),
        Create("nord", "Nord", "#2E3440", "#D8DEE9", "#D8DEE9", "#434C5E80",
            "#3B4252", "#BF616A", "#A3BE8C", "#EBCB8B", "#81A1C1", "#B48EAD", "#88C0D0", "#E5E9F0",
            "#4C566A", "#BF616A", "#A3BE8C", "#EBCB8B", "#81A1C1", "#B48EAD", "#8FBCBB", "#ECEFF4"),
        Create("solarized-dark", "Solarized Dark", "#002B36", "#839496", "#839496", "#07364280",
            "#073642", "#DC322F", "#859900", "#B58900", "#268BD2", "#D33682", "#2AA198", "#EEE8D5",
            "#002B36", "#CB4B16", "#586E75", "#657B83", "#839496", "#6C71C4", "#93A1A1", "#FDF6E3"),
        Create("solarized-light", "Solarized Light", "#FDF6E3", "#657B83", "#657B83", "#EEE8D580",
            "#073642", "#DC322F", "#859900", "#B58900", "#268BD2", "#D33682", "#2AA198", "#EEE8D5",
            "#002B36", "#CB4B16", "#586E75", "#657B83", "#839496", "#6C71C4", "#93A1A1", "#FDF6E3"),
        Create("monokai", "Monokai", "#272822", "#F8F8F2", "#F8F8F0", "#49483E80",
            "#272822", "#F92672", "#A6E22E", "#F4BF75", "#66D9EF", "#AE81FF", "#A1EFE4", "#F8F8F2",
            "#75715E", "#F92672", "#A6E22E", "#F4BF75", "#66D9EF", "#AE81FF", "#A1EFE4", "#F9F8F5"),
        Create("gruvbox-dark", "Gruvbox Dark", "#282828", "#EBDBB2", "#EBDBB2", "#3C383680",
            "#282828", "#CC241D", "#98971A", "#D79921", "#458588", "#B16286", "#689D6A", "#A89984",
            "#928374", "#FB4934", "#B8BB26", "#FABD2F", "#83A598", "#D3869B", "#8EC07C", "#EBDBB2"),
        Create("tokyo-night", "Tokyo Night", "#1A1B26", "#A9B1D6", "#C0CAF5", "#33467C80",
            "#15161E", "#F7768E", "#9ECE6A", "#E0AF68", "#7AA2F7", "#BB9AF7", "#7DCFFF", "#A9B1D6",
            "#414868", "#F7768E", "#9ECE6A", "#E0AF68", "#7AA2F7", "#BB9AF7", "#7DCFFF", "#C0CAF5")
    ];

    public static ColorSchemeDefinition Get(string? id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? All[0];

    private static ColorSchemeDefinition Create(
        string id, string name, string bg, string fg, string cursor, string selection,
        string black, string red, string green, string yellow, string blue, string magenta, string cyan, string white,
        string bBlack, string bRed, string bGreen, string bYellow, string bBlue, string bMagenta, string bCyan, string bWhite) =>
        new()
        {
            Id = id,
            Name = name,
            Background = bg,
            Foreground = fg,
            Cursor = cursor,
            Selection = selection,
            Black = black,
            Red = red,
            Green = green,
            Yellow = yellow,
            Blue = blue,
            Magenta = magenta,
            Cyan = cyan,
            White = white,
            BrightBlack = bBlack,
            BrightRed = bRed,
            BrightGreen = bGreen,
            BrightYellow = bYellow,
            BrightBlue = bBlue,
            BrightMagenta = bMagenta,
            BrightCyan = bCyan,
            BrightWhite = bWhite
        };
}
