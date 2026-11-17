# Shell integration

X-Terminal can optionally parse shell-emitted OSC sequences (OSC 7, 133, 633) to show working directory, command boundaries, exit status and duration.

## Status

| Shell | Support | Notes |
|-------|---------|-------|
| Bash | Optional | Requires user-approved OSC 133 / OSC 7 hooks |
| PowerShell 7 | Optional | OSC 633 / VS Code-style sequences when the host emits them |
| Windows PowerShell 5.1 | Partial | Limited; may remain Detecting/Degraded without OSC |
| Zsh | Optional / partial | Same OSC 133 model as Bash when hooks are present |

Integration states: **Disabled**, **Unavailable**, **Detecting**, **Active**, **Degraded**.

## Security

- Terminal output is **untrusted**.
- Sequences never execute local commands, open URIs, modify the clipboard, or start transfers.
- Private Input Mode suppresses application history capture, including shell-integration command text.
- Metadata is advisory and is not used for security-sensitive trust decisions.

## Enabling

1. Settings → Advanced → **Enable shell integration parsing**.
2. Optionally install shell hooks **only after explicit approval** (X-Terminal does not rewrite remote `.*rc` files automatically).
3. Restart the terminal pane or reconnect SSH.

## Hook installation (explicit approval required)

X-Terminal may offer to copy a short snippet into the clipboard for the user to paste. It will **not** silently edit:

- `~/.bashrc` / `~/.bash_profile`
- `~/.zshrc`
- PowerShell `$PROFILE`

### Example Bash (OSC 133 + OSC 7) — review before use

```bash
# X-Terminal optional integration — paste only if you approve
__xterm_precmd() {
  local ec=$?
  printf '\033]133;D;%s\007' "$ec"
  printf '\033]7;file://%s%s\007' "$HOSTNAME" "$PWD"
  printf '\033]133;A\007'
}
__xterm_preexec() { printf '\033]133;C\007'; }
if [[ -z "${__XTERMINAL_HOOKS:-}" ]]; then
  __XTERMINAL_HOOKS=1
  PROMPT_COMMAND="__xterm_precmd${PROMPT_COMMAND:+;$PROMPT_COMMAND}"
  trap '__xterm_preexec' DEBUG
fi
```

### Example PowerShell 7 — review before use

Prefer enabling VS Code / Windows Terminal shell integration features that emit OSC 633, or add a prompt function that writes OSC 133 D/A sequences. X-Terminal does not overwrite `$PROFILE`.

### Example Zsh — review before use

```zsh
# X-Terminal optional integration — paste only if you approve
precmd() { printf '\033]133;D;%s\007' $?; printf '\033]7;file://%s%s\007' "$HOST" "$PWD"; printf '\033]133;A\007'; }
preexec() { printf '\033]133;C\007'; }
```

## Removal

Remove the pasted blocks from your shell configuration and disable the Settings checkbox. Restart panes.

## Limitations

- Without hooks, integration remains Detecting/Unavailable; the terminal still works normally.
- Exit codes are shown only when a reliable OSC finish event is received — never invented.
- Remote server history and auditing are unaffected by Private Input Mode.
