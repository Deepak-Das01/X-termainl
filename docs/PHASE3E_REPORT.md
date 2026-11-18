# Phase 3E / 3E.1 — Live integration validation and stability hardening

Sanitized milestone report. No credentials, private hostnames, process identifiers, or absolute developer paths.

## Executive summary

Phase 3E hardens opt-in live integration infrastructure, adds isolated AppData profiles (`XTERMINAL_APPDATA` / `--appdata`), ConPTY connect lifecycle smoke, CI filters that exclude private-server and flaky interactive ConPTY I/O categories, and documentation (validation matrix, known limitations, secure runner script).

**Phase 3E.1** corrects defective live-gate/probe assertions and implements real authenticated SSH/SFTP/shell/forward/diagnostics tests against production protocol services. Live execution still requires the developer to run `tools/run-live-integration.ps1` interactively (password never stored in repo files).

## Baseline

- Prior checkpoint: Phase 3E on `main` (`13afa37`)
- Default automated suite (filtered): **119** tests (preserves prior coverage + gate/probe unit additions)
- GitHub Actions excludes `LiveIntegration` and `LocalConPtyInteractive`

## Task status

| Task | Status |
|------|--------|
| Baseline / harness / isolated AppData | Completed and validated |
| Live gate refactor (process opt-in, fingerprint, no secret JSON) | Completed and validated (unit) |
| Endpoint probe requires `FailureKind.None` | Completed and validated (code) |
| Deterministic DNS/refused/timeout/network classification | Completed and validated (unit) |
| Authenticated SSH / command / shell / host-key reject | Implemented (Category=LiveIntegration) |
| SFTP lifecycle via `SftpSessionService` | Implemented (Category=LiveIntegration) |
| Loopback port forward + diagnostics | Implemented (Category=LiveIntegration) |
| Secure runner (PS 5.1 / 7, env restore, exit code) | Completed |
| GUI audit / workspace live GUI | Not executed |
| Interactive ConPTY echo | Not executed in default suite (category filtered) |

## How to run live testing

1. Ensure `%USERPROFILE%\.xterminal\integration.local.json` has `enable: true`, host, username, and `expectedHostKeyFingerprint` (no password field).
2. Run interactively: `powershell -NoProfile -File tools\run-live-integration.ps1`
3. Enter the password at the secure prompt (not echoed).

## Related docs

- [`INTEGRATION_TESTING.md`](INTEGRATION_TESTING.md)
- [`VALIDATION_MATRIX.md`](VALIDATION_MATRIX.md)
- [`KNOWN_LIMITATIONS.md`](KNOWN_LIMITATIONS.md)
