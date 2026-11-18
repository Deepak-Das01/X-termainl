# Known limitations

## Protocols

- **Serial (Phase 4A):** Implemented. No SFTP/tunnels; no remote resize; COM ports never auto-open on workspace restore; friendly names may be unavailable; presence ≠ free. Physical COM validation outstanding without a loopback pair.
- **Telnet (Phase 4A):** Implemented as plaintext. Not all servers behave identically; many RFC options unsupported; prefer SSH.
- **RDP (Phase 4B/4C):** Embedded MsRdpClient9 ActiveX. WPF airspace may occlude overlays; no graphical split panes; live endpoint validation not executed by default; Ctrl+Alt+Del only via ActiveX when available (never local OS injection).
- **VNC (Phase 4B/4C):** First-party RFB client (Raw/CopyRect). Classic VNC Auth is not transport encryption; TLS/VeNCrypt unsupported; optional SSH tunnel; live external servers not assumed.
- **X11 (Phase 4C):** Preferences + BYO X server manager shipped; **genuine forwarding gated** — SSH.NET has no public `x11-req` API (ADR-004C).
- SSH agent / Pageant: default-key fallback only — not a full agent bridge.
- Independent jump-host credentials remain limited.

## Metrics

- Per-forward byte counters are **N/A** (SSH.NET limitation).
- Shell exit codes and durations appear only when shell integration emits reliable OSC events.

## Workspace restoration

- Restored layout does **not** restore prior process state or terminal buffers.
- Serial hardware is never auto-connected on restore.
- Telnet reconnect on restore requires workspace remote-reconnect **and** profile `AutoReconnect`.
- Remote editor tabs are not auto-reopened without a matching live SFTP session.
- Unsaved document crash recovery is intentionally disabled by default.

## Integration testing

- Live private-server tests are opt-in (`XTERMINAL_IT_ENABLE=1`) and excluded from GitHub Actions.
- File `enable` alone cannot turn live tests on; the secure runner sets process opt-in after validating local config.
- Persistent remote shell-integration hooks are not installed by live tests (session-local OSC only).
- SFTP chmod uses POSIX bitmasks internally; SSH.NET requires a one-time conversion to octal-digit mode at the API boundary.
- Diagnostic clipboard export redacts endpoints by default; auth-method labels are retained.
- SSH shell disconnect bounds read-loop and client teardown so dispose cannot hang indefinitely on SSH.NET ReadAsync.
- Live private-server tests are serialized in one xUnit collection to reduce authorized-host contention.
- Interactive ConPTY echo checks may be environment-sensitive under automation hosts.
- Serial hardware loopback is optional (`Category=SerialLiveIntegration`) and not claimed when ports are unset.
- Telnet loopback tests use `127.0.0.1` only.

## Privacy

- Older published history before Phase 3A may still contain machine-specific wording in docs (not rewritten).
- Telnet acknowledgments are per-host only and store no credentials.
