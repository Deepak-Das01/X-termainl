# Keyboard shortcuts

Custom bindings are stored in application settings (`settings.json` keybindings list). Recording happens only in **Settings → Keybindings** after clicking **Record selected**.

## Recording

1. Select a command row.
2. Click **Record selected**.
3. Press the desired chord (Esc cancels).
4. Conflicts prompt before silent overwrite.
5. Reserved chords (for example `Alt+F4`, `Ctrl+Shift+P`) cannot be assigned.

Terminal keystrokes are not captured while recording unless Settings has focus.

## Context-aware routing

Global commands (`app.*`, `tab.*`, `workspace.*`, `settings.*`) remain available broadly. Terminal-only shortcuts do not steal focus from Files, Editor text boxes, or Settings fields.

## Recovery

- **Reset selected** restores one default
- **Restore defaults** clears all overrides (with confirmation)
- Command Palette remains available via the reserved recovery chord
