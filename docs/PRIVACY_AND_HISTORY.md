# Private Input, History, and Broadcast Guarantees

## Private Input Mode

**What it does**

- Suppresses application-managed command history (`CommandHistoryTracker` clears the in-progress line and does not flush entries).
- Prevents broadcast fan-out while the focused pane has Private Input enabled (input goes only to that pane).
- Does not write private keystrokes into AppLog diagnostic lines (history path never receives them).

**What it does not do**

- It does **not** disable remote shell echo. Password prompts and interactive apps may still echo or log input on the remote host.
- It does **not** prevent the local ConPTY/SSH channel from transmitting keystrokes to the session.
- It does **not** guarantee secrecy against process memory inspection, accessibility APIs, or host-side audit logging.
- Masking the UI in a way that claims “secure password entry” would be misleading; X-Terminal therefore does not fake echo masking.

## Command history capture

History is inferred from Enter-terminated input lines (ConPTY / SSH input), not from shell integration OSC sequences.

| Shell | Capture quality | Exit codes |
|-------|-----------------|------------|
| PowerShell / CMD (ConPTY) | Best-effort line capture | Not available without shell integration |
| WSL / Bash over ConPTY | Same as above | Not available |
| SSH remote shells | Same, labeled `SshInferred` | Not fabricated |

Duplicates within a short window for the same session are coalesced. Private Input and configured exclude patterns skip storage.

## Broadcast safety

- Modes: Off, All panes in the **active group**, Selected panes in the **active group**.
- Disconnected panes and Private Input panes are excluded from targets.
- Multi-line paste and multi-host broadcasts prompt when Security settings require confirmation.
- Closing a group disables broadcast for that group.
