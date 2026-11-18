# Phase 3E / 3E.1 / 3E.2 — Live integration validation and stability hardening

Sanitized milestone report. No credentials, private hostnames, process identifiers, or absolute developer paths.

## Executive summary

Phase 3E hardens opt-in live integration infrastructure. Phase 3E.1 added real authenticated SSH/SFTP live tests. Phase 3E.2 fixes three confirmed live failures from the first developer run (7/10) without weakening security policy.

**Live validation status:** code fixes landed; full 10/10 confirmation requires the developer to re-run `tools/run-live-integration.ps1`.

## Baseline (3E.2)

- Prior checkpoint: Phase 3E.1 (`5acf342`)
- Filtered automated suite: **147** passed (Release)
- Live discovery count: **10**
- GitHub Actions excludes `LiveIntegration` and `LocalConPtyInteractive`

## Task status

| Task | Status |
|------|--------|
| Live gate / probe / auth suite (3E.1) | Completed |
| SFTP POSIX↔SSH.NET permission boundary (3E.2) | Completed (unit + awaiting live rerun) |
| Session-local OSC live harness (3E.2) | Completed (unit + awaiting live rerun) |
| Diagnostic privacy export (3E.2) | Completed (unit + awaiting live rerun) |
| Developer live 10/10 confirmation | Pending interactive rerun |

## How to run live testing

1. Keep existing `%USERPROFILE%\.xterminal\integration.local.json` (do not recreate).
2. `powershell -NoProfile -File tools\run-live-integration.ps1`
3. Enter password at the secure prompt.

## Related docs

- [`PHASE3E2_REPORT.md`](PHASE3E2_REPORT.md)
- [`INTEGRATION_TESTING.md`](INTEGRATION_TESTING.md)
- [`VALIDATION_MATRIX.md`](VALIDATION_MATRIX.md)
- [`KNOWN_LIMITATIONS.md`](KNOWN_LIMITATIONS.md)
