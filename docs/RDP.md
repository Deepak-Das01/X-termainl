# RDP remote desktop

Phase 4B embeds **Microsoft MsRdpClient9** ActiveX via `RdpActiveXHost` (`AxHost` + `WindowsFormsHost`). Phase 4C documents verification limits and keeps secure defaults.

## Security defaults

| Setting | Default |
|---------|---------|
| Require NLA / CredSSP support flag | On |
| AuthenticationLevel | 2 (require) |
| Clipboard / drives / printers / audio redirection | Off |
| Credentials in JSON | Never — Credential Manager only |

Setting `EnableCredSspSupport` alone is **not** equated with full NLA/certificate policy enforcement. Certificate failure UX depends on the installed `mstscax` control; X-Terminal does not automatically accept certificate failures.

## Runtime

- CLSID `301B94BA-5D25-4A12-BFFE-3B6E7A616585` (MsRdpClient9)
- Absence of ActiveX produces a safe actionable failure
- Ctrl+Alt+Del is attempted only through the ActiveX `SendKeys` API when present — **never** injected into the local OS

## Lifecycle / UX

- Control must be sited before `Connect`
- WPF airspace: native HWND can obscure WPF overlays (command palette may appear behind RDP)
- No dynamic display resizing API is claimed without evidence
- Workspace restore leaves RDP tabs disconnected

## Live validation

Authorized RDP endpoint testing is optional and not assumed in CI.
