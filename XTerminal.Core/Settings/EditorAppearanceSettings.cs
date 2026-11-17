namespace XTerminal.Core.Settings;

public sealed class EditorAppearanceSettings
{
    public string FontFamily { get; set; } = "Cascadia Code";
    public double FontSize { get; set; } = 14;
    public double LineHeight { get; set; } = 1.45;
    public bool WordWrap { get; set; }
    public bool Minimap { get; set; } = true;
    public int TabSize { get; set; } = 4;
    public bool InsertSpaces { get; set; } = true;
    public bool AutoSave { get; set; }
    public int AutoSaveDelayMs { get; set; } = 1500;
    public bool RenderWhitespace { get; set; }
}

public enum EditorDocumentKind
{
    Untitled,
    Local,
    RemoteSftp
}
