# Phase 4C report — Secure X11 prep and graphical session hardening

## Executive summary

Phase 4C delivers an honest X11 architecture decision (**gate forwarding**), BYO X server management, SSH X11 preference UI/diagnostics, VNC security hardening, VNC-over-SSH local forwards, and RDP secure-default regression coverage. Genuine SSH X11 forwarding is **not** claimed.

## Baseline

- Prior HEAD: `03453f2` (Phase 4B)
- Filtered suite baseline: **207** passed
- Release build: 0 warnings / 0 errors

## X11 feasibility

| Option | Result |
|--------|--------|
| A SSH.NET public API | Blocked |
| B Replace SSH stack | Deferred |
| C OpenSSH dual backend | Deferred (trust/SFTP lease divergence) |
| D Gate + prepare | **Selected** |

ADR: [`adr/ADR-004C-X11-FORWARDING.md`](adr/ADR-004C-X11-FORWARDING.md).

## Delivered

- `XServerManager`, cookie registry, setup parser, forwarding coordinator
- SSH editor X11 tab + Settings → X11 + command palette actions
- VNC RFB bounds/generation hardening; read-only + focus modifier release
- `VncSshTunnel` via `PortForwardRuntimeService` (loopback-only, no plaintext fallback when tunnel requested)
- RDP defaults tests; CAD only via ActiveX when available
- Docs: X11, VNC, RDP, validation matrix, known limitations, this report

## Automated results (this checkpoint)

- Release build: success, 0 warnings
- Filtered suite: **232** passed / 0 failed  
  (`Category!=LiveIntegration&Category!=LocalConPtyInteractive&Category!=SerialLiveIntegration`)
- X11 unit/protocol-prep: included
- VNC loopback: retained in suite
- SSH/SFTP live: **not re-run in this agent session** — developer should run `tools/run-live-integration.ps1`
- Serial hardware: outstanding
- External RDP/VNC live: blocked (no authorized endpoints assumed)
- X11 live: blocked (backend unavailable)

## Privacy / dependencies

- No new NuGet packages
- SSH.NET remains MIT
- `.gitignore` extended for authority files
- No cookies, hostnames, credentials, or framebuffer captures committed

## Remaining blockers

1. Public SSH X11 API or reviewed dual OpenSSH backend
2. Authorized live RDP/VNC/X11 endpoints
3. Serial hardware loopback
4. Phase 5 DevOps — not started
