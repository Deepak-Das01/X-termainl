# Phase 3E — Live integration validation and stability hardening

Sanitized milestone report. No credentials, private hostnames, process identifiers, or absolute developer paths.

## Executive summary

Phase 3E hardens opt-in live integration infrastructure, adds isolated AppData profiles (`XTERMINAL_APPDATA` / `--appdata`), ConPTY connect lifecycle smoke, CI filters that exclude private-server and flaky interactive ConPTY I/O categories, and documentation (validation matrix, known limitations, secure runner script).

**Live private-server SSH/SFTP/shell/tunnel/GUI validation remains blocked** until the developer runs `tools/run-live-integration.ps1` with authorized host metadata and an interactive password prompt.

## Baseline

- Prior checkpoint: Phase 3D on `main`
- Default automated suite (filtered): preserves prior coverage + Phase 3E unit additions
- GitHub Actions excludes `LiveIntegration` and `LocalConPtyInteractive`

## Task status

| Task | Status |
|------|--------|
| Baseline / harness / isolated AppData | Completed and validated |
| Live credentialed SSH/SFTP/editor/shell/ports | **Blocked** — credentials not supplied in-session |
| GUI audit (code inspection) | Partially completed |
| Local ConPTY connect/dispose | Completed and validated (unit) |
| Interactive ConPTY echo | Not executed in default suite (category filtered) |
| Workspace / keybinding / broadcast live GUI | Not executed |
| Security review (host-key AppData isolation) | Completed and validated (code) |
| Docs / privacy / CI filter | Completed and validated |

## How to unblock live testing

1. Edit `%USERPROFILE%\.xterminal\integration.local.json` (no password field).
2. Run interactively: `pwsh -NoProfile -File tools\run-live-integration.ps1`
3. Verify host-key fingerprint before trusting connections in the app.

## Related docs

- [`INTEGRATION_TESTING.md`](INTEGRATION_TESTING.md)
- [`VALIDATION_MATRIX.md`](VALIDATION_MATRIX.md)
- [`KNOWN_LIMITATIONS.md`](KNOWN_LIMITATIONS.md)
