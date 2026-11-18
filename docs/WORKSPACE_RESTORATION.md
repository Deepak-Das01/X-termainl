# Workspace restoration

X-Terminal persists **workspace metadata only** — tab layout, split trees, session profile references, editor file paths, and UI chrome. It does **not** persist terminal buffers, passwords, passphrases, or live process state.

## Schema

- File: `%AppData%\XTerminal\workspace-layout.json`
- Backup: `workspace-layout.json.bak`
- Named profiles: `%AppData%\XTerminal\workspaces\`
- Current schema version: **2**

## Guarantees

- Atomic write via temporary file + replace
- Corrupt primary file falls back to last-good backup
- Validation clamps tab/pane counts and split depth
- Missing SSH profiles are skipped; remaining tabs still restore
- Remote reconnect is **opt-in** (`Auto reconnect restored SSH sessions`)
- Restored layout ≠ restored shell process

## Startup modes

| Preference | Behavior |
|------------|----------|
| Restore workspace layout | Rebuild tabs/panes from metadata |
| Open default shell on startup | Used when restore yields no tabs |
| Auto reconnect restored SSH | Only when enabled; otherwise SSH tabs restore disconnected |

Launch without restoration by disabling **Settings → Advanced → Restore workspace layout**.

## Limitations

- Unsaved editor contents are not crash-recovered by default
- Remote editor tabs are not auto-reopened without a matching live SFTP session
- Named workspace open closes the current layout after confirmation
