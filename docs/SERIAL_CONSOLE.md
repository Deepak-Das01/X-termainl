# Serial console

X-Terminal Phase 4A adds a first-class **Serial** session host using `System.IO.Ports` on Windows.

## Architecture

- `SerialSessionProfile` / `SessionDefinition` fields hold validated line settings (no secrets).
- `SerialProfileValidator` rejects invalid baud, data bits, stop bits, flow control, and encodings — it does **not** silently substitute defaults for invalid values.
- `WindowsSerialPortEnumerator` lists COM ports via `SerialPort.GetPortNames()` **without opening** ports.
- `SerialPortOwnership` enforces exclusive in-process ownership of a COM port.
- `SerialSessionHost` implements `ISessionHost` and reuses the existing xterm.js terminal pipeline.

## Configuration

| Setting | Notes |
|---------|--------|
| COM port | `COM1`…`COM256` shape |
| Baud rate | Common rates (9600–230400+) and other device-supported values in range |
| Data bits | 5–8 |
| Parity | None, Odd, Even, Mark, Space |
| Stop bits | 1, 1.5, 2 |
| Flow control | None, RTS/CTS, XON/XOFF, combined |
| DTR / RTS | Applied on open; toggling may reset some devices |
| Encoding | e.g. utf-8, ascii |
| Line ending | CR, LF, CRLF (input mapping preference) |
| Local echo | **Off by default** |

## Capabilities

Supported: interactive I/O, reconnect (manual), diagnostics, binary/hex send, BREAK, DTR/RTS.

**Not supported:** SFTP, SSH tunnels, remote terminal resize (NAWS). Local display zoom/resize still works.

## Ownership and disposal

- Only one X-Terminal session may own a given COM port at a time.
- Closing the port unblocks a stuck Windows serial read.
- Dispose is idempotent; ownership is released on disconnect/dispose.

## Workspace restoration

Serial tabs restore as **disconnected placeholders**. Hardware ports are **never** auto-opened on workspace restore.

## Optional hardware loopback testing

Set both environment variables to a virtual or physical COM pair:

```powershell
$env:XTERMINAL_SERIAL_PORT_A = "COMx"
$env:XTERMINAL_SERIAL_PORT_B = "COMy"
dotnet test XTerminal.sln -c Release --filter "Category=SerialLiveIntegration"
```

Do **not** install virtual COM drivers automatically. If ports are unset, the live serial suite is **not executed** (excluded from the standard regression filter).

## Limitations

- Friendly USB device names are best-effort / may be unavailable.
- Presence in the port list does not prove the port is free.
- Serial binary traffic is not recorded in command history.
- Resize affects local display only.
