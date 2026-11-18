# Remote desktop architecture (Phase 4B ADR)

## Decision

| Protocol | Approach | Rationale |
|----------|----------|-----------|
| **RDP** | Embedded Microsoft Remote Desktop ActiveX (`MsRdpClient9`, CLSID `301B94BA-5D25-4A12-BFFE-3B6E7A616585`) hosted via `System.Windows.Forms.AxHost` inside `WindowsFormsHost` | Ships with Windows; no third-party RDP stack; no checked-in tlbimp binaries; CI-compatible on `windows-latest` |
| **VNC** | First-party bounded RFB 3.3/3.7/3.8 client in `XTerminal.Protocols` | Avoids untrusted/under-maintained NuGet RFB packages; full control of security policy, allocations, and loopback testing |

## Rejected alternatives

- **Launching `mstsc.exe` / external VNC viewers** — not embedded remote desktop; would misrepresent product capability.
- **Window reparenting / screen scraping** — unsupported and fragile.
- **Devolutions.MsRdpEx / community VNC NuGets as hard deps** — deferred; may revisit after license/redistribution review. ActiveX + first-party RFB meet Phase 4B goals without bundling proprietary extras.
- **Writing a full RDP stack** — not maintainable; OS control is authoritative.

## Licensing

- **MsTscAx / mstscax.dll** — Windows component; activated in-process; not redistributed by X-Terminal.
- **First-party RFB client** — project license; no third-party RFB dependency.

## Graphical vs terminal sessions

Graphical sessions use `IGraphicalDesktopSession` and `DesktopTabViewModel`. They do **not** push bitmaps through `ISessionHost.OutputReceived`. Terminal groups, editor tabs, and desktop tabs are distinct `IWorkspaceTab` kinds.

## Security defaults

- RDP: CredSSP/NLA enabled; authentication level require; redirections off; credentials in Credential Manager only.
- VNC: VNC Auth preferred; unencrypted/none requires explicit acknowledgment; clipboard off; no unauthenticated default.

## Limitations

- WPF airspace: native RDP HWND may occlude WPF overlays (palette uses dimmed overlay; document known limitation).
- No graphical split panes in 4B.
- VNC encodings: Raw + CopyRect (documented); ZRLE/Tight not claimed.
- Live RDP/VNC against private servers: opt-in only when authorized endpoints exist.
