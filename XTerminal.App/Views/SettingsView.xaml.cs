using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XTerminal.App.ViewModels;
using XTerminal.Core.Settings;

namespace XTerminal.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void ThemeSystem_OnClick(object sender, RoutedEventArgs e) => SetTheme(AppThemeMode.System);
    private void ThemeDark_OnClick(object sender, RoutedEventArgs e) => SetTheme(AppThemeMode.Dark);
    private void ThemeLight_OnClick(object sender, RoutedEventArgs e) => SetTheme(AppThemeMode.Light);
    private void MaterialMica_OnClick(object sender, RoutedEventArgs e) => SetMaterial(WindowMaterialMode.Mica);
    private void MaterialAcrylic_OnClick(object sender, RoutedEventArgs e) => SetMaterial(WindowMaterialMode.Acrylic);
    private void MaterialSolid_OnClick(object sender, RoutedEventArgs e) => SetMaterial(WindowMaterialMode.Solid);
    private void CursorBar_OnClick(object sender, RoutedEventArgs e) => SetCursor(CursorStyleMode.Bar);
    private void CursorBlock_OnClick(object sender, RoutedEventArgs e) => SetCursor(CursorStyleMode.Block);
    private void CursorUnderline_OnClick(object sender, RoutedEventArgs e) => SetCursor(CursorStyleMode.Underline);

    private void SettingsField_OnChanged(object sender, RoutedEventArgs e) =>
        Vm?.FlushNestedSettings();

    private void RecordKeybinding_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.BeginKeybindingRecord();

    private void ClearKeybinding_OnClick(object sender, RoutedEventArgs e) =>
        Vm?.ClearSelectedKeybinding();

    private void KeybindingGrid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm?.IsRecordingKeybinding != true) return;
        if (Vm.TryCompleteKeybindingRecord(e.Key, Keyboard.Modifiers))
            e.Handled = true;
    }

    private void SetTheme(AppThemeMode mode)
    {
        if (Vm is not null) Vm.ThemeMode = mode;
    }

    private void SetMaterial(WindowMaterialMode mode)
    {
        if (Vm is not null) Vm.WindowMaterial = mode;
    }

    private void SetCursor(CursorStyleMode mode)
    {
        if (Vm is not null) Vm.CursorStyle = mode;
    }
}
