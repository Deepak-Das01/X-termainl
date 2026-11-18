# X11 forwarding guide (Phase 4C)

## Status

| Aspect | State |
|--------|-------|
| SSH profile X11 preferences | Implemented |
| BYO X server detection / owned launch | Implemented (best-effort; auth file required) |
| Genuine SSH `x11-req` forwarding | **Blocked** — SSH.NET public API gap |
| Live graphical validation | **Blocked** — no forwarding backend |
| Unit / protocol-prep tests | Implemented |

## Why forwarding is gated

SSH.NET 2026.0.0 does not expose a supported public API to:

1. Send `x11-req` on the interactive session channel
2. Accept only authorized inbound `x11` channels
3. Perform temporary-cookie verification and local authority replacement

Simulating X11 with `ForwardedPortRemote` is explicitly rejected.

Details: [`adr/ADR-004C-X11-FORWARDING.md`](adr/ADR-004C-X11-FORWARDING.md).

## Local X server (bring-your-own)

1. Install VcXsrv (or compatible) independently.
2. Set **Settings → X11 → Default X server executable** to the absolute `vcxsrv.exe` path, or rely on well-known install detection.
3. Optionally use **Start application-owned X server** (command palette / Settings). Only processes started by X-Terminal are stopped by X-Terminal.
4. Launch never uses `-ac` or `xhost +`.

## SSH session preferences

In **SSH Session → X11**:

- Enable forwarding (saved intent; runtime remains gated)
- Trusted vs untrusted (trusted requires acknowledgment)
- Display number, timeout, reconnect preference
- Optional per-session executable override

Restoring a workspace **does not** auto-start X servers or trusted forwarding.

## Remote prerequisites (guidance only)

When a backend exists, typical remote needs include `X11Forwarding yes` in `sshd_config` and `xauth` on the account. X-Terminal does **not** modify server configuration or install packages.

## Trusted vs untrusted

Untrusted is the default when forwarding becomes available. Trusted forwarding can expose keystrokes and window contents to remote clients — require explicit acknowledgment; never auto-enable.
