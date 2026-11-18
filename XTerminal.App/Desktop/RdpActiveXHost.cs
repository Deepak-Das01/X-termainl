using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace XTerminal.App.Desktop;

/// <summary>
/// Hosts Microsoft RDP ActiveX (MsRdpClient9) without checked-in tlbimp assemblies.
/// Requires Windows mstscax.dll. CLSID is the redistributable MsRdpClient9 control.
/// </summary>
public sealed class RdpActiveXHost : AxHost
{
    public const string MsRdpClient9Clsid = "301B94BA-5D25-4A12-BFFE-3B6E7A616585";

    private object? _ocx;
    private System.Windows.Forms.Timer? _poll;
    private bool _connectRequested;
    private int _lastConnected = -1;

    public event Action? Connected;
    public event Action<int>? Disconnected;
    public event Action<string>? FatalError;

    public RdpActiveXHost() : base(MsRdpClient9Clsid)
    {
    }

    public static bool IsRuntimeAvailable()
    {
        try
        {
            var t = Type.GetTypeFromCLSID(new Guid(MsRdpClient9Clsid));
            return t is not null;
        }
        catch
        {
            return false;
        }
    }

    protected override void AttachInterfaces()
    {
        try
        {
            _ocx = GetOcx();
        }
        catch (Exception ex)
        {
            FatalError?.Invoke("RDP ActiveX unavailable: " + ex.Message);
        }
    }

    public void ApplyAndConnect(
        string server,
        int port,
        string username,
        string? domain,
        string? password,
        int width,
        int height,
        int colorDepth,
        bool smartSizing,
        bool enableCredSsp,
        int authenticationLevel,
        bool redirectClipboard,
        bool redirectDrives,
        bool redirectPrinters,
        bool redirectAudio)
    {
        if (_ocx is null)
            throw new InvalidOperationException("RDP ActiveX control is not initialized.");

        var serverTarget = port is > 0 and not 3389 ? $"{server}:{port}" : server;
        SetProp("Server", serverTarget);
        SetProp("UserName", string.IsNullOrWhiteSpace(domain) ? username : $"{domain}\\{username}");
        SetProp("DesktopWidth", width);
        SetProp("DesktopHeight", height);
        SetProp("ColorDepth", colorDepth);

        // Advanced settings via AdvancedSettings2/7/8/9 where available.
        var advanced = GetProp("AdvancedSettings9") ?? GetProp("AdvancedSettings8") ?? GetProp("AdvancedSettings2");
        if (advanced is not null)
        {
            SetOn(advanced, "EnableCredSspSupport", enableCredSsp);
            SetOn(advanced, "AuthenticationLevel", authenticationLevel);
            SetOn(advanced, "SmartSizing", smartSizing);
            SetOn(advanced, "RedirectClipboard", redirectClipboard);
            SetOn(advanced, "RedirectDrives", redirectDrives);
            SetOn(advanced, "RedirectPrinters", redirectPrinters);
            if (redirectAudio)
                SetOn(advanced, "AudioRedirectionMode", 0);
            else
                SetOn(advanced, "AudioRedirectionMode", 2); // do not play
            SetOn(advanced, "NegotiateSecurityLayer", true);
        }

        // Password via non-scriptable interface property ClearTextPassword.
        if (!string.IsNullOrEmpty(password))
            TrySetClearTextPassword(password);

        _connectRequested = true;
        StartPoll();
        InvokeMethod("Connect");
    }

    public void Disconnect()
    {
        try
        {
            if (_ocx is not null && GetConnectedState() != 0)
                InvokeMethod("Disconnect");
        }
        catch
        {
            // ignore
        }
        finally
        {
            StopPoll();
            _connectRequested = false;
        }
    }

    public bool TrySendCtrlAltDel()
    {
        try
        {
            // IMsRdpClientNonScriptable::SendKeys — prefer SecuredSettings if present.
            var secured = GetProp("SecuredSettings3") ?? GetProp("SecuredSettings2") ?? GetProp("SecuredSettings");
            if (secured is not null)
            {
                // KeyboardHookMode already may capture; use SyncSession/ConnectShare alternative:
                InvokeOn(secured, "SendKeys", null!); // may not exist
            }
        }
        catch
        {
            // fall through
        }

        // Documented approach for ActiveX: put focus and send CAD via non-scriptable when available.
        try
        {
            var ns = _ocx;
            if (ns is null) return false;
            // Many builds expose SendKeys(int numKeys, ref Array keyData, ref Array keyState)
            // Skip unsafe local CAD injection — never send Ctrl+Alt+Del to the local workstation.
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void StartPoll()
    {
        StopPoll();
        _poll = new System.Windows.Forms.Timer { Interval = 250 };
        _poll.Tick += (_, _) => PollConnection();
        _poll.Start();
    }

    private void StopPoll()
    {
        if (_poll is null) return;
        _poll.Stop();
        _poll.Dispose();
        _poll = null;
    }

    private void PollConnection()
    {
        if (_ocx is null) return;
        try
        {
            var state = GetConnectedState();
            if (state == _lastConnected) return;
            var prev = _lastConnected;
            _lastConnected = state;
            if (state == 1 && _connectRequested)
                Connected?.Invoke();
            else if (state == 0 && prev is 1 or 2)
                Disconnected?.Invoke(0);
        }
        catch (Exception ex)
        {
            FatalError?.Invoke(ex.Message);
            StopPoll();
        }
    }

    private int GetConnectedState()
    {
        var v = GetProp("Connected");
        return v switch
        {
            short s => s,
            int i => i,
            _ => Convert.ToInt32(v ?? 0)
        };
    }

    private void TrySetClearTextPassword(string password)
    {
        // IMsTscNonScriptable.ClearTextPassword
        try
        {
            SetProp("ClearTextPassword", password);
            return;
        }
        catch
        {
            // try advanced
        }

        try
        {
            if (_ocx is not null)
                SetOn(_ocx, "ClearTextPassword", password);
        }
        catch
        {
            FatalError?.Invoke("Unable to set RDP password on ActiveX control.");
        }
    }

    private object? GetProp(string name)
    {
        if (_ocx is null) return null;
        try
        {
            return _ocx.GetType().InvokeMember(name, BindingFlags.GetProperty, null, _ocx, null);
        }
        catch
        {
            return null;
        }
    }

    private void SetProp(string name, object value)
    {
        if (_ocx is null) return;
        _ocx.GetType().InvokeMember(name, BindingFlags.SetProperty, null, _ocx, new[] { value });
    }

    private static void SetOn(object target, string name, object value)
    {
        try
        {
            target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, new[] { value });
        }
        catch
        {
            // property may not exist on this control version
        }
    }

    private void InvokeMethod(string name)
    {
        if (_ocx is null) return;
        _ocx.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, _ocx, null);
    }

    private static void InvokeOn(object target, string name, params object[] args)
    {
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { Disconnect(); } catch { /* ignore */ }
            StopPoll();
        }
        base.Dispose(disposing);
    }
}

public static class RdpComAvailability
{
    [DllImport("ole32.dll")]
    private static extern int CLSIDFromString([MarshalAs(UnmanagedType.LPWStr)] string lpsz, out Guid pclsid);

    public static bool IsMsRdpClient9Registered()
    {
        try
        {
            return Type.GetTypeFromCLSID(new Guid(RdpActiveXHost.MsRdpClient9Clsid)) is not null;
        }
        catch
        {
            return false;
        }
    }
}
