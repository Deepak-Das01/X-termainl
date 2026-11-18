# Validation matrix (Phase 3E.1)

Legend: **Unit** · **Protocol live** · **GUI** · **E2E** · **Not executed** · **Unsupported**

| Area | Unit | Protocol live | GUI | E2E | Notes |
|------|------|---------------|-----|-----|-------|
| Local PowerShell ConPTY | Yes | Yes (smoke) | Not executed | Not executed | Connect/dispose smoke |
| Local CMD | Partial | Not executed | Not executed | Not executed | |
| Local WSL | Partial | Not executed | Not executed | Not executed | Depends on install |
| SSH handshake / host key | Yes | Opt-in suite | Not executed | Not executed | Production `SshTransportFactory` |
| SSH remote command | Yes (gate) | Opt-in suite | Not executed | Not executed | Unique marker |
| SSH interactive shell | Yes (mocks) | Opt-in suite | Not executed | Not executed | Production `SshSessionHost` |
| Host-key mismatch reject | Yes | Opt-in suite | N/A | N/A | Isolated known_hosts |
| SFTP transfers | Yes | Opt-in suite | Not executed | Not executed | Production `SftpSessionService` |
| Remote Monaco editor | Yes (helpers) | Not executed | Not executed | Not executed | |
| Shell integration OSC parser | Yes | Opt-in (session-local OSC) | Not executed | Not executed | No remote rc mutation |
| Terminal↔SFTP CWD sync | Yes | Not executed | Not executed | Not executed | |
| Port forwarding | Yes (validation) | Opt-in suite | Not executed | Not executed | Loopback-only |
| Diagnostics | Yes | Opt-in suite | Not executed | Not executed | Sanitized export |
| Live integration gate | Yes | N/A | N/A | N/A | Process opt-in required |
| Probe failure classification | Yes | N/A | N/A | N/A | DNS/refused/timeout/network |
| Workspace restore | Yes | N/A | Not executed | Not executed | |
| Keybindings | Yes | N/A | Not executed | Not executed | |
| Broadcast / Private Input | Yes | N/A | Not executed | Not executed | |
| Unsupported protocols | Gated | Unsupported | N/A | N/A | RDP/VNC/Telnet/Serial/X11 |

Live private-server rows require a successful interactive run of `tools/run-live-integration.ps1`. Until that run succeeds on the developer machine, treat protocol-live results as **implemented but not yet executed in-session**.
