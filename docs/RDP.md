# RDP remote desktop

Phase 4B embeds the **Microsoft Remote Desktop ActiveX** (`MsRdpClient9`) via `WindowsFormsHost` + `AxHost`.

## Requirements

- Windows with `mstscax.dll` / MsRdpClient9 registered (typical Windows 10/11 client SKUs).
- X-Terminal built with WPF + Windows Forms interop enabled.

## Security defaults

| Setting | Default |
|---------|---------|
| NLA / CredSSP | On |
| Authentication level | 2 (require) |
| Clipboard / drives / printers / audio redirection | Off |
| Passwords | Windows Credential Manager only |

The application does not silently downgrade NLA. Certificate/identity handling follows the native control and Windows policy.

## Capabilities

Supported where the ActiveX control provides them: connect/disconnect, desktop size, color depth, smart sizing, CredSSP, least-privilege redirection toggles.

**Not claimed:** graphical split panes; guaranteed Ctrl+Alt+Del injection (never sent to the local workstation as a substitute); dynamic resolution unless validated on the host control.

## Lifecycle

Closing an X-Terminal RDP tab disconnects the ActiveX session. It does **not** intentionally log off the remote Windows user.

## Testing

- Unit: profile validation, secure defaults, capability gating, runtime availability probe.
- Live RDP against authorized endpoints: **not executed** unless explicitly configured — do not enable Remote Desktop services automatically.
