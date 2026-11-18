# X11 architecture (Phase 4C)

## Decision

See [`adr/ADR-004C-X11-FORWARDING.md`](adr/ADR-004C-X11-FORWARDING.md): **Option D — feature gate**.

SSH.NET 2026.0.0 implements `SendX11ForwardingRequest` only on **internal** channel types. `ShellStream` never issues `x11-req`. Public APIs cannot own inbound `x11` channels. Remote TCP forwards are **not** genuine X11.

## Components shipped

| Component | Role |
|-----------|------|
| `X11SessionSettings` | Persisted SSH-profile preferences (no cookies) |
| `X11AppSettings` | Settings → X11 BYO executable path |
| `X11BackendCapability` | Reports forwarding availability (always false in 4C) |
| `X11ForwardingCoordinator` | Policy + cookie registry lifecycle; rejects enablement |
| `X11CookieMaterial` / `X11CookieRegistry` | Session-scoped MIT-MAGIC-COOKIE-1 prep |
| `X11SetupMessageParser` | Bounded setup-header / cookie extraction tests |
| `XServerManager` | BYO detect/launch/stop; never `-ac`; owned processes only |
| SSH editor **X11** tab | Accurate gated status messaging |

## Security properties enforced now

- Default forwarding **disabled**
- Default trust mode **untrusted**
- Trusted mode requires explicit acknowledgment
- No automatic X server install
- No Windows firewall changes
- No LAN-oriented unauthenticated listeners by policy (`-localhost` when launching)
- Authority temp files ACL-restricted and deleted on stop
- Diagnostics never log cookies

## Not claimed

- Live `x11-req` acceptance
- Remote `DISPLAY` establishment via SSH protocol
- Embedded Linux GUI windows inside X-Terminal
- WSLg as a generic Windows X11 server
