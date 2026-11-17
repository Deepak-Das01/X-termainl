# X-Terminal security notes

## Credentials

- SSH passwords and key passphrases are stored only in Windows Credential Manager via `WindowsCredentialStore`.
- Session export/import never includes passwords, passphrases, or private key material.
- Known hosts are stored under `%AppData%\XTerminal\known_hosts`.

## Private Input Mode

- When Private Input is enabled on a pane, **application command history is not recorded** for that pane.
- Broadcast input never targets a pane in Private Input Mode (and the focused private pane receives local input only).
- Private Input does **not** prevent a remote shell from echoing keystrokes or writing its own logs (`.bash_history`, auditd, etc.).

## History

- History is inferred from Enter-submitted terminal input (ConPTY / SSH channel), not from shell integration.
- Exit codes are **not** claimed unless a future shell-integration protocol provides them. The History UI shows `—` when unknown.
- Exclude patterns and “exclude SSH” settings live under Settings → History.

## Broadcast

- Settings → Security can require confirmation for multi-line broadcast paste and warn when multiple distinct remote hosts are targeted.
- Broadcast is disabled when a terminal group/tab is closed.
