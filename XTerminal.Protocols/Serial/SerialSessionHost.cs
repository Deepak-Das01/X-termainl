using System.IO.Ports;
using System.Text;
using XTerminal.Core.Models;
using XTerminal.Core.Models.Serial;
using XTerminal.Core.Services;

namespace XTerminal.Protocols.Serial;

public sealed class SerialSessionHost : ISessionHost
{
    private readonly object _gate = new();
    private SerialPort? _port;
    private SessionDefinition? _definition;
    private SerialSessionProfile? _profile;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private int _disposed;
    private int _intentionalClose;
    private Encoding _encoding = Encoding.UTF8;
    private Decoder _decoder = Encoding.UTF8.GetDecoder();

    public SessionKind Kind => SessionKind.Serial;
    public string Title => _definition?.Name ?? "Serial";
    public bool IsConnected => _port?.IsOpen == true;
    public bool IsImplemented => true;
    public string? StatusMessage { get; private set; }
    public ConnectionState State { get; private set; } = ConnectionState.Idle;

    public event Action<byte[]>? OutputReceived;
    public event Action? Closed;
    public event Action<ConnectionState, string?>? StateChanged;

    public bool DtrEnable
    {
        get => _port?.DtrEnable ?? _profile?.DtrEnable ?? true;
        set
        {
            if (_port is { IsOpen: true })
                _port.DtrEnable = value;
        }
    }

    public bool RtsEnable
    {
        get => _port?.RtsEnable ?? _profile?.RtsEnable ?? true;
        set
        {
            if (_port?.Handshake is Handshake.RequestToSend or Handshake.RequestToSendXOnXOff)
                return; // RTS owned by handshake
            if (_port is { IsOpen: true })
                _port.RtsEnable = value;
        }
    }

    public async Task ConnectAsync(SessionDefinition definition, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        _definition = definition;
        var profile = definition.ToSerialProfile();
        if (!SerialProfileValidator.TryValidate(profile, out var error))
            throw new InvalidOperationException(error);

        await DisconnectCoreAsync().ConfigureAwait(false);
        Interlocked.Exchange(ref _intentionalClose, 0);
        SetState(ConnectionState.Connecting, $"Opening {profile.PortName}…");

        if (!SerialPortOwnership.TryAcquire(profile.PortName, definition.Id, out _))
        {
            SetState(ConnectionState.Failed, "COM port is already open in another X-Terminal session.");
            throw new InvalidOperationException(
                $"Port {profile.PortName} is already open in another X-Terminal session.");
        }

        SerialPort? port = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _encoding = Encoding.GetEncoding(profile.EncodingName);
            _decoder = _encoding.GetDecoder();
            port = CreatePort(profile);
            await Task.Run(() => port.Open(), cancellationToken).ConfigureAwait(false);
            port.DtrEnable = profile.DtrEnable;
            if (port.Handshake is not (Handshake.RequestToSend or Handshake.RequestToSendXOnXOff))
                port.RtsEnable = profile.RtsEnable;

            _port = port;
            _profile = profile;
            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoop(_readCts.Token));
            SetState(ConnectionState.Connected, $"Connected · {profile.PortName} @ {profile.BaudRate}");
        }
        catch (OperationCanceledException)
        {
            SerialPortOwnership.Release(profile.PortName, definition.Id);
            SafeClose(port);
            SetState(ConnectionState.Cancelled, "Serial connect cancelled");
            throw;
        }
        catch (Exception ex)
        {
            SerialPortOwnership.Release(profile.PortName, definition.Id);
            SafeClose(port);
            _port = null;
            var msg = ClassifyOpenError(ex, profile.PortName);
            SetState(ConnectionState.Failed, msg);
            throw new InvalidOperationException(msg, ex);
        }
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_definition is null) return;
        SetState(ConnectionState.Reconnecting, "Reopening serial port…");
        await ConnectAsync(_definition, cancellationToken).ConfigureAwait(false);
    }

    public void CancelConnect()
    {
        try { _readCts?.Cancel(); } catch { /* ignore */ }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        var port = _port;
        if (port is not { IsOpen: true } || data.Length == 0)
            return;

        var bytes = data.ToArray();
        try
        {
            port.Write(bytes, 0, bytes.Length);
            if (_profile?.LocalEcho == true)
                OutputReceived?.Invoke(bytes);
        }
        catch (Exception ex)
        {
            SetState(ConnectionState.Failed, "Serial write failed: " + Sanitize(ex.Message));
        }
    }

    public void Resize(short columns, short rows)
    {
        // Serial devices do not receive NAWS; local display-only.
    }

    public void SendBreak(int durationMs = 100)
    {
        var port = _port;
        if (port is not { IsOpen: true }) return;
        durationMs = Math.Clamp(durationMs, 10, 3000);
        try { port.BreakState = true; } catch { return; }
        try
        {
            Thread.Sleep(durationMs);
        }
        finally
        {
            try { port.BreakState = false; } catch { /* ignore */ }
        }
    }

    public bool TryWriteHex(string hex, out string error)
    {
        error = "";
        if (!TryParseHex(hex, out var bytes, out error))
            return false;
        Write(bytes);
        return true;
    }

    public static bool TryParseHex(string hex, out byte[] bytes, out string error)
    {
        bytes = Array.Empty<byte>();
        error = "";
        if (string.IsNullOrWhiteSpace(hex))
        {
            error = "Hex payload is empty.";
            return false;
        }

        var compact = new string(hex.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not ':' and not ',').ToArray());
        if (compact.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            compact = compact[2..];
        if (compact.Length % 2 != 0)
        {
            error = "Hex payload must contain an even number of digits.";
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(compact);
            return true;
        }
        catch
        {
            error = "Hex payload contains invalid digits.";
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await DisconnectCoreAsync().ConfigureAwait(false);
        SetState(ConnectionState.Disconnected, "Disposed");
        Closed?.Invoke();
    }

    private void ReadLoop(CancellationToken token)
    {
        var buffer = new byte[4096];
        var port = _port;
        if (port is null) return;

        try
        {
            while (!token.IsCancellationRequested && port.IsOpen)
            {
                int read;
                try
                {
                    // BaseStream.ReadAsync may still block at the driver; closing the port unblocks it.
                    read = port.BaseStream.Read(buffer, 0, buffer.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                if (read <= 0)
                {
                    Thread.Sleep(10);
                    continue;
                }

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                OutputReceived?.Invoke(chunk);
            }
        }
        finally
        {
            if (Volatile.Read(ref _intentionalClose) == 0 &&
                State is ConnectionState.Connected or ConnectionState.Reconnecting)
            {
                SetState(ConnectionState.Disconnected, "Serial port closed");
                Closed?.Invoke();
            }
        }
    }

    private async Task DisconnectCoreAsync()
    {
        Interlocked.Exchange(ref _intentionalClose, 1);
        try { _readCts?.Cancel(); } catch { /* ignore */ }

        var port = _port;
        _port = null;
        if (port is not null)
        {
            try
            {
                // Closing unblocks a stuck Read on Windows.
                if (port.IsOpen)
                    port.Close();
            }
            catch { /* ignore */ }

            try { port.Dispose(); } catch { /* ignore */ }
        }

        if (_readTask is not null)
        {
            try { await _readTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch { /* ignore */ }
            _readTask = null;
        }

        if (_definition is not null && !string.IsNullOrWhiteSpace(_definition.SerialPort))
            SerialPortOwnership.Release(_definition.SerialPort!, _definition.Id);

        _profile = null;
    }

    private static SerialPort CreatePort(SerialSessionProfile profile)
    {
        var port = new SerialPort(profile.PortName, profile.BaudRate, MapParity(profile.Parity), profile.DataBits, MapStopBits(profile.StopBits))
        {
            Handshake = MapHandshake(profile.FlowControl),
            ReadTimeout = profile.ReadTimeoutMs <= 0 ? SerialPort.InfiniteTimeout : profile.ReadTimeoutMs,
            WriteTimeout = profile.WriteTimeoutMs <= 0 ? 5000 : profile.WriteTimeoutMs,
            Encoding = Encoding.GetEncoding(profile.EncodingName)
        };
        return port;
    }

    private static Parity MapParity(SerialParityMode m) => m switch
    {
        SerialParityMode.Odd => Parity.Odd,
        SerialParityMode.Even => Parity.Even,
        SerialParityMode.Mark => Parity.Mark,
        SerialParityMode.Space => Parity.Space,
        _ => Parity.None
    };

    private static StopBits MapStopBits(SerialStopBitsMode m) => m switch
    {
        SerialStopBitsMode.Two => StopBits.Two,
        SerialStopBitsMode.OnePointFive => StopBits.OnePointFive,
        _ => StopBits.One
    };

    private static Handshake MapHandshake(SerialFlowControl f) => f switch
    {
        SerialFlowControl.RtsCts => Handshake.RequestToSend,
        SerialFlowControl.XonXoff => Handshake.XOnXOff,
        SerialFlowControl.RtsCtsAndXonXoff => Handshake.RequestToSendXOnXOff,
        _ => Handshake.None
    };

    private static void SafeClose(SerialPort? port)
    {
        if (port is null) return;
        try { if (port.IsOpen) port.Close(); } catch { /* ignore */ }
        try { port.Dispose(); } catch { /* ignore */ }
    }

    private static string ClassifyOpenError(Exception ex, string port)
    {
        var msg = Sanitize(ex.Message);
        if (msg.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("access", StringComparison.OrdinalIgnoreCase))
            return $"Access denied opening {port}.";
        if (msg.Contains("not exist", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
            return $"Port {port} is not available.";
        if (msg.Contains("in use", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("being used", StringComparison.OrdinalIgnoreCase))
            return $"Port {port} is already in use.";
        return $"Failed to open {port}: {msg}";
    }

    private static string Sanitize(string message) =>
        string.IsNullOrEmpty(message) ? "Unknown error" : message.Length > 200 ? message[..200] + "…" : message;

    private void SetState(ConnectionState state, string? message)
    {
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke(state, message);
    }
}
