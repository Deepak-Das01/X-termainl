using CommunityToolkit.Mvvm.ComponentModel;
using XTerminal.Core.Settings;

namespace XTerminal.App.ViewModels;

public partial class ActivityItemViewModel : ObservableObject
{
    public required ActivitySection Section { get; init; }
    public required string Glyph { get; init; }
    public required string Label { get; init; }
    public required string ToolTip { get; init; }
    public bool IsEnabled { get; init; } = true;

    [ObservableProperty]
    private bool _isSelected;
}
