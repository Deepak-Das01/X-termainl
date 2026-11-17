using CommunityToolkit.Mvvm.ComponentModel;
using XTerminal.Core.Settings;

namespace XTerminal.App.ViewModels;

public partial class SchemeTileViewModel : ObservableObject
{
    public required ColorSchemeDefinition Scheme { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    public string Name => Scheme.Name;
    public string Background => Scheme.Background;
    public string Foreground => Scheme.Foreground;
    public string AccentSample => Scheme.Blue;
    public string GreenSample => Scheme.Green;
    public string RedSample => Scheme.Red;
}
