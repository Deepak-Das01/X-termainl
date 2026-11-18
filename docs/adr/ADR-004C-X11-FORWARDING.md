# ADR-004C — SSH X11 forwarding backend selection

**Status:** Accepted (Phase 4C)  
**Date:** 2026-11-18  
**Decision:** Option D — continue feature gating of genuine X11 forwarding; ship X server management, profile settings, diagnostics, and protocol-prep helpers without advertising live forwarding.

## Context

X-Terminal uses **SSH.NET 2026.0.0** for interactive SSH, SFTP leases, and port forwards. RFC 4254 §6.3 requires:

1. An `x11-req` channel request on the session channel (with protocol name, cookie, screen).
2. Acceptance of authorized inbound `x11` channels only.
3. Temporary cookie verification and replacement with local X authority data before forwarding bytes to the local X server.

## Options evaluated

| Option | Approach | Verdict |
|--------|----------|---------|
| **A** | Native SSH.NET public API | **Blocked.** `SendX11ForwardingRequest` exists only on **internal** `IChannelSession` / `ChannelSession`. `ShellStream` never sends `x11-req`. Public `SshClient` / `ISshClient` expose no X11 API. Incoming `X11ChannelOpenInfo` is also internal. |
| **B** | Replace SSH stack with another maintained library that has first-class X11 | Deferred. Would rewrite host-key policy, SFTP leases, shared connection manager, and live test harness. Out of Phase 4C scope. |
| **C** | Windows OpenSSH-backed adapter for X11-enabled sessions | Feasible later with strict constraints, but **not selected now**. X11 forwarding must share the same SSH connection that owns the interactive shell/`DISPLAY`. A second OpenSSH process cannot attach to an existing SSH.NET session. A full dual-backend session would diverge trust/config (`ProxyCommand`, agent, password CLI risks) and **cannot share SSH.NET SFTP leases**. |
| **D** | Gate forwarding; document blockers; prepare BYO X server + settings | **Selected.** |

## Explicitly rejected

- **Remote TCP port forward pretending to be X11** (StackOverflow pattern `ForwardedPortRemote(6000+n, …)`). This does not send `x11-req`, does not perform cookie spoofing/replacement, and does not set server-side `DISPLAY` via the SSH protocol. Forbidden by product policy.
- Reflection into SSH.NET internals.
- Undocumented SSH.NET forks without review.
- Implementing a full SSH protocol stack.
- Auto-installing Windows X servers or remote `xauth` packages.
- `-ac`, `xhost +`, or LAN-exposed unauthenticated X listeners.

## Consequences

- SSH session editor exposes X11 **preferences** and accurate states (`Disabled`, `BackendUnavailable`, `XServerUnavailable`, …).
- `XServerManager` supports BYO detection/launch with authenticated, loopback-oriented defaults; does not claim forwarding works.
- `SessionHostFactory` X11 stub remains; X11 is an SSH capability, not a fake standalone terminal.
- Phase 5+ may revisit Option C (isolated OpenSSH session backend) or Option A if SSH.NET publishes a supported public X11 API.

## Dependency / license note

- **SSH.NET 2026.0.0** — MIT License (sshnet/SSH.NET). Maintained. No redistribution of proprietary binaries.
- No new SSH client dependency introduced in Phase 4C.
