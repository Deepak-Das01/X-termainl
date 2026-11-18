# Known limitations

## Protocols

- RDP, VNC, Telnet, Serial, and X11 are **not implemented** (gated).
- SSH agent / Pageant: default-key fallback only — not a full agent bridge.
- Independent jump-host credentials remain limited.

## Metrics

- Per-forward byte counters are **N/A** (SSH.NET limitation).
- Shell exit codes and durations appear only when shell integration emits reliable OSC events.

## Workspace restoration

- Restored layout does **not** restore prior process state or terminal buffers.
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

## Privacy

- Older published history before Phase 3A may still contain machine-specific wording in docs (not rewritten).
