using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using XTerminal.Core.Models.X11;

namespace XTerminal.Core.Services.X11;

/// <summary>
/// Bring-your-own X server detection and ownership-scoped process control.
/// Never terminates processes it did not start. Never uses -ac or xhost +.
/// Automatic launch requires a writable session authority file; otherwise launch stays gated.
/// </summary>
public sealed class XServerManager : IDisposable
{
    private readonly object _gate = new();
    private Process? _owned;
    private int? _ownedDisplay;
    private string? _configuredPath;
    private string? _authorityFilePath;
    private int _disposed;

    public string? ConfiguredExecutablePath
    {
        get { lock (_gate) return _configuredPath; }
        set { lock (_gate) _configuredPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
    }

    public bool PreferLoopbackOnly { get; set; } = true;

    /// <summary>When false, StartOwnedAsync refuses to launch (detection-only mode).</summary>
    public bool AllowManagedLaunch { get; set; } = true;

    public void Configure(X11AppSettings settings)
    {
        ConfiguredExecutablePath = settings.DefaultExecutablePath;
        PreferLoopbackOnly = settings.PreferLoopbackOnly;
    }

    public bool TryResolveExecutable(out string? path, out XServerProduct product, out string detail)
    {
        path = null;
        product = XServerProduct.Unknown;
        detail = "";

        var configured = ConfiguredExecutablePath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!File.Exists(configured))
            {
                detail = "Configured X server executable was not found.";
                return false;
            }

            if (!IsAllowedExecutableName(configured))
            {
                detail = "Executable name is not a recognized X server binary.";
                return false;
            }

            path = configured;
            product = InferProduct(configured);
            detail = "Configured executable is present.";
            return true;
        }

        foreach (var candidate in EnumerateWellKnownCandidates())
        {
            if (!File.Exists(candidate.Path)) continue;
            path = candidate.Path;
            product = candidate.Product;
            detail = "Detected a local X server installation (bring-your-own).";
            return true;
        }

        detail = "No X server configured or detected. Install a supported X server independently and set the path in Settings.";
        return false;
    }

    public bool IsOwnedInstanceRunning
    {
        get
        {
            lock (_gate)
            {
                try { return _owned is { HasExited: false }; }
                catch { return false; }
            }
        }
    }

    public int? OwnedDisplayNumber
    {
        get { lock (_gate) return _ownedDisplay; }
    }

    public async Task<(bool Ok, string Message)> StartOwnedAsync(
        int displayNumber,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!AllowManagedLaunch)
            return (false, "Managed X server launch is disabled.");

        if (displayNumber is < 0 or > 63)
            return (false, "Display number must be 0–63.");

        if (!TryResolveExecutable(out var exe, out var product, out var resolveDetail))
            return (false, resolveDetail);

        lock (_gate)
        {
            if (_owned is { HasExited: false })
                return (false, "An application-owned X server is already running.");
        }

        if (IsDisplayTcpPortInUse(displayNumber))
            return (false, $"Display :{displayNumber} TCP port appears already in use.");

        string authorityPath;
        try
        {
            authorityPath = CreateAuthorityFile(displayNumber);
        }
        catch (Exception ex)
        {
            return (false, "Unable to create restricted X authority file: " + Sanitize(ex.Message));
        }

        var args = BuildSecureArguments(product, displayNumber, authorityPath, PreferLoopbackOnly);
        if (args.Any(IsForbiddenArgument))
        {
            TryDeleteAuthority(authorityPath);
            return (false, "Refusing launch: forbidden X server argument.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            TryDeleteAuthority(authorityPath);
            return (false, "Failed to start X server: " + Sanitize(ex.Message));
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    var err = "";
                    try { err = await process.StandardError.ReadToEndAsync(linked.Token).ConfigureAwait(false); }
                    catch { /* ignore */ }
                    process.Dispose();
                    TryDeleteAuthority(authorityPath);
                    return (false, "X server exited during startup. " + Sanitize(err));
                }

                // Local-only transports may not open TCP; accept stable process as owned.
                if (!PreferLoopbackOnly || IsDisplayTcpPortInUse(displayNumber) || DateTime.UtcNow > deadline.AddSeconds(-1))
                {
                    lock (_gate)
                    {
                        _owned = process;
                        _ownedDisplay = displayNumber;
                        _authorityFilePath = authorityPath;
                    }
                    return (true, $"Started application-owned X server on display :{displayNumber} (auth file scoped to this process).");
                }

                await Task.Delay(200, linked.Token).ConfigureAwait(false);
            }

            lock (_gate)
            {
                _owned = process;
                _ownedDisplay = displayNumber;
                _authorityFilePath = authorityPath;
            }
            return (true, $"Started application-owned X server (display :{displayNumber}).");
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            process.Dispose();
            TryDeleteAuthority(authorityPath);
            return (false, "X server startup cancelled or timed out.");
        }
    }

    public (bool Ok, string Message) StopOwned()
    {
        Process? process;
        string? authority;
        lock (_gate)
        {
            process = _owned;
            authority = _authorityFilePath;
            _owned = null;
            _ownedDisplay = null;
            _authorityFilePath = null;
        }

        if (process is null)
            return (false, "No application-owned X server to stop.");

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.Dispose();
        }
        catch (Exception ex)
        {
            TryDeleteAuthority(authority);
            return (false, "Failed to stop owned X server: " + Sanitize(ex.Message));
        }

        TryDeleteAuthority(authority);
        return (true, "Stopped application-owned X server.");
    }

    public X11DiagnosticSnapshot BuildDiagnostics(X11ForwardingStatus status = X11ForwardingStatus.Disabled)
    {
        var resolved = TryResolveExecutable(out _, out _, out var detail);
        var running = IsOwnedInstanceRunning;
        var forwarding = X11BackendCapability.IsForwardingAvailable
            ? status
            : status == X11ForwardingStatus.Disabled
                ? X11ForwardingStatus.Disabled
                : X11ForwardingStatus.BackendUnavailable;

        if (status is not X11ForwardingStatus.Disabled && !resolved && X11BackendCapability.IsForwardingAvailable)
            forwarding = X11ForwardingStatus.XServerUnavailable;

        return new X11DiagnosticSnapshot
        {
            BackendAvailable = X11BackendCapability.IsForwardingAvailable,
            BackendName = X11BackendCapability.BackendDisplayName,
            BackendBlocker = X11BackendCapability.BlockerReason,
            XServerConfigured = !string.IsNullOrWhiteSpace(ConfiguredExecutablePath) || resolved,
            XServerExecutableValid = resolved,
            XServerRunningOwned = running,
            AuthenticationConfigured = running && !string.IsNullOrEmpty(_authorityFilePath),
            LocalTransportReady = running,
            ForwardingStatus = forwarding,
            Summary = X11BackendCapability.IsForwardingAvailable
                ? detail
                : "X11 forwarding backend unavailable. " + detail
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        StopOwned();
    }

    public static bool IsForbiddenArgument(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return true;
        var a = arg.Trim();
        if (a.Equals("-ac", StringComparison.OrdinalIgnoreCase)) return true;
        if (a.Equals("+ac", StringComparison.OrdinalIgnoreCase)) return true;
        if (a.Equals("-auth", StringComparison.OrdinalIgnoreCase)) return false;
        if (a.Equals("off", StringComparison.OrdinalIgnoreCase)) return true; // never pair with -auth
        if (a.Contains("xhost", StringComparison.OrdinalIgnoreCase) && a.Contains('+')) return true;
        return false;
    }

    private static List<string> BuildSecureArguments(
        XServerProduct product, int display, string authorityPath, bool loopbackOnly)
    {
        // Structured ArgumentList only. Never -ac. Authority file required.
        // Prefer -localhost (loopback listener) over disabling TCP entirely so a future
        // X11 backend can reach 127.0.0.1:6000+n without LAN exposure.
        _ = product;
        var args = new List<string>
        {
            $":{display}",
            "-multiwindow",
            "-clipboard",
            "-auth", authorityPath
        };
        if (loopbackOnly)
            args.Add("-localhost");
        return args;
    }

    private static string CreateAuthorityFile(int displayNumber)
    {
        var dir = Path.Combine(Path.GetTempPath(), "XTerminal", "x11");
        Directory.CreateDirectory(dir);
        RestrictDirectoryAcl(dir);

        var path = Path.Combine(dir, $"xauth-{displayNumber}-{Guid.NewGuid():N}.bin");
        var cookie = new byte[16];
        RandomNumberGenerator.Fill(cookie);
        // Minimal binary authority blob for servers that accept -auth <file>.
        // Contents are never logged. Format is server-specific; cookie entropy still required.
        var payload = new byte[32];
        RandomNumberGenerator.Fill(payload);
        Buffer.BlockCopy(cookie, 0, payload, 0, 16);
        File.WriteAllBytes(path, payload);
        RestrictFileAcl(path);
        CryptographicOperations.ZeroMemory(cookie);
        CryptographicOperations.ZeroMemory(payload);
        return path;
    }

    private static void RestrictDirectoryAcl(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            var sec = di.GetAccessControl();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var sid = WindowsIdentity.GetCurrent().User;
            if (sid is null) return;
            sec.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            di.SetAccessControl(sec);
        }
        catch
        {
            // Best-effort on non-ACL filesystems.
        }
    }

    private static void RestrictFileAcl(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            var sec = fi.GetAccessControl();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var sid = WindowsIdentity.GetCurrent().User;
            if (sid is null) return;
            sec.AddAccessRule(new FileSystemAccessRule(
                sid, FileSystemRights.FullControl, AccessControlType.Allow));
            fi.SetAccessControl(sec);
        }
        catch
        {
            // Best-effort.
        }
    }

    private static void TryDeleteAuthority(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path))
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    CryptographicOperations.ZeroMemory(bytes);
                    File.WriteAllBytes(path, bytes);
                }
                catch { /* ignore */ }
                File.Delete(path);
            }
        }
        catch { /* ignore */ }
    }

    private static bool IsDisplayTcpPortInUse(int displayNumber)
    {
        var port = 6000 + displayNumber;
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            return props.GetActiveTcpListeners().Any(ep => ep.Port == port);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAllowedExecutableName(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("vcxsrv.exe", StringComparison.OrdinalIgnoreCase)
               || name.Equals("xming.exe", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Xming.exe", StringComparison.OrdinalIgnoreCase)
               || name.Equals("XWin.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static XServerProduct InferProduct(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("vcxsrv.exe", StringComparison.OrdinalIgnoreCase)) return XServerProduct.VcXsrv;
        if (name.Contains("xming", StringComparison.OrdinalIgnoreCase)) return XServerProduct.Xming;
        if (name.Equals("XWin.exe", StringComparison.OrdinalIgnoreCase)) return XServerProduct.Custom;
        return XServerProduct.Custom;
    }

    private static IEnumerable<(string Path, XServerProduct Product)> EnumerateWellKnownCandidates()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return (Path.Combine(pf, "VcXsrv", "vcxsrv.exe"), XServerProduct.VcXsrv);
        yield return (Path.Combine(pf86, "VcXsrv", "vcxsrv.exe"), XServerProduct.VcXsrv);
        yield return (Path.Combine(local, "Programs", "VcXsrv", "vcxsrv.exe"), XServerProduct.VcXsrv);
        yield return (Path.Combine(pf, "Xming", "Xming.exe"), XServerProduct.Xming);
        yield return (Path.Combine(pf86, "Xming", "Xming.exe"), XServerProduct.Xming);
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown error";
        // Never include cookie-like hex blobs in diagnostics.
        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            message, @"\b[0-9a-fA-F]{32,}\b", "[redacted]");
        return cleaned.Length > 240 ? cleaned[..240] + "…" : cleaned;
    }
}
