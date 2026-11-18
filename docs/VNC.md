# VNC remote desktop

Phase 4B ships a **first-party RFB 3.3/3.7/3.8 client** (`VncRfbClient`) with WPF `WriteableBitmap` rendering. Phase 4C hardens parsing and adds optional SSH tunneling.

## Security

- Classic **VNC Auth proves a password; it does not encrypt** framebuffer or input.
- Unauthenticated (`None`) requires explicit `AllowUnencrypted` + per-host acknowledgment.
- Clipboard sharing is **off** by default (server cut-text is skipped; not synced).
- Credentials use `DesktopSecretKeys.VncPassword` (isolated from SSH keys).
- **Native VeNCrypt / TLS is unsupported** — do not claim TLS VNC.

## VNC over SSH

When **Use SSH local forward** is selected:

1. Choose an explicit SSH profile (central host-key verification).
2. X-Terminal starts an application-owned loopback `ForwardedPortLocal`.
3. The RFB client connects only to `127.0.0.1:<ephemeral>` — no direct fallback to the profile Host.
4. Tunnel ownership is disposed with the graphical session.

SSH encryption protects traffic between SSH endpoints. The remaining path from the SSH server to `TunnelRemoteHost:TunnelRemotePort` may still be local/plaintext on the remote host.

Connecting to `localhost` without an application-owned tunnel is **not** labeled as tunneled.

## Supported encodings

| Encoding | Status |
|----------|--------|
| Raw | Yes |
| CopyRect | Yes |
| Hextile / ZRLE / Tight | Not implemented (not claimed) |

## Limits (Phase 4C)

- Max framebuffer 8192×8192
- Per-rectangle pixel/payload caps aligned with `MaxMessageBytes`
- Checked arithmetic for rectangle placement
- Generation stamps discard stale frames after reconnect
- Read-only mode suppresses keyboard/pointer packets
- Focus loss releases modifier keysyms

## Loopback tests

`Category=VncLoopback` runs against an ephemeral `127.0.0.1` RFB fixture. Loopback success does **not** prove compatibility with every real VNC server.

## Live servers

Do not install TigerVNC/xrdp without approval. Live VNC validation is **not executed** unless an authorized endpoint is explicitly configured.
