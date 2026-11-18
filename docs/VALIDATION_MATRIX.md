# Validation matrix (Phase 3E)

Legend: **Unit** · **Protocol live** · **GUI** · **E2E** · **Not executed** · **Unsupported**

| Area | Unit | Protocol live | GUI | E2E | Notes |
|------|------|---------------|-----|-----|-------|
| Local PowerShell ConPTY | Yes | Yes (smoke) | Not executed | Not executed | Marker echo smoke added |
| Local CMD | Partial | Not executed | Not executed | Not executed | |
| Local WSL | Partial | Not executed | Not executed | Not executed | Depends on install |
| SSH handshake / host key | Yes | Blocked* | Not executed | Not executed | *Needs opt-in credentials |
| SSH interactive shell | Yes (mocks) | Blocked* | Not executed | Not executed | |
| SFTP transfers | Yes | Blocked* | Not executed | Not executed | |
| Remote Monaco editor | Yes (helpers) | Blocked* | Not executed | Not executed | |
| Shell integration OSC parser | Yes | Blocked* | Not executed | Not executed | |
| Terminal↔SFTP CWD sync | Yes | Blocked* | Not executed | Not executed | |
| Port forwarding | Yes (validation) | Blocked* | Not executed | Not executed | |
| Diagnostics | Yes | Blocked* | Not executed | Not executed | |
| Workspace restore | Yes | N/A | Not executed | Not executed | |
| Keybindings | Yes | N/A | Not executed | Not executed | |
| Broadcast / Private Input | Yes | N/A | Not executed | Not executed | |
| Unsupported protocols | Gated | Unsupported | N/A | N/A | RDP/VNC/Telnet/Serial/X11 |

\* Live private-server testing requires `tools/run-live-integration.ps1` with authorized host metadata. Until that runs successfully, live columns remain **Blocked / Not executed**.
