using System.Windows;
using XTerminal.Core.Services;

namespace XTerminal.App.Views;

public partial class UnixPermissionEditorWindow : Window
{
    private bool _syncing;

    public UnixPermissionEditorWindow(string path, UnixPermissionBits initial)
    {
        InitializeComponent();
        PathDisplay = path;
        ApplyBits(initial);
        DataContext = this;
        RefreshDerived();
    }

    public string PathDisplay { get; }
    public string SymbolicDisplay { get; private set; } = "---------";
    public string OctalText { get; set; } = "644";

    public bool OwnerRead { get; set; }
    public bool OwnerWrite { get; set; }
    public bool OwnerExecute { get; set; }
    public bool GroupRead { get; set; }
    public bool GroupWrite { get; set; }
    public bool GroupExecute { get; set; }
    public bool OtherRead { get; set; }
    public bool OtherWrite { get; set; }
    public bool OtherExecute { get; set; }

    public UnixPermissionBits ResultBits { get; private set; } = new();

    private void ApplyBits(UnixPermissionBits bits)
    {
        OwnerRead = bits.OwnerRead;
        OwnerWrite = bits.OwnerWrite;
        OwnerExecute = bits.OwnerExecute;
        GroupRead = bits.GroupRead;
        GroupWrite = bits.GroupWrite;
        GroupExecute = bits.GroupExecute;
        OtherRead = bits.OtherRead;
        OtherWrite = bits.OtherWrite;
        OtherExecute = bits.OtherExecute;
    }

    private UnixPermissionBits CurrentBits() => new()
    {
        OwnerRead = OwnerRead,
        OwnerWrite = OwnerWrite,
        OwnerExecute = OwnerExecute,
        GroupRead = GroupRead,
        GroupWrite = GroupWrite,
        GroupExecute = GroupExecute,
        OtherRead = OtherRead,
        OtherWrite = OtherWrite,
        OtherExecute = OtherExecute
    };

    private void RefreshDerived()
    {
        var bits = CurrentBits();
        SymbolicDisplay = bits.ToSymbolic();
        if (!_syncing)
        {
            _syncing = true;
            OctalText = bits.ToOctal().ToString("D3");
            OctalBox.Text = OctalText;
            _syncing = false;
        }

        DataContext = null;
        DataContext = this;
    }

    private void Bit_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        RefreshDerived();
    }

    private void OctalBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        try
        {
            var bits = UnixPermissionBits.FromOctal(OctalBox.Text);
            _syncing = true;
            ApplyBits(bits);
            _syncing = false;
            RefreshDerived();
        }
        catch
        {
            // keep previous until Apply validates
        }
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ResultBits = UnixPermissionBits.FromOctal(OctalBox.Text.Trim());
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Invalid permissions", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
