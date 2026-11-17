using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using XTerminal.Core.Models;

namespace XTerminal.Terminal.ConPty;

public sealed class ConPtySession : IAsyncDisposable
{
    private readonly object _sync = new();
    private IntPtr _pseudoConsole = IntPtr.Zero;
    private IntPtr _attributeList = IntPtr.Zero;
    private IntPtr _ptyInput = IntPtr.Zero;   // ConPTY reads this (we close after create)
    private IntPtr _ptyOutput = IntPtr.Zero;  // ConPTY writes this (we close after create)
    private SafeFileHandle? _inputWrite;
    private SafeFileHandle? _outputRead;
    private IntPtr _processHandle = IntPtr.Zero;
    private IntPtr _threadHandle = IntPtr.Zero;
    private FileStream? _inputStream;
    private FileStream? _outputStream;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private bool _disposed;

    public event Action<byte[]>? OutputReceived;
    public event Action? Exited;

    public int ProcessId { get; private set; }
    public bool IsRunning => _processHandle != IntPtr.Zero && !_disposed;

    public void Start(SessionDefinition session, short columns = 120, short rows = 40)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Pipe A: we write -> ConPTY reads (input)
        if (!NativeMethods.CreatePipe(out _ptyInput, out var inputWrite, IntPtr.Zero, 0))
        {
            throw new InvalidOperationException($"CreatePipe(input) failed: {Marshal.GetLastWin32Error()}");
        }

        // Pipe B: ConPTY writes -> we read (output)
        if (!NativeMethods.CreatePipe(out var outputRead, out _ptyOutput, IntPtr.Zero, 0))
        {
            throw new InvalidOperationException($"CreatePipe(output) failed: {Marshal.GetLastWin32Error()}");
        }

        var size = new NativeMethods.Coord(columns, rows);
        var hr = NativeMethods.CreatePseudoConsole(size, _ptyInput, _ptyOutput, 0, out _pseudoConsole);
        if (hr != 0)
        {
            throw new InvalidOperationException($"CreatePseudoConsole failed: 0x{hr:X8}");
        }

        // ConPTY duplicated these; close our copies.
        NativeMethods.CloseHandle(_ptyInput);
        NativeMethods.CloseHandle(_ptyOutput);
        _ptyInput = IntPtr.Zero;
        _ptyOutput = IntPtr.Zero;

        _inputWrite = new SafeFileHandle(inputWrite, ownsHandle: true);
        _outputRead = new SafeFileHandle(outputRead, ownsHandle: true);

        StartProcess(ShellResolver.ResolveCommand(session), session.WorkingDirectory);

        _inputStream = new FileStream(_inputWrite, FileAccess.Write);
        _outputStream = new FileStream(_outputRead, FileAccess.Read);

        _readCts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token));
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_inputStream is null || _disposed || data.Length == 0)
        {
            return;
        }

        lock (_sync)
        {
            _inputStream.Write(data);
            _inputStream.Flush();
        }
    }

    public void Resize(short columns, short rows)
    {
        if (_pseudoConsole == IntPtr.Zero || columns <= 0 || rows <= 0)
        {
            return;
        }

        NativeMethods.ResizePseudoConsole(_pseudoConsole, new NativeMethods.Coord(columns, rows));
    }

    private void StartProcess(string commandLine, string? workingDirectory)
    {
        var startupInfo = new NativeMethods.StartupInfoEx();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<NativeMethods.StartupInfoEx>();

        var size = IntPtr.Zero;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        _attributeList = Marshal.AllocHGlobal(size);
        if (!NativeMethods.InitializeProcThreadAttributeList(_attributeList, 1, 0, ref size))
        {
            throw new InvalidOperationException($"InitializeProcThreadAttributeList failed: {Marshal.GetLastWin32Error()}");
        }

        startupInfo.lpAttributeList = _attributeList;

        if (!NativeMethods.UpdateProcThreadAttribute(
                _attributeList,
                0,
                (IntPtr)NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                _pseudoConsole,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
        {
            throw new InvalidOperationException($"UpdateProcThreadAttribute failed: {Marshal.GetLastWin32Error()}");
        }

        var cmd = new StringBuilder(commandLine);
        if (!NativeMethods.CreateProcessW(
                null,
                cmd,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                NativeMethods.ExtendedStartupInfoPresent,
                IntPtr.Zero,
                string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
                ref startupInfo,
                out var processInfo))
        {
            throw new InvalidOperationException($"CreateProcess failed for '{commandLine}'. Win32={Marshal.GetLastWin32Error()}");
        }

        _processHandle = processInfo.hProcess;
        _threadHandle = processInfo.hThread;
        ProcessId = processInfo.dwProcessId;

        _ = Task.Run(() =>
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(ProcessId);
                process.WaitForExit();
            }
            catch
            {
                // ignored
            }
            finally
            {
                Exited?.Invoke();
            }
        });
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (!cancellationToken.IsCancellationRequested && _outputStream is not null)
            {
                var read = await _outputStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                OutputReceived?.Invoke(chunk);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _readCts?.Cancel();
            if (_readTask is not null)
            {
                try { await _readTask.ConfigureAwait(false); } catch { /* ignore */ }
            }
        }
        finally
        {
            _readCts?.Dispose();
            _inputStream?.Dispose();
            _outputStream?.Dispose();
            _inputWrite?.Dispose();
            _outputRead?.Dispose();

            if (_ptyInput != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_ptyInput);
                _ptyInput = IntPtr.Zero;
            }

            if (_ptyOutput != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_ptyOutput);
                _ptyOutput = IntPtr.Zero;
            }

            if (_threadHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_threadHandle);
                _threadHandle = IntPtr.Zero;
            }

            if (_processHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_processHandle);
                _processHandle = IntPtr.Zero;
            }

            if (_pseudoConsole != IntPtr.Zero)
            {
                NativeMethods.ClosePseudoConsole(_pseudoConsole);
                _pseudoConsole = IntPtr.Zero;
            }

            if (_attributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(_attributeList);
                Marshal.FreeHGlobal(_attributeList);
                _attributeList = IntPtr.Zero;
            }
        }
    }
}
