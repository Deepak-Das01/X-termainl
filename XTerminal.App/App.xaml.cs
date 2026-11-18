using System.Windows;
using XTerminal.Core.Services;

namespace XTerminal.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Isolated profile for validation: XTERMINAL_APPDATA or --appdata <path>
        var args = e.Args ?? Array.Empty<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--appdata", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length &&
                !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                AppStoragePaths.SetRootOverride(args[i + 1]);
                break;
            }
        }

        // Env var also honored inside AppStoragePaths.Root
        _ = AppStoragePaths.Root;
        base.OnStartup(e);
    }
}
