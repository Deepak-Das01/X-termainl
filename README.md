# X-Terminal

**X-Terminal** is a modern Windows terminal, secure remote administration and developer workspace featuring SSH, SFTP, integrated editing, advanced session management, networking tools and intelligent shell integration.

> Repository: [Deepak-Das01/X-termainl](https://github.com/Deepak-Das01/X-termainl)

Built with **.NET 8**, **WPF (MVVM)**, **WebView2** (xterm.js + Monaco), **ConPTY**, and **SSH.NET**.

---

## Table of contents

1. [Product overview](#product-overview)
2. [Feature set](#feature-set)
3. [Architecture](#architecture)
4. [Requirements](#requirements)
5. [Installation / getting started](#how-to-start-and-stop)
6. [Local terminals](#local-terminals)
7. [SSH authentication and jump hosts](#ssh-authentication-and-jump-hosts)
8. [SFTP file management](#sftp-file-management)
9. [Shell integration](#shell-integration)
10. [Command history](#command-history)
11. [Port forwarding](#port-forwarding)
12. [Connection diagnostics](#connection-diagnostics)
13. [Supported commands and shortcuts](#supported-commands-and-shortcuts)
14. [Settings](#settings)
15. [Security and privacy](#security-model)
16. [Troubleshooting](#troubleshooting)
17. [Known limitations](#limitations-and-roadmap)
18. [Workspaces and startup](#workspaces-and-startup)
19. [Keyboard shortcuts](#keyboard-shortcuts-guide)
20. [Build from source](#build-test-and-ci)
21. [Development progress](#development-progress)
22. [Contributing / License](#license-and-acknowledgements)

---

## Product overview

X-Terminal is designed as a MobaXterm-class alternative for Windows operators who need:

- Fast local terminals (PowerShell, CMD, WSL) with split panes
- Secure SSH sessions with host-key verification and Credential Manager secrets
- SFTP browsing, local files, and an embedded code editor
- Port forwarding managed beside terminal sessions
- Snippets, command history, broadcast input, and a command palette

**Supported today:** Local shells (ConPTY) and SSH (interactive + SFTP + forwards).  
**Not implemented yet:** RDP, VNC, Telnet, Serial, X11 (explicitly gated).

---

## Feature set

### Terminal

| Capability | Details |
|------------|---------|
| Local shells | PowerShell, CMD, WSL via ConPTY |
| SSH interactive | SSH.NET + shared connection leases |
| Split panes | Horizontal / vertical splits in a tab group |
| Pane chrome | Maximize/restore, rename, duplicate, close |
| Appearance | Font, size, cursor, color schemes, theme (Light/Dark/System) |
| Find / clear / zoom | In-pane search, clear buffer, Ctrl+/- zoom |
| Context menu | Split, copy/paste/select all, reconnect, broadcast targets |
| Broadcast input | Off · All panes in active group · Selected panes |
| Private Input Mode | Suppresses app history + broadcast for that pane |

### Sessions & SSH

| Capability | Details |
|------------|---------|
| Session library | Folders, favorites, tags, edit/duplicate |
| Quick Connect | One-shot or saveable SSH connect |
| Host keys | Central `known_hosts`, trust prompt, change detection |
| Auth | Password, private key path, agent (as configured per session) |
| Jump host | Per-session proxy jump where configured |
| Import / export | Versioned JSON; **never** exports passwords or key material |
| Reconnect | Per-pane reconnect with lease-aware connection sharing |

### Files & editor

| Capability | Details |
|------------|---------|
| Local Files | Explorer with hidden-file prefs, sizes, open-in-terminal |
| SFTP | Browse/upload/download against an SSH session |
| Editor | Monaco in WebView2 — local + remote SFTP files |
| Auto-save / wrap / minimap | Configurable under Settings → Editor |

### Ports, snippets, history

| Capability | Details |
|------------|---------|
| Port forwards | Local / remote / dynamic SOCKS5; loopback default; LAN bind confirmation |
| Tunnel metrics | State, uptime, listener status; byte counts **N/A (SSH.NET)** |
| Connection diagnostics | Staged DNS → TCP → SSH/host-key/auth report (sanitized export) |
| Shell integration | Optional OSC 7/133/633 parsing; per-pane status; never auto-edits shell rc |
| Snippets | Library with insert/run into active terminal |
| History | Enter-inferred + shell-integrated metadata; result/date filters; privacy controls |

### Application UX

- Activity bar: Terminal, Sessions, SFTP, Files, Editor, Ports, Snippets, History, Settings  
- Command palette and quick switcher  
- Toast notifications (can be disabled)  
- Confirm exit when sessions are active  
- Customizable keybindings with conflict detection  

---

## Architecture

```
XTerminal.sln
├── XTerminal.App          # WPF UI, ViewModels, WebView2 hosts
├── XTerminal.Core         # Models, settings, stores, import/export
├── XTerminal.Protocols    # SSH / SFTP / ConPTY session hosts, leases
├── XTerminal.Terminal     # ConPTY native interop
└── XTerminal.Tests        # Regression tests (xUnit)
```

**Patterns:** MVVM (CommunityToolkit.Mvvm), JSON persistence under AppData, Windows Credential Manager for secrets, centralized `AppCommandRegistry` for palette/menu parity.

---

## Requirements

| Requirement | Notes |
|-------------|--------|
| OS | Windows 10 / 11 (x64 recommended) |
| Runtime / SDK | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| WebView2 | Evergreen Runtime (usually preinstalled on modern Windows) |
| Optional | WSL for WSL profile; OpenSSH agent if using agent auth |

---

## How to start and stop

### Prerequisites (once)

```powershell
# Verify .NET 8 SDK
dotnet --list-sdks

# Clone (if needed)
git clone https://github.com/Deepak-Das01/X-termainl.git
cd X-termainl
```

### Start — Debug (development)

```powershell
cd <project-root>
dotnet run --project XTerminal.App\XTerminal.App.csproj -c Debug
```

Or open `XTerminal.sln` in Visual Studio / Cursor and press **F5** on the `XTerminal.App` project.

### Start — Release

```powershell
cd <project-root>

# Build
dotnet build XTerminal.sln -c Release

# Run the executable
.\XTerminal.App\bin\Release\net8.0-windows\XTerminal.exe
```

If the default `bin` folder is locked by a running instance, build to an alternate folder:

```powershell
dotnet build XTerminal.App\XTerminal.App.csproj -c Release -o .\_build_out
.\_build_out\XTerminal.exe
```

### Stop the application

| Method | How |
|--------|-----|
| UI | Click the window **Close** button (✕) |
| Keyboard | `Alt+F4` |
| Settings | If **Confirm exit when sessions are active** is on, confirm the dialog |
| PowerShell | `Stop-Process -Name XTerminal` (force; avoid if you have open remote work) |
| Task Manager | End task **XTerminal** |

Graceful close disposes SFTP, port forwards, terminal hosts, and SSH connection leases.

### First launch behavior

Controlled by **Settings → General**:

- Open default shell on startup (PowerShell / profile default)
- Restore last activity section (not live processes)
- Show toast notifications
- Confirm exit with active sessions

---

## Supported commands and shortcuts

Open the **Command Palette** with `Ctrl+Shift+P` and type any command name. Shortcuts below are defaults; customize under **Settings → Keybindings**.

### Application

| Command | Default shortcut | Description |
|---------|------------------|-------------|
| Command Palette | `Ctrl+Shift+P` | Search and run any registered command |
| Quick Switcher | `Ctrl+P` | Jump between open tabs |
| Open Settings | `Ctrl+,` | Settings workspace |
| Toggle Utility Panel | — | Show/hide side utility panel |
| Close Tab | `Ctrl+W` | Close active tab |
| Reopen Closed Tab | `Ctrl+Shift+T` | Restore last closed tab |
| Next / Previous Tab | `Ctrl+Tab` / `Ctrl+Shift+Tab` | Cycle tabs |
| New Terminal | `Ctrl+Shift+N` | New PowerShell tab |
| Help / Palette (F1) | `F1` | Opens command palette |
| Escape | `Esc` | Close palette / switcher overlays |

### Terminal

| Command | Default shortcut | Description |
|---------|------------------|-------------|
| New PowerShell | — | Local PowerShell session |
| New CMD | — | Local Command Prompt |
| New WSL | — | Local WSL session |
| Split Right | `Alt+Shift+Right` | Vertical split |
| Split Down | `Alt+Shift+Down` | Horizontal split |
| Focus next/prev pane | `Alt+←/→/↑/↓` | Move focus between panes |
| Close Pane | `Ctrl+Shift+W` | Close active pane (or tab if last) |
| Maximize / Restore Pane | `Alt+Z` (editor context) / palette | Maximize one pane in the group |
| Rename Tab | — | Rename active tab |
| Clear Terminal | — | Reset terminal buffer |
| Find in Terminal | `Ctrl+F` (terminal) | Search scrollback |
| Copy | `Ctrl+Shift+C` | Copy selection |
| Paste | `Ctrl+Shift+V` | Paste clipboard |
| Select All | — | Select terminal buffer |
| Reset Zoom | `Ctrl+0` | Reset font zoom |
| Zoom In / Out | `Ctrl+=` / `Ctrl+-` | Terminal zoom |
| Toggle Broadcast Input | — | Toggle broadcast on/off |
| Broadcast: All Panes | — | Fan-out to connected panes in group |
| Broadcast: Selected Panes | — | Fan-out to marked targets |
| Broadcast: Off | — | Disable broadcast |
| Toggle Private Input Mode | — | Suppress history + broadcast for pane |
| Show Shell Integration Status | — | Per-pane integration state |
| Show Current Working Directory | — | Tracked CWD when available |
| Copy Bash/PowerShell/Zsh Integration Snippet | — | Clipboard only — never auto-installs |
| Reconnect | — | Reconnect active SSH/local host |

### SSH / Sessions

| Command | Shortcut | Description |
|---------|----------|-------------|
| Quick Connect | — | SSH quick connect dialog |
| Open Sessions | — | Sessions activity |
| Open SFTP | — | SFTP workspace |
| Show Connection Diagnostics | — | Staged SSH diagnostics dialog |
| Export Sessions | — | JSON export (no secrets) |
| Import Sessions | — | JSON import with conflict preview |
| Reconnect | — | Reconnect active terminal |
| Synchronize SFTP Directory | — | Navigate SFTP to terminal CWD |
| Toggle SFTP Directory Following | — | Auto-follow shell CWD events |

### Editor / Files

| Command | Shortcut | Description |
|---------|----------|-------------|
| Open File | `Ctrl+O` | Open local file in editor |
| Save | `Ctrl+S` | Save editor document |
| Save As | `Ctrl+Shift+S` | Save as… |
| Open Files | — | Local files explorer |
| Insert Snippet | `Ctrl+Shift+I` | Open snippets activity |

### Ports / History / Settings

| Command | Shortcut | Description |
|---------|----------|-------------|
| Create Port Forward | — | Create tunnel |
| Start / Stop / Restart Selected Tunnel | — | Control selected forward |
| Search History | — | History activity |
| Clear Command History | — | Wipe stored history (confirms) |
| Settings: General / Appearance / Terminal / SSH / Security / Keybindings | — | Jump to category |

---

## Local terminals

Local profiles use ConPTY for PowerShell, CMD, and WSL. Split panes, broadcast, Private Input, and optional shell integration apply the same way as SSH panes. Working-directory tracking requires shell-emitted OSC sequences (see [Shell integration](#shell-integration)).

---

## SSH authentication and jump hosts

| Topic | Behavior |
|-------|----------|
| Auth methods | Password, private key file path, agent/default-key fallback (not a full Pageant bridge) |
| Host keys | Centralized verification; unknown/changed keys require explicit trust — diagnostics never auto-accept |
| Jump hosts | Multi-hop ProxyJump with per-hop host-key checks where configured |
| OpenSSH config | Partial import of `Host` / `HostName` / `User` / `Port` / `IdentityFile` / `ProxyJump` |
| Keyboard-interactive | Supported for interactive auth prompts |

Technical notes: [`docs/PHASE3A_REPORT.md`](docs/PHASE3A_REPORT.md).

Example profile values for documentation:

- Host: `server.example.com`
- Bastion: `bastion.example.com`
- Username: `username`

---

## SFTP file management

- Browse, upload, download with a real transfer queue (progress, speed, ETA, cancel, retry)
- Conflict policies, path-traversal protection, remote permission editing (including setuid/setgid/sticky)
- Integrated remote editor with conflict detection
- Drag-and-drop uploads and directory search
- Optional **follow terminal working directory** when shell integration reports CWD for the **same** SSH session (debounced; never auto-transfers)

Details: [`docs/PHASE3B_REPORT.md`](docs/PHASE3B_REPORT.md).

---

## Shell integration

Optional, advisory metadata from Bash / PowerShell / Zsh when the shell emits OSC **7**, **133**, or **633**.

| State | Meaning |
|-------|---------|
| Disabled | Parsing off |
| Detecting | Waiting for first valid sequence |
| Active | Receiving reliable events |
| Degraded | Malformed/oversized input; terminal still works |
| Unavailable | Host/shell cannot provide events |

**Enable:** Settings → Advanced → Enable shell integration parsing.  
**Install hooks:** use Command Palette → Copy … Integration Snippet, then paste into your shell config **only after you approve**. X-Terminal never rewrites remote `.*rc` / `$PROFILE` automatically.

Full guide: [`docs/SHELL_INTEGRATION.md`](docs/SHELL_INTEGRATION.md).

---

## Command history

- Legacy Enter-based capture and shell-integration finish events
- Deduplication prefers shell-integration records when both fire
- Filters: date range, local/SSH, shell type, result (Succeeded / Failed / Unknown)
- Exit codes and durations are recorded **only** when shell integration provides them — never invented
- Private Input suppresses all application capture, including delayed shell events carrying command text
- Does **not** clear remote shell history or server audit logs

---

## Port forwarding

Supports **local**, **remote**, and **dynamic SOCKS5** forwards via SSH.NET.

- Defaults to **loopback** (`127.0.0.1`); non-loopback / LAN binds require explicit confirmation
- Independent tunnel lifecycle with SSH lease awareness — closing a terminal tab does not always stop workspace tunnels
- Byte counters remain **N/A** (SSH.NET does not expose trustworthy per-forward counters)

Details: [`docs/PORT_FORWARDING.md`](docs/PORT_FORWARDING.md).

---

## Workspaces and startup

- **Save Workspace** / **Open Workspace** from the command palette (named profiles under `%AppData%\XTerminal\workspaces\`)
- **Restore previous workspace** rebuilds tabs and splits from metadata — not prior process state
- Settings → Advanced:
  - Restore workspace layout on startup
  - Auto-reconnect restored SSH sessions (explicit opt-in; host-key policy unchanged)
- Missing SSH profiles are skipped; other tabs still restore
- Unsaved editor buffers are not persisted by default

Details: [`docs/WORKSPACE_RESTORATION.md`](docs/WORKSPACE_RESTORATION.md).

## Keyboard shortcuts guide

- Settings → Keybindings: search, record, clear, reset, restore defaults
- Conflicts require confirmation before reassignment
- Context-aware routing keeps Files/Editor typing from being stolen by terminal chords
- Details: [`docs/KEYBINDINGS.md`](docs/KEYBINDINGS.md)

## Connection diagnostics

From Sessions → Diagnose (or palette **Show Connection Diagnostics**):

1. Profile validation  
2. Hostname resolution  
3. TCP connectivity  
4. Jump-host progression (when configured)  
5. SSH negotiation / authentication (respects host-key policy)  

Exports are sanitized (no passwords/passphrases/keys). TCP success is not presented as SSH latency or a fake “connection quality” percentage.

---

## Troubleshooting

| Symptom | What to check |
|---------|----------------|
| Shell CWD / exit always N/A | Enable shell integration; install approved hooks; confirm OSC output |
| SFTP does not follow terminal | Same SSH session; follow toggle on; reliable Directory events |
| Tunnel bind fails | Port in use; loopback vs LAN confirmation; SSH session reachable |
| Diagnostics DNS/TCP fail | Hostname, firewall, bastion path; jump direct-target probes may fail by design |
| Host key rejected | Expected for unknown/changed keys until you trust via the normal prompt |

### Pane context menu (right-click terminal)

Split Right · Split Down · Duplicate Pane · Maximize/Restore · Close Pane · Rename · Reconnect · Copy · Paste · Select All · Find · Clear Terminal · Reset Zoom · Broadcast All / Selected / Toggle Target / Off

### Tab context menu

Rename · Pin/Unpin · Duplicate · Close · Close Others · Close to the Right · Close Unpinned

---

## Workspaces guide

| Activity | Purpose |
|----------|---------|
| **Terminal** | Tabbed/split terminal workspace (primary) |
| **Sessions** | SSH profile library — connect, SFTP, edit, import/export |
| **SFTP** | Remote file tree and transfers for the active SSH session |
| **Files** | Local filesystem explorer |
| **Editor** | Monaco editor tabs (local / remote) |
| **Ports** | SSH port forwards with runtime state |
| **Snippets** | Reusable command snippets |
| **History** | Application-managed command history |
| **Settings** | Full product configuration |

---

## Settings

| Category | What you configure |
|----------|--------------------|
| **General** | Startup shell, default workspace, exit confirm, restore activity, toasts |
| **Appearance** | Theme, window material, font, cursor, color schemes |
| **Terminal** | Default shell, scrollback, copy-on-select, right-click, bell |
| **Profiles** | PowerShell / CMD / WSL profiles, startup dir/args, default profile |
| **SSH** | Timeout, keepalive, default port/user, reconnect, strict host keys |
| **Editor** | Font, wrap, minimap, tabs, auto-save, whitespace |
| **Files** | Download directory, conflicts, hidden files, size format, transfer toasts |
| **History** | Enable, retention, max entries, exclude SSH, exclude patterns |
| **Keybindings** | Searchable action list, custom shortcuts, reset / restore defaults |
| **Security** | Broadcast safety, known hosts list/remove, privacy notes |
| **Advanced** | Logging, shell integration, CWD tracking, SFTP follow, non-loopback bind confirm, AppData |
| **About** | Version, runtime, dependencies, copy diagnostics |

Nested options persist when you leave Settings, save, or change fields that call flush. Theme/material may require a restart on some Windows builds.

---

## Security model

- **Secrets:** Passwords and passphrases live only in **Windows Credential Manager** — never in session JSON exports.
- **Host keys:** Central verification with interactive trust / replace; stored under AppData `known_hosts`.
- **SSH leases:** Shared connections for shell + forwards without weakening ownership rules.
- **Private Input:** Blocks application history and broadcast only. It does **not** stop remote echo or host-side logging. See `SECURITY_NOTES.md` and `docs/PRIVACY_AND_HISTORY.md`.
- **Broadcast:** Confirms multi-line paste and multi-host sends when Security settings require it; never targets Private Input or disconnected panes; clears when the group closes.

---

## Application data

| Data | Location |
|------|----------|
| Settings, sessions, history, snippets, ports, UI state | `%AppData%\XTerminal\` |
| Known hosts | `%AppData%\XTerminal\known_hosts` |
| Logs | `%LocalAppData%\XTerminal\logs\` |
| Credentials | Windows Credential Manager (not files) |

Use **Settings → Advanced → Open app data folder** to browse AppData.

---

## Development progress

### Phase 1 — Security & lifecycle

- Centralized SSH host-key verification and prompts  
- SSH connection leases for shared clients  
- WebView2 / ConPTY lifecycle hardening  
- Secure persistence and shortcut routing fixes  
- Initial regression suite  

### Phase 2 — Functional maturity

- Complete Settings workspace (all categories backed by real behavior)  
- Terminal pane context menus  
- Expanded command palette (`AppCommandRegistry`)  
- Advanced broadcast (Off / All / Selected) with safety prompts  
- History improvements + Private Input guarantees  
- Ports runtime accuracy; honest N/A metrics  
- Versioned session import/export (no secrets)  
- GitHub Actions Windows CI  
- Expanded regression tests (44 passing in Phase 2 validation)  

### Phase 3A — Advanced SSH

Jump hosts, keyboard-interactive auth, failure classification, OpenSSH config import (partial). See [`docs/PHASE3A_REPORT.md`](docs/PHASE3A_REPORT.md).

### Phase 3B — Professional SFTP

Transfer queue, conflict policies, permissions, remote editor, drag-and-drop. See [`docs/PHASE3B_REPORT.md`](docs/PHASE3B_REPORT.md).

### Phase 3C — Shell integration, networking, diagnostics

Optional OSC shell integration, terminal↔SFTP CWD sync, advanced port forwarding, connection diagnostics UI. See [`docs/PHASE3C_REPORT.md`](docs/PHASE3C_REPORT.md) and [`docs/SHELL_INTEGRATION.md`](docs/SHELL_INTEGRATION.md).

### Phase 3D — Workspace restoration and keybindings

Named workspaces, layout restore with opt-in SSH reconnect, shortcut recording with conflict handling, and context-aware routing. See [`docs/PHASE3D_REPORT.md`](docs/PHASE3D_REPORT.md), [`docs/WORKSPACE_RESTORATION.md`](docs/WORKSPACE_RESTORATION.md), and [`docs/KEYBINDINGS.md`](docs/KEYBINDINGS.md).

Detailed Phase 2 notes: [`docs/PHASE2_REPORT.md`](docs/PHASE2_REPORT.md).

---

## Build, test, and CI

```powershell
# Restore + Release build
dotnet restore
dotnet build XTerminal.sln -c Release

# Run all regression tests
dotnet test XTerminal.Tests\XTerminal.Tests.csproj -c Release

# If App bin is locked by a running XTerminal.exe:
dotnet build XTerminal.App\XTerminal.App.csproj -c Release -o .\_build_out
dotnet test XTerminal.sln -c Release -o .\_build_out
```


CI workflow: [`.github/workflows/ci.yml`](.github/workflows/ci.yml) — Windows runner, .NET 8, Release build + tests. No embedded secrets; no automatic GitHub Releases.

---

## Limitations and roadmap

| Topic | Status |
|-------|--------|
| RDP / VNC / Telnet / Serial / X11 | Not implemented (gated stubs) |
| Per-forward byte counters | Not available from SSH.NET — UI shows **N/A** |
| Shell integration without hooks | Remains Detecting/Unavailable; terminal still works |
| SSH agent / Pageant | Default-key fallback only — not a full agent bridge |
| Independent jump-host credentials | Limited; see SSH backlog in Phase 3A/3C reports |
| Per-forward byte / connection truth | Listener status only; bytes N/A |
| Password echo masking | Not faked; Private Input documents real guarantees |
| Live process / buffer restore | Not supported (by design) |
| RDP / VNC / etc. | Explicitly out of scope until dedicated design |

Contributions and issues: use the GitHub repository. Keep secrets out of commits — `.gitignore` excludes keys, `.env`, logs, WebView2 data, and build outputs.

---

## License and acknowledgements

Runtime stack includes .NET 8, WPF, CommunityToolkit.Mvvm, Microsoft WebView2, SSH.NET, xterm.js, and Monaco Editor. See **Settings → About** inside the app for the installed version and a non-sensitive diagnostics snapshot.

---

*X-Terminal — enterprise terminal workstation for Windows.*
