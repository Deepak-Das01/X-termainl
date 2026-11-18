# Remote desktop architecture

## Abstraction

- `IGraphicalDesktopSession` — implemented by `VncGraphicalSession`
- RDP uses `DesktopTabViewModel` + `RdpActiveXHost` (COM), not the terminal host pipeline
- `SessionHostFactory` stubs for RDP/VNC/X11 prevent fake terminal sessions

## Capability gating

RDP/VNC expose `GraphicalDesktop` (+ `PlaintextNetwork` for VNC). They do **not** expose terminal broadcast, shell history, snippets, SFTP, split-pane terminal controls, or terminal search as functional features.

## VNC transport

```
[Optional SSH profile] --local forward 127.0.0.1:ephemeral--> [remote VNC host:port]
                              |
                         VncRfbClient
```

## X11

X11 is an SSH capability, not a standalone graphical tab. Forwarding remains gated (ADR-004C). External X server windows are separate OS windows — not embedded in X-Terminal.

## Persistence

Desktop profiles store non-secret fields in `sessions.json`. Passwords use Credential Manager. Workspace layout restores tabs disconnected-by-default.
