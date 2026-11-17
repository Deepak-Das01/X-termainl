using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using XTerminal.App.ViewModels;
using XTerminal.Core.Settings;

namespace XTerminal.App.Controls;

public partial class EditorHost : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private EditorTabViewModel? _boundTab;
    private bool _ready;
    private bool _initializing;
    private EditorAppearanceSettings? _pendingOptions;
    public event Action? SaveRequested;

    public EditorHost()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureWebViewAsync();
            BindTab(DataContext as EditorTabViewModel);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Editor failed:\n{ex.Message}";
            StatusText.Foreground = System.Windows.Media.Brushes.Salmon;
            StatusText.TextWrapping = TextWrapping.Wrap;
            StatusText.Margin = new Thickness(24);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => UnbindTab();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        BindTab(e.NewValue as EditorTabViewModel);

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
            "WebView2-Editor");
        Directory.CreateDirectory(userData);

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
        await WebView.EnsureCoreWebView2Async(env);
        var core = WebView.CoreWebView2
                   ?? throw new InvalidOperationException("WebView2 failed to initialize.");

        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.WebMessageReceived += OnWebMessageReceived;

        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        core.SetVirtualHostNameToFolderMapping("xt.app", assets, CoreWebView2HostResourceAccessKind.Allow);

        var tcs = new TaskCompletionSource<bool>();
        void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs args)
        {
            core.NavigationCompleted -= OnNav;
            if (args.IsSuccess) tcs.TrySetResult(true);
            else tcs.TrySetException(new InvalidOperationException("Failed to load editor UI."));
        }

        core.NavigationCompleted += OnNav;
        core.Navigate("https://xt.app/editor.html");
        await tcs.Task;
        _ready = true;
        LoadingOverlay.Visibility = Visibility.Collapsed;
        if (_pendingOptions is not null)
        {
            ApplyOptions(_pendingOptions);
            _pendingOptions = null;
        }

        FlushDocument();
    }

    private void BindTab(EditorTabViewModel? tab)
    {
        if (ReferenceEquals(_boundTab, tab))
        {
            return;
        }

        UnbindTab();
        _boundTab = tab;
        if (_boundTab is not null)
        {
            _boundTab.ContentExternallyUpdated += OnExternalUpdate;
        }

        if (_ready)
        {
            FlushDocument();
        }
    }

    private void UnbindTab()
    {
        if (_boundTab is not null)
        {
            _boundTab.ContentExternallyUpdated -= OnExternalUpdate;
        }

        _boundTab = null;
    }

    private void OnExternalUpdate() => FlushDocument();

    private void FlushDocument()
    {
        if (!_ready || _boundTab is null) return;
        Post(new
        {
            type = "open",
            id = _boundTab.Id.ToString("N"),
            value = _boundTab.Content,
            language = _boundTab.LanguageId
        });
        Post(new { type = "focus" });
    }

    public void ApplyOptions(EditorAppearanceSettings settings)
    {
        if (!_ready)
        {
            _pendingOptions = settings;
            return;
        }

        Post(new
        {
            type = "options",
            fontFamily = settings.FontFamily + ", Consolas, monospace",
            fontSize = settings.FontSize,
            lineHeight = settings.LineHeight,
            wordWrap = settings.WordWrap,
            minimap = settings.Minimap,
            tabSize = settings.TabSize,
            insertSpaces = settings.InsertSpaces,
            renderWhitespace = settings.RenderWhitespace
        });
    }

    public void RunCommand(string name, object? arg = null) =>
        Post(new { type = "command", id = _boundTab?.Id.ToString("N"), name, arg });

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;
            switch (typeProp.GetString())
            {
                case "ready":
                    FlushDocument();
                    break;
                case "changed" when root.TryGetProperty("value", out var value) &&
                                    root.TryGetProperty("id", out var id) &&
                                    _boundTab is not null &&
                                    id.GetString() == _boundTab.Id.ToString("N"):
                    _boundTab.ApplyHostContent(value.GetString() ?? string.Empty);
                    break;
                case "cursor" when root.TryGetProperty("line", out var line) &&
                                   root.TryGetProperty("column", out var col) &&
                                   _boundTab is not null:
                    _boundTab.SetCursor(line.GetInt32(), col.GetInt32());
                    break;
            }
        }
        catch
        {
            // ignore
        }
    }

    private void Post(object payload)
    {
        if (WebView.CoreWebView2 is null) return;
        WebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private void Find_OnClick(object sender, RoutedEventArgs e) => RunCommand("find");
    private void Replace_OnClick(object sender, RoutedEventArgs e) => RunCommand("replace");
    private void Goto_OnClick(object sender, RoutedEventArgs e) => RunCommand("gotoLine");
    private void Wrap_OnClick(object sender, RoutedEventArgs e) => RunCommand("toggleWordWrap");
    private void Minimap_OnClick(object sender, RoutedEventArgs e) => RunCommand("toggleMinimap");
    private void Save_OnClick(object sender, RoutedEventArgs e) => SaveRequested?.Invoke();
}
