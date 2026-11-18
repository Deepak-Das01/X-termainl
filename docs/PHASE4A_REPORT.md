# Phase 4A report — Serial console and Telnet

## Executive summary

Phase 4A delivers first-class **Serial** and **Telnet** terminal sessions integrated with the existing X-Terminal host factory, xterm.js pipeline, profiles, workspace restore, command palette, and capability gating. RDP/VNC/X11 remain gated.

## Protocol architecture

`SessionCapabilities` distinguishes unsupported features (SFTP, tunnels, etc.) from temporary failures. `SessionHostFactory` creates `SerialSessionHost` / `TelnetSessionHost` for supported kinds.

## Serial

- Validated profiles, COM discovery without opening ports, exclusive ownership, async read with close-to-cancel, hex send, BREAK, DTR/RTS.
- UI: `SerialConnectWindow`; toolbar + palette commands.
- Workspace: restore disconnected only.

## Telnet

- Incremental IAC engine (WILL/WONT/DO/DONT/SB), escaped IAC, bounded SB, loop caps.
- Options: ECHO, SGA, TERMINAL-TYPE, NAWS, BINARY; others rejected.
- Security: plaintext warning, per-host ack, no SSH credential reuse, reconnect opt-in.
- Loopback integration against ephemeral `127.0.0.1` listener.

## Validation (this checkpoint)

| Suite | Result |
|-------|--------|
| Release build | 0 errors / 0 warnings |
| Filtered automated | **190** passed |
| Telnet loopback | **5** (included above) |
| Serial hardware live | **Not executed** (ports unset) |
| SSH/SFTP live 10 | Discoverable; **not re-run** this checkpoint |
| GUI sanity | See final implementation report |

## Remaining limitations

See [`KNOWN_LIMITATIONS.md`](KNOWN_LIMITATIONS.md). Phase 4B candidates: richer serial device metadata, additional Telnet options, RDP/VNC design — **not started**.
