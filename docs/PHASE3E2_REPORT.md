# Phase 3E.2 — Resolve live integration failures

Sanitized milestone report. No credentials, private hostnames, fingerprints, or absolute developer paths.

## Initial live outcome (developer machine)

| Metric | Value |
|--------|-------|
| Discovered live tests | 10 |
| Passed | 7 |
| Failed | 3 |
| Approximate duration | ~4.25s |
| Exit code | 1 |

Failed tests:

1. `Live_sftp_lifecycle_upload_download_permissions_cleanup`
2. `Live_shell_success_and_failure_commands_with_session_local_osc`
3. `Live_diagnostics_pipeline_authenticates`

## Confirmed root causes

### 1. SFTP permissions (production defect)

Internal representation is a **POSIX bitmask** (0644 → decimal **420**). SSH.NET `ChangePermissions` / `SetPermissions` expects **octal-looking decimal digits** (644), which it splits with `/1000`, `/100`, `/10`.

Passing bitmask 420 caused SSH.NET to interpret digits 4-2-0 → remote mode **0420** (decimal **272**). That matches the live assertion failure exactly.

**Fix:** convert once at the `SftpSessionService.SetPermissionsAsync` boundary via `UnixPermissionBits.ToSshNetPermissionsMode`.

### 2. Missing shell integration events (test harness defect)

Markers were observed (shell output path works), but session-local OSC events were empty. The live command used `printf '\033]…\007'` in a way that did not reliably emit ESC/BEL on the remote shell, and the test waited on markers that can appear from echo before asserting events.

**Fix:** emit OSC with portable `printf '\x1b]…\x07'` sequences; await Directory and CommandFinished events with bounded TCS timeouts; also emit session-local OSC 133 `D;<code>` for exit status. Still does **not** install persistent shell hooks.

### 3. Diagnostics assertion (test harness defect + privacy hardening)

`Assert.DoesNotContain("password")` matched the legitimate **Auth: Password** method label. Method labels are not secrets.

**Fix:** allow auth-method labels; assert credential canaries are absent; default clipboard export redacts private endpoints (`DiagnosticExportMode.SanitizedPublic`); strengthen `Sanitize` for `password=` / `passphrase=` assignments.

## Production vs harness

| Area | Classification |
|------|----------------|
| SFTP chmod boundary | Production defect |
| OSC live emission/await | Test harness defect |
| Diagnostic “password” assertion | Test harness defect |
| Endpoint redaction in sanitized export | Production privacy hardening |
| Assignment-value sanitization | Production privacy hardening |

## Live validation status

Fixes are **awaiting the developer’s live rerun**:

```powershell
powershell -NoProfile -File tools\run-live-integration.ps1
```

Do **not** treat live validation as complete until that run reports 10/10 (or the then-current discovery count) passed.

## Related docs

- [`INTEGRATION_TESTING.md`](INTEGRATION_TESTING.md)
- [`VALIDATION_MATRIX.md`](VALIDATION_MATRIX.md)
- [`KNOWN_LIMITATIONS.md`](KNOWN_LIMITATIONS.md)
