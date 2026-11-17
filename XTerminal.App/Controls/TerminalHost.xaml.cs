using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using XTerminal.App.ViewModels;
using XTerminal.Core.Settings;

namespace XTerminal.App.Controls;

public partial class TerminalHost : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private TerminalTabViewModel? _boundTab;
    private bool _ready;
    private bool _initializing;
    private readonly Action<string> _outputHandler;

    /// <summary>Raised for each input chunk from the terminal (used for conservative command history).</summary>
    public event Action<TerminalTabViewModel, string>? InputReceived;

    /// <summary>When true, parent handles WriteInput (e.g. broadcast).</summary>
    public bool SuppressAutoWrite { get; set; }

    public TerminalHost()
    {
        InitializeComponent();
        _outputHandler = OnOutputText;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        PreviewMouseLeftButtonDown += (_, _) => FocusTerminal();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureWebViewAsync();
            BindTab(DataContext as TerminalTabViewModel);
            FocusTerminal();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Terminal engine failed:\n{ex.Message}";
            StatusText.Foreground = System.Windows.Media.Brushes.Salmon;
            StatusText.TextAlignment = TextAlignment.Center;
            StatusText.TextWrapping = TextWrapping.Wrap;
            StatusText.Margin = new Thickness(24);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => UnbindTab();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        BindTab(e.NewValue as TerminalTabViewModel);

    private async Task EnsureWebViewAsync()
    {
        if (WebView.CoreWebView2 is not null || _initializing)
        {
            return;
        }

        _initializing = true;

        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTerminal",
            "WebView2-Main");
        Directory.CreateDirectory(userData);

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
        await WebView.EnsureCoreWebView2Async(env);

        var core = WebView.CoreWebView2
                   ?? throw new InvalidOperationException("WebView2 failed to initialize. Install the Evergreen WebView2 Runtime.");

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.WebMessageReceived += OnWebMessageReceived;

        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        if (!Directory.Exists(assets))
        {
            throw new DirectoryNotFoundException($"Assets folder missing: {assets}");
        }

        core.SetVirtualHostNameToFolderMapping(
            "xt.app",
            assets,
            CoreWebView2HostResourceAccessKind.Allow);

        var tcs = new TaskCompletionSource<bool>();
        void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs args)
        {
            core.NavigationCompleted -= OnNav;
            if (args.IsSuccess)
            {
                tcs.TrySetResult(true);
            }
            else
            {
                tcs.TrySetException(new InvalidOperationException($"Failed to load terminal UI (0x{args.WebErrorStatus:X})."));
            }
        }

        core.NavigationCompleted += OnNav;
        core.Navigate("https://xt.app/terminal.html");
        await tcs.Task;

        _ready = true;
        LoadingOverlay.Visibility = Visibility.Collapsed;
        if (_pendingAppearance is not null)
        {
            ApplyAppearance(_pendingAppearance);
            _pendingAppearance = null;
        }

        FlushOutputBinding();
        Post(new { type = "fit" });
        FocusTerminal();
    }

    private void BindTab(TerminalTabViewModel? tab)
    {
        if (ReferenceEquals(_boundTab, tab))
        {
            return;
        }

        UnbindTab();
        _boundTab = tab;
        if (_boundTab is null)
        {
            return;
        }

        if (_ready)
        {
            FlushOutputBinding();
            FocusTerminal();
        }
        else
        {
            _boundTab.AttachOutput(_outputHandler);
        }
    }

    private void UnbindTab()
    {
        if (_boundTab is not null)
        {
            _boundTab.DetachOutput(_outputHandler);
            _boundTab = null;
        }
    }

    private void FlushOutputBinding()
    {
        if (!_ready || _boundTab is null)
        {
            return;
        }

        _boundTab.DetachOutput(_outputHandler);
        Post(new { type = "clear" });
        _boundTab.AttachOutput(_outputHandler);
        Post(new { type = "fit" });
    }

    private void OnOutputText(string text)
    {
        if (!_ready || string.IsNullOrEmpty(text))
        {
            return;
        }

        void Send() => Post(new { type = "output", data = text });

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Send);
            return;
        }

        Send();
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp))
            {
                return;
            }

            switch (typeProp.GetString())
            {
                case "input" when root.TryGetProperty("data", out var data):
                {
                    var text = data.GetString() ?? string.Empty;
                    if (_boundTab is not null)
                    {
                        InputReceived?.Invoke(_boundTab, text);
                        if (!SuppressAutoWrite)
                            _boundTab.WriteInput(text);
                    }

                    break;
                }
                case "resize" when root.TryGetProperty("cols", out var cols) &&
                                   root.TryGetProperty("rows", out var rows):
                    _boundTab?.Resize(cols.GetInt32(), rows.GetInt32());
                    break;
                case "ready":
                    FlushOutputBinding();
                    break;
                case "clipboardCopy" when root.TryGetProperty("text", out var clip):
                    try { Clipboard.SetText(clip.GetString() ?? ""); } catch { /* ignore */ }
                    break;
            }
        }
        catch
        {
            // ignore malformed messages
        }
    }

    private void Post(object payload)
    {
        if (WebView.CoreWebView2 is null)
        {
            return;
        }

        WebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public void RequestFit()
    {
        if (!_ready)
        {
            return;
        }

        void Send()
        {
            Post(new { type = "fit" });
            FocusTerminal();
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Send);
            return;
        }

        Dispatcher.BeginInvoke(Send, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void RunSearch() => Post(new { type = "find" });

    public void Clear() => Post(new { type = "clear" });

    public void SelectAll() => Post(new { type = "selectAll" });

    public void CopySelection() => Post(new { type = "copy" });

    public void PasteText(string? text = null)
    {
        try
        {
            text ??= Clipboard.GetText();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(text)) return;
        Post(new { type = "paste", text });
    }

    public void SetZoomFontSize(double fontSize)
    {
        if (fontSize <= 0) return;
        Post(new { type = "zoom", fontSize });
    }

    public new void Focus() => FocusTerminal();

    public void ApplyAppearance(TerminalAppearanceSettings appearance)
    {
        if (!_ready || appearance is null)
        {
            _pendingAppearance = appearance;
            return;
        }

        var scheme = ColorSchemeCatalog.Get(appearance.SchemeId);
        Post(new
        {
            type = "appearance",
            fontFamily = appearance.FontFamily,
            fontSize = appearance.FontSize,
            lineHeight = appearance.LineHeight,
            letterSpacing = appearance.LetterSpacing,
            cursorBlink = appearance.CursorBlink,
            cursorStyle = appearance.CursorStyle.ToString().ToLowerInvariant(),
            padding = appearance.Padding,
            showScrollbar = appearance.ShowScrollbar,
            scheme = new
            {
                background = scheme.Background,
                foreground = scheme.Foreground,
                cursor = scheme.Cursor,
                selectionBackground = scheme.Selection,
                black = scheme.Black,
                red = scheme.Red,
                green = scheme.Green,
                yellow = scheme.Yellow,
                blue = scheme.Blue,
                magenta = scheme.Magenta,
                cyan = scheme.Cyan,
                white = scheme.White,
                brightBlack = scheme.BrightBlack,
                brightRed = scheme.BrightRed,
                brightGreen = scheme.BrightGreen,
                brightYellow = scheme.BrightYellow,
                brightBlue = scheme.BrightBlue,
                brightMagenta = scheme.BrightMagenta,
                brightCyan = scheme.BrightCyan,
                brightWhite = scheme.BrightWhite
            }
        });
        RequestFit();
    }

    private TerminalAppearanceSettings? _pendingAppearance;

    private void FocusTerminal()
    {
        try
        {
            WebView.Focus();
            Keyboard.Focus(WebView);
            Post(new { type = "focus" });
        }
        catch
        {
            // ignore focus races during init
        }
    }
}
