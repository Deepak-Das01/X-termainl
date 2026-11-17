# X-Terminal Phase 3A — Tasks 0–3

**Milestone:** Phase 3A  
**Scope:** Baseline validation, advanced SSH connection management, jump/bastion chains, OpenSSH config import  
**Next:** Phase 3B (Tasks 4–6) — not started in this checkpoint  
**Date:** 2026-11-17

---

## 1. Executive summary

Phase 3A strengthens SSH connection management (auth methods, timeouts/keepalive, cancellation, classified failures, status/duration), multi-hop jump hosts with per-hop host-key verification, and partial OpenSSH `~/.ssh/config` import without secrets or ProxyCommand execution.

**Automated:** Release build + **53** Phase 3A gate tests passed on `_phase3a_out` (44 Phase 1–2 preserved + 9 Phase 3A). Full working-tree suite also green at **59** (includes uncommitted 3B+ WIP tests).  
**Manual:** Live SSH / bastion / config-import UX still required (see checklist).  
**Git checkpoint:** `ab546a8` on `main` (Phase 3A scope). Subsequent Phase 3B/3C commits follow on the same branch.

---

## 2. Task 0 — Baseline

| Check | Result |
|-------|--------|
| Git repository | Yes — `main` ↔ `origin/main` |
| Running instance | a running local XTerminal instance (not terminated); default `bin` locked |
| Build strategy | Isolated output `_phase3a_out` |
| Phase 1–2 tests | **44/44** still pass |
| Phase 2 report | Reviewed; no open Phase 2 regressions blocking 3A |
| Security baseline | Host-key policy and Credential Manager secrets unchanged |

---

## 3. Features (Tasks 1–3)

### Task 1 — Advanced SSH connection management

| Capability | Status | Validation |
|------------|--------|------------|
| Password / private key / passphrase | Present | Automated (enum/factory) + **manual** live auth |
| Keyboard-interactive | Implemented (stored password prompts) | **Manual** |
| SSH agent | Documented fallback to `~/.ssh` default keys only (no Pageant) | **Manual** / honest limitation |
| Timeout + keepalive | From session record | **Manual** |
| Cancel connect | `CancelConnect` | **Manual** |
| Classified failures | Dns / Network / Timeout / HostKey / Authentication / Negotiation / Jump / Cancelled | Automated classify tests |
| Status: auth, user@host:port, duration | Session host + terminal tab labels | **Manual** UI |
| Reconnect controls | Existing reconnect path retained | **Manual** |
| Centralized host-key policy | All paths attach policy; never auto-accept | Automated host-key tests (Phase 1) + **manual** prompts |

### Task 2 — Jump hosts / bastion chains

| Capability | Status | Validation |
|------------|--------|------------|
| Single jump | Live tunnel | **Manual** |
| Multi-hop | ProxyJump CSV → tags + live chain; max 5 hops | Automated chain validation; **manual** live multi-hop |
| Loop / invalid chain detection | `JumpHostChain.Validate` before connect | Automated |
| Host-key per hop + target | Attached on every hop client and logical target | **Manual** trust prompts |
| Cleanup on hop failure | Chain dispose on exclusive ownership | **Manual** |
| Connection sharing across jumps | **Not implemented** — exclusive ownership retained | Documented limitation |
| Independent per-hop ProfileId auth | **Partial** — intermediate hops reuse jump credentials | Documented limitation |

### Task 3 — OpenSSH configuration import

| Capability | Status | Validation |
|------------|--------|------------|
| Default path `%USERPROFILE%\.ssh\config` | Supported | Automated path expand |
| Host, HostName, User, Port, IdentityFile | Parsed | Automated |
| ProxyJump (incl. CSV extras as `jump:` tags) | Parsed | Automated |
| ServerAliveInterval, ConnectTimeout | Parsed | Automated |
| Import preview + rename duplicates | UI flow | **Manual** |
| Unsupported directive reporting | Preview lists blocked/unknown | Automated + **manual** |
| ProxyCommand / LocalCommand | **Blocked** — never executed | Automated |
| Secrets / key material | **Never imported** | Automated JSON assert |

---

## 4. Files in Phase 3A checkpoint

**Added**
- `XTerminal.Core/Services/OpenSshConfigParser.cs`
- `XTerminal.Core/Services/JumpHostChain.cs`
- `XTerminal.Core/Services/SshConnectionDiagnostics.cs`
- `XTerminal.Core/Services/SshConnectionProbe.cs` (shared by session Diagnose surface)
- `XTerminal.Core/Services/ShellIntegrationParser.cs` (compile dependency for terminal tab; enablement is Phase 3C)
- `XTerminal.Tests/Phase3AFunctionalTests.cs`
- `docs/PHASE3A_REPORT.md`

**Modified**
- `XTerminal.Core/Models/ConnectionState.cs` — `KeyboardInteractive`
- `XTerminal.Protocols/Ssh/SshAuthFactory.cs`
- `XTerminal.Protocols/Ssh/SshSessionHost.cs`
- `XTerminal.Protocols/Ssh/SshTransportFactory.cs`
- `XTerminal.Protocols/Ssh/SshConnectionTester.cs`
- `XTerminal.App/ViewModels/SessionsViewModel.cs` — OpenSSH import (+ Diagnose button wiring)
- `XTerminal.App/Views/SessionsView.xaml`
- `XTerminal.App/ViewModels/TerminalTabViewModel.cs` — auth/duration/status labels

**Explicitly deferred (uncommitted WIP for 3B–3D)**  
SFTP conflict/chmod UI, workspace restore, keybinding record routing, ports LAN confirm settings, `Phase3FunctionalTests` (3B+), draft `PHASE3_REPORT.md`, etc.

---

## 5. Architectural decisions

1. **Exclusive jump ownership** — shared SSH leases are disabled when any ProxyJump is present.
2. **Partial OpenSSH compatibility** — intentional subset; shell-bearing directives never run.
3. **Failure taxonomy** — UI/status use `SshFailureKind` without exposing secrets.
4. **Agent honesty** — Agent mode does not claim Pageant/ssh-agent support.
5. **Milestone isolation** — Phase 3A Git checkpoint excludes SFTP/workspace/keybinding milestone files.

---

## 6. Security considerations

- Host-key verification remains mandatory; unknown/changed keys are never auto-accepted.
- Credentials stay in Windows Credential Manager; OpenSSH import does not persist secrets.
- Diagnostic/sanitize helpers strip password/passphrase wording from messages.
- No weakening of Phase 1–2 guarantees for test convenience.

---

## 7. Automated test results

```
Filter: exclude Phase3FunctionalTests (3B+ WIP)
Configuration: Release
Output: <project-root>\_phase3a_out

Expected:
  Phase 1–2: 44 passed
  Phase 3A:    9 passed
  Total:      53 passed
```

Full working-tree runs may also execute uncommitted 3B+ tests; those are **not** part of the Phase 3A gate.

---

## 8. Manual integration checklist (not executed in this milestone)

- [ ] Password, key, passphrase-protected key, keyboard-interactive against real hosts
- [ ] Agent mode with/without default `~/.ssh` keys — confirm error text
- [ ] Cancel mid-connect; timeout behavior
- [ ] Single jump + multi-hop with host-key accept/reject per hop
- [ ] Import real `~/.ssh/config`; verify preview, duplicates, unsupported list
- [ ] Confirm no ProxyCommand execution and no secrets in session JSON

---

## 9. Task classification

| Task | Classification |
|------|----------------|
| 0 Baseline | **Completed and validated** |
| 1 SSH connection management | **Implemented but requires manual validation** |
| 2 Jump hosts | **Implemented but requires manual validation** |
| 3 OpenSSH import | **Implemented but requires manual validation** |

---

## 10. Limitations (Phase 3A)

- No true ssh-agent/Pageant bridge.
- Intermediate jump hops reuse jump-host credentials (not per-profile hop secrets).
- OpenSSH import is partial (no `Match`, no `ProxyCommand`, etc.).
- Sessions **Diagnose** control is present early; full diagnostics milestone remains Phase 3C.

---

## 11. Phase 3B preview (not started here)

Tasks 4–6: SFTP transfer queue/conflicts, remote file management/chmod, remote editor conflict handling — held until this Phase 3A report is accepted.
