# VNC remote desktop

Phase 4B ships a **first-party RFB 3.3/3.7/3.8 client** (`VncRfbClient`) with WPF `WriteableBitmap` rendering.

## Security

- Classic **VNC Auth proves a password; it does not encrypt** framebuffer or input.
- Unauthenticated (`None`) requires explicit `AllowUnencrypted` + per-host acknowledgment.
- Clipboard sharing is **off** by default.
- Credentials use `DesktopSecretKeys.VncPassword` (isolated from SSH keys).

Prefer SSH local port forwards (loopback listeners) for legacy servers when appropriate.

## Supported encodings

| Encoding | Status |
|----------|--------|
| Raw | Yes |
| CopyRect | Yes |
| Hextile / ZRLE / Tight | Not implemented (not claimed) |

## Limits

- Max framebuffer 8192×8192; bounded message sizes; invalid rectangles rejected.

## Loopback tests

`Category=VncLoopback` runs against an ephemeral `127.0.0.1` RFB fixture exercising the production client. Loopback success does **not** prove compatibility with every real VNC server.

## Live servers

Do not install TigerVNC/xrdp without approval. Live VNC validation is **not executed** unless an authorized endpoint is explicitly configured.
