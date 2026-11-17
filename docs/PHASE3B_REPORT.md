# X-Terminal Phase 3B — Professional SFTP and Remote File Management

**Milestone:** Phase 3B  
**Scope:** SFTP lifecycle, transfer queue/metrics/conflicts, remote file ops, chmod, remote editor hardening  
**Base checkpoint:** Phase 3A `892d934`  
**Date:** 2026-11-17

---

## 1. Executive summary

Phase 3B upgrades SFTP into a production-oriented transfer and remote file workspace: queued transfers with a real state machine, byte-accurate progress metrics, conflict policies (Ask/Overwrite/Skip/Rename + batch sticky scope), path safety (traversal/ADS/reserved names/symlink non-follow), chmod editor, and stronger remote editor association/conflict handling.

**Automated:** Release build + **66** tests on `_phase3b_out` (includes 53 Phase 3A gate + new Phase 3B tests).  
**Manual:** Live SFTP checklist prepared — not executed in this milestone.  
**Git:** Phase 3B checkpoint created on `main` without committing Phase 3C/3D WIP (keybindings, workspace restore UI, ports LAN confirm, shell-integration settings UI).

---

## 2. Baseline and Git status

| Item | Result |
|------|--------|
| Branch | `main` |
| Phase 3A | `892d934` accepted |
| Running app | a running local XTerminal instance (not terminated) |
| Build output | `_phase3b_out` (default bin locked) |
| Phase 3A gate | **53/53** preserved |

### WIP triage

| Path | Milestone |
|------|-----------|
| SFTP service/VM/views, path safety, transfer helpers, chmod dialog, Phase3B tests | **3B** |
| PortForwardEditor LAN confirm, shell-integration settings UI, diagnostics extras | **3C** |
| MainWindow keybinding TryMatch, workspace layout store/restore, keybinding record UI | **3D** |
| `MainViewModel` mixed editor+workspace+keybinding | **Split** — editor conflict UX remains working-tree; not fully isolated into 3B commit |

---

## 3. Files added and modified (Phase 3B checkpoint)

**Added**
- `XTerminal.Core/Services/SftpPathSafety.cs`
- `XTerminal.Core/Services/TransferProgress.cs`
- `XTerminal.Core/Services/TransferStateMachine.cs`
- `XTerminal.Core/Services/UnixPermissions.cs`
- `XTerminal.App/Views/UnixPermissionEditorWindow.xaml(.cs)`
- `XTerminal.Tests/Phase3BFunctionalTests.cs`
- `docs/PHASE3B_REPORT.md`

**Modified**
- `XTerminal.Core/Models/Sftp/SftpModels.cs`
- `XTerminal.Core/Settings/AppSettings.cs` (Files: Rename, MaxConcurrentTransfers, MaxEditorFileBytes; Advanced schema props may be present as defaults)
- `XTerminal.Protocols/Ssh/SftpSessionService.cs`
- `XTerminal.App/ViewModels/SftpWorkspaceViewModel.cs`
- `XTerminal.App/Views/SftpView.xaml(.cs)`
- `XTerminal.App/Services/RemoteEditorFileService.cs`
- `XTerminal.App/ViewModels/EditorTabViewModel.cs`
- Files settings ComboBox items (Prompt/Overwrite/Skip/Rename + concurrency/editor limits)

---

## 4. SFTP architecture

- Auth and host-key verification continue via `SshTransportFactory.OpenSftpAsync` (jump chains included from 3A).
- One owned `SftpTransport` / `SftpClient` per workspace; no unsafe shared shell client reuse.
- Transfer concurrency default **1** (SSH.NET stream safety); configurable 1–4 via Files settings.
- Connect/disconnect/cancel cancel active transfers; dispose is idempotent-minded.
- Network work runs on background tasks; UI updates marshaled to the dispatcher.

---

## 5. Transfer queue implementation

- States: Queued → Transferring → Complete | Failed | Cancelled | Skipped
- Invalid transitions throw via `TransferStateMachine`
- Queue actions: cancel selected, cancel all (confirm), retry, remove, clear completed
- Stable `Guid` per job; retry clones a new id
- Pause/resume **not** exposed

---

## 6. Supported transfer operations

| Operation | Status |
|-----------|--------|
| Single/multi file upload/download | Yes |
| Recursive folder upload/download | Yes (skips symlinks/reparse points) |
| Explorer drag-drop upload | Yes |
| Local↔remote panel DnD | Partial (Explorer → remote yes; dual local pane polish limited) |
| Retry / cancel / clear | Yes |

---

## 7. Conflict resolution

- Policies: Ask, Overwrite, Skip, Rename
- Ask UI shows source/destination names, sizes, mtimes, path
- Apply-to-remaining sticky for current batch only (does not rewrite global settings)
- Cancel path maps to Skip (YesNoCancel: Cancel=Rename)

---

## 8. Transfer metrics

- Bytes transferred, percentage (`TransferProgress`), speed, ETA, elapsed, start/complete timestamps
- Unknown totals: `-1` / N/A
- Zero-byte: completes at 100%
- Progress publish throttled (~100ms)
- Never marks 100% before successful completion helper
- No fabricated byte counters; no file-content logging

---

## 9. Remote file management

- Browse, breadcrumbs, path box, back/forward/up/home/refresh
- Current-directory search filter
- Create file/folder, rename, delete (protected roots blocked; recursive delete does not follow symlinks)
- Properties, copy remote path, open in editor
- Permissions column + symlink glyph
- Multi-select enabled on grid
- Server-side copy/move: **not** claimed — rename only; copy disabled as unsupported

---

## 10. Unix permission handling

- `UnixPermissionEditorWindow` with owner/group/other RWX, octal sync, symbolic display
- Presets 644/755/600/700/775 supported via octal
- Applies via `ChangePermissions` only — **no chown**
- Non-recursive by default

---

## 11. Remote editor behavior

- Opens via SFTP; tab title shows `file @ host`
- Bound to `RemoteEditSession.SessionId` / host
- Binary probe; large-file limit from Files settings
- Line-ending detection/preservation
- Dirty tracking + unsaved close confirm (existing)
- Atomic temp upload/rename when possible
- Conflict: detect mtime change; working-tree MainViewModel offers Overwrite / Reload / Save local copy (may remain uncommitted if mixed with 3D)

**Limitation:** remote rename after delete is not fully atomic on all servers; fallback overwrite documented in code.

---

## 12. Security safeguards

- Remote path normalize + traversal rejection
- Local download root confinement, reserved Windows names, ADS rejection
- Symlinks not followed in recursive download/delete
- Dangerous delete targets blocked (`/`, `/etc`, …)
- Partial download temps cleaned on cancel/fail
- No credential/file-content logging
- Host-key policy unchanged

---

## 13. Settings integration

| Setting | Wired |
|---------|-------|
| Default download directory | Yes |
| Conflict behavior Prompt/Overwrite/Skip/Rename | Yes |
| Show hidden files | Yes |
| Notify on transfer complete | Yes |
| Max concurrent transfers | Yes |
| Max editor file bytes | Yes (service + open path) |
| Human-readable sizes | Display helpers (partial consistency) |

---

## 14. Automated test counts

```
Release (_phase3b_out): Passed 66 / Failed 0
Phase 3A gate filter (~Phase3FunctionalTests & ~Phase3B): 53 preserved when isolating 3A+prior
Phase 3BFunctionalTests: 10
```

Coverage includes state machine, progress math, path safety, permissions, conflict enum, editor helpers.

---

## 15. Release build results

```
dotnet test -c Release -o _phase3b_out → succeeded (0 errors, 66 tests)
```

a running local process left running.

---

## 16. Manual validation checklist (not executed)

- [ ] Password / key auth SFTP
- [ ] Host-key accept/reject
- [ ] SFTP via jump host
- [ ] Single/multi/recursive transfers
- [ ] Large + zero-byte files
- [ ] Cancel mid-transfer; disconnect mid-transfer; retry
- [ ] Overwrite / Skip / Rename / apply remaining
- [ ] Chmod 644/755; permission denied
- [ ] Symlink non-follow on recursive ops
- [ ] Remote edit + external mtime conflict paths
- [ ] Shutdown during transfer

---

## 17. Remaining limitations

- Dual local/remote pane browser is remote-centric (local Explorer DnD upload only)
- No server-side copy; no chown; no pause/resume
- Concurrent transfers >1 on one client remain cautiously capped
- Cryptographic integrity verification not claimed
- Accurate terminal CWD sync deferred to Phase 3C shell integration
- Some MainViewModel editor UX may remain in uncommitted mixed WIP

---

## 18. Git checkpoint hash

**`d61d843`** — `Phase 3B: Professional SFTP transfers and remote file management`  
(`main` ahead of origin by 2 with Phase 3A; not pushed.)

---

## 19. Uncommitted files remaining (expected 3C/3D)

- `MainWindow.xaml.cs` (keybinding routing)
- `MainViewModel.cs` (workspace restore + keybinding + possibly editor conflict wiring)
- `PortForwardEditorWindow.xaml.cs`
- `SettingsView` Advanced shell/workspace UI (if not included)
- `WorkspaceLayoutDocument` / `JsonWorkspaceLayoutStore`
- `Phase3FunctionalTests.cs` (shell/workspace/diagnostics)
- Draft `docs/PHASE3_REPORT.md`

---

## 20. Recommended Phase 3C work

Shell integration (optional OSC), connection diagnostics panel polish, advanced port-forward health/bind UX — after this report is reviewed.

---

## Task classification

| Task | Classification |
|------|----------------|
| 0 Baseline | **Completed and validated** |
| 1 SFTP architecture | **Implemented but requires manual validation** |
| 2 SFTP workspace | **Implemented but requires manual validation** |
| 3 Transfer queue | **Implemented but requires manual validation** |
| 4 Transfer metrics | **Completed and validated** (automated) |
| 5 Conflicts | **Implemented but requires manual validation** |
| 6 Path safety / integrity | **Completed and validated** (automated + code) |
| 7 Remote file ops | **Implemented but requires manual validation** |
| 8 Unix permissions | **Implemented but requires manual validation** |
| 9 Remote editor | **Implemented but requires manual validation** |
| 10 Terminal↔SFTP | **Partially completed** (open SFTP/editor; CWD sync deferred) |
| 11 Settings / a11y | **Implemented but requires manual validation** |
| 12 Automated tests | **Completed and validated** |
| 13 Manual checklist | **Prepared; not executed** |
| 14 Build + Git checkpoint | **Completed and validated** |
