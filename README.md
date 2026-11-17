# X-Terminal

**X-Terminal** is a premium, enterprise-focused Windows desktop workstation for local shells and remote SSH — combining capabilities inspired by Windows Terminal, Termius, and Visual Studio Code in one native WPF application.

> Repository: [Deepak-Das01/X-termainl](https://github.com/Deepak-Das01/X-termainl)

Built with **.NET 8**, **WPF (MVVM)**, **WebView2** (xterm.js + Monaco), **ConPTY**, and **SSH.NET**.

---

## Table of contents

1. [Product overview](#product-overview)
2. [Feature set](#feature-set)
3. [Architecture](#architecture)
4. [Requirements](#requirements)
5. [How to start and stop](#how-to-start-and-stop)
6. [Supported commands and shortcuts](#supported-commands-and-shortcuts)
7. [Workspaces guide](#workspaces-guide)
8. [Settings](#settings)
9. [Security model](#security-model)
10. [Application data](#application-data)
11. [Development progress](#development-progress)
12. [Build, test, and CI](#build-test-and-ci)
13. [Limitations and roadmap](#limitations-and-roadmap)

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
| Port forwards | Local / remote / dynamic; start/stop/restart; duplicate bind detection |
| Byte counters | Shown as **N/A (SSH.NET)** — not fabricated |
| Snippets | Library with insert/run into active terminal |
| History | Enter-inferred commands; search/filter/copy/delete; privacy controls |

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
dotnet build XTerminal.App\XTerminal.App.csproj -c Release -o .\_phase2_out
.\_phase2_out\XTerminal.exe
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
| Reconnect | — | Reconnect active SSH/local host |

### SSH / Sessions

| Command | Shortcut | Description |
|---------|----------|-------------|
| Quick Connect | — | SSH quick connect dialog |
| Open Sessions | — | Sessions activity |
| Open SFTP | — | SFTP workspace |
| Export Sessions | — | JSON export (no secrets) |
| Import Sessions | — | JSON import with conflict preview |
| Reconnect | — | Reconnect active terminal |

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
| New Forward | — | Create port forward |
| Start / Stop Selected Forward | — | Control selected forward |
| Search History | — | History activity |
| Clear Command History | — | Wipe stored history (confirms) |
| Settings: General / Appearance / Terminal / SSH / Security / Keybindings | — | Jump to category |

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
| **Advanced** | File logging, log retention, open AppData folder |
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

### Phase 3 (planned)

- Shell integration for richer history / exit codes  
- Stronger keybinding capture UX  
- Jump-host and known-hosts editor polish  
- End-to-end SFTP conflict policies  
- Optional transport metrics if SSH.NET allows  
- Remote protocols (RDP/VNC/etc.) only after dedicated security design  

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
dotnet build XTerminal.App\XTerminal.App.csproj -c Release -o .\_phase2_out
dotnet build XTerminal.Tests\XTerminal.Tests.csproj -c Release -o .\_phase2_out --no-dependencies
dotnet test XTerminal.Tests\XTerminal.Tests.csproj -c Release -o .\_phase2_out --no-build
```

CI workflow: [`.github/workflows/ci.yml`](.github/workflows/ci.yml) — Windows runner, .NET 8, Release build + tests. No embedded secrets; no automatic GitHub Releases.

---

## Limitations and roadmap

| Topic | Status |
|-------|--------|
| RDP / VNC / Telnet / Serial / X11 | Not implemented (gated stubs) |
| Per-forward byte counters | Not available from SSH.NET — UI shows **N/A** |
| Shell-integration exit codes | Not claimed; history is Enter-inferred |
| Password echo masking | Not faked; Private Input documents real guarantees |
| Live process restore across restarts | Not supported (by design) |

Contributions and issues: use the GitHub repository. Keep secrets out of commits — `.gitignore` excludes keys, `.env`, logs, WebView2 data, and build outputs.

---

## License and acknowledgements

Runtime stack includes .NET 8, WPF, CommunityToolkit.Mvvm, Microsoft WebView2, SSH.NET, xterm.js, and Monaco Editor. See **Settings → About** inside the app for the installed version and a non-sensitive diagnostics snapshot.

---

*X-Terminal — enterprise terminal workstation for Windows.*
