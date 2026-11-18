# Phase 3E / 3E.1 / 3E.2 — Live integration validation and stability hardening

Sanitized milestone report. No credentials, private hostnames, process identifiers, or absolute developer paths.

## Executive summary

Phase 3E hardens opt-in live integration infrastructure. Phase 3E.1 added real authenticated SSH/SFTP live tests. Phase 3E.2 fixes three confirmed live failures from the first developer run (7/10) without weakening security policy.

**Live validation status:** code fixes landed; full 10/10 confirmation requires the developer to re-run `tools/run-live-integration.ps1`.

## Baseline (3E.3)

- Prior checkpoint: Phase 3E.2 (`419277a`)
- Filtered automated suite: preserved / extended (see 3E.3 commit notes)
- Live discovery count: **10**
- GitHub Actions excludes `LiveIntegration` and `LocalConPtyInteractive`

## Task status

| Task | Status |
|------|--------|
| Live gate / probe / auth suite (3E.1) | Completed |
| SFTP / diagnostic privacy (3E.2) | Completed (live-validated for those cases) |
| SSH dispose deadlock / forward teardown hang (3E.3) | Completed (unit; awaiting live rerun) |
| Bounded live waits + selective runner filters (3E.3) | Completed |
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
