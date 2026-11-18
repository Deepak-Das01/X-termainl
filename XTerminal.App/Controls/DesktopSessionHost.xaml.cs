using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XTerminal.App.ViewModels;
using XTerminal.Core.Models;

namespace XTerminal.App.Controls;

public partial class DesktopSessionHost : UserControl
{
    private DesktopTabViewModel? _vm;
    private byte _buttons;
    private bool _rdpAttached;

    public DesktopSessionHost()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += async (_, _) =>
        {
            if (_vm is { ConnectionState: ConnectionState.Idle or ConnectionState.Connecting } &&
                !_vm.IsPlaceholder)
                await EnsureStartedAsync();
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _vm = DataContext as DesktopTabViewModel;
        _rdpAttached = false;
        UpdateSurfaceVisibility();
        if (_vm is not null)
            PlaceholderText.Text = _vm.Status;
    }

    private async Task EnsureStartedAsync()
    {
        if (_vm is null) return;
        try
        {
            if (_vm.ConnectionState is ConnectionState.Idle or ConnectionState.Cancelled or ConnectionState.Failed
                or ConnectionState.Disconnected)
                await _vm.StartAsync();

            UpdateSurfaceVisibility();
            if (_vm.IsRdp)
                AttachRdp();
        }
        catch (Exception ex)
        {
            PlaceholderText.Text = ex.Message;
            Placeholder.Visibility = Visibility.Visible;
        }
    }

    private void AttachRdp()
    {
        if (_vm?.RdpControl is null || _rdpAttached) return;
        RdpHost.Child = _vm.RdpControl;
        RdpHost.Visibility = Visibility.Visible;
        VncScroller.Visibility = Visibility.Collapsed;
        Placeholder.Visibility = Visibility.Collapsed;
        _rdpAttached = true;
        _vm.BeginRdpConnect();
    }

    private void UpdateSurfaceVisibility()
    {
        if (_vm is null) return;
        if (_vm.IsRdp)
        {
            RdpHost.Visibility = Visibility.Visible;
            VncScroller.Visibility = Visibility.Collapsed;
        }
        else
        {
            RdpHost.Visibility = Visibility.Collapsed;
            VncScroller.Visibility = Visibility.Visible;
            VncImage.Source = _vm.VncFrame;
        }

        Placeholder.Visibility = _vm.ConnectionState == ConnectionState.Connected
            ? Visibility.Collapsed
            : Visibility.Visible;
        PlaceholderText.Text = _vm.Status;
    }

    private async void Connect_OnClick(object sender, RoutedEventArgs e)
    {
        await EnsureStartedAsync();
        UpdateSurfaceVisibility();
    }

    private void VncImage_OnMouseMove(object sender, MouseEventArgs e) => ForwardPointer(e, null);
    private void VncImage_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) _buttons |= 1;
        if (e.ChangedButton == MouseButton.Right) _buttons |= 4;
        if (e.ChangedButton == MouseButton.Middle) _buttons |= 2;
        ForwardPointer(e, _buttons);
        VncImage.CaptureMouse();
    }

    private void VncImage_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) _buttons &= unchecked((byte)~1);
        if (e.ChangedButton == MouseButton.Right) _buttons &= unchecked((byte)~4);
        if (e.ChangedButton == MouseButton.Middle) _buttons &= unchecked((byte)~2);
        ForwardPointer(e, _buttons);
        if (_buttons == 0) VncImage.ReleaseMouseCapture();
    }

    private void VncImage_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mask = e.Delta > 0 ? (byte)8 : (byte)16;
        ForwardPointer(e, (byte)(_buttons | mask));
        ForwardPointer(e, _buttons);
    }

    private void ForwardPointer(MouseEventArgs e, byte? buttons)
    {
        if (_vm?.VncFrame is not System.Windows.Media.Imaging.BitmapSource bmp) return;
        var pos = e.GetPosition(VncImage);
        if (VncImage.ActualWidth <= 0 || VncImage.ActualHeight <= 0) return;
        var x = (int)(pos.X / VncImage.ActualWidth * bmp.PixelWidth);
        var y = (int)(pos.Y / VncImage.ActualHeight * bmp.PixelHeight);
        _vm.SendVncPointer(x, y, buttons ?? _buttons);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null || _vm.IsRdp) return;
        // Keep application chords (Ctrl/Alt combos) local when not captured as plain VNC keys.
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
            (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            return;

        var keysym = KeyToKeysym(e.Key);
        if (keysym != 0)
        {
            _vm.SendVncKey(keysym, down: true);
            e.Handled = true;
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (_vm is null || _vm.IsRdp) return;
        var keysym = KeyToKeysym(e.Key);
        if (keysym != 0)
        {
            _vm.SendVncKey(keysym, down: false);
            e.Handled = true;
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (_vm is null || _vm.IsRdp) return;
        // Release common modifiers so remote desktops do not stick after tab switch.
        foreach (var keysym in new uint[] { 0xffe1, 0xffe2, 0xffe3, 0xffe4, 0xffe9, 0xffea })
            _vm.SendVncKey(keysym, down: false);
    }

    private static uint KeyToKeysym(Key key) => key switch
    {
        Key.Back => 0xff08,
        Key.Tab => 0xff09,
        Key.Return => 0xff0d,
        Key.Escape => 0xff1b,
        Key.Left => 0xff51,
        Key.Up => 0xff52,
        Key.Right => 0xff53,
        Key.Down => 0xff54,
        Key.Delete => 0xffff,
        Key.Space => 0x20,
        Key.LeftShift => 0xffe1,
        Key.RightShift => 0xffe2,
        Key.LeftCtrl => 0xffe3,
        Key.RightCtrl => 0xffe4,
        Key.LeftAlt => 0xffe9,
        Key.RightAlt => 0xffea,
        >= Key.A and <= Key.Z => (uint)('a' + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (uint)('0' + (key - Key.D0)),
        _ => 0
    };
}
