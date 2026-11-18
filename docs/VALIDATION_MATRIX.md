# Validation matrix (Phase 4A)

Legend: **Unit** · **Protocol live** · **GUI** · **E2E** · **Not executed** · **Unsupported**

| Area | Unit | Protocol live | GUI | E2E | Notes |
|------|------|---------------|-----|-----|-------|
| Local PowerShell ConPTY | Yes | Yes (smoke) | Sanity | Not executed | |
| SSH / SFTP / shell / forward | Yes | Opt-in 10 tests | Not executed this checkpoint | — | Prior Phase 3E.3: 10/10 |
| Serial profile validation | Yes | — | Sanity | — | Rejects invalid configs |
| Serial port discovery | Yes (abstraction) | Optional hardware | Sanity | — | No open-to-probe |
| SerialSessionHost | Yes (ownership/hex/caps) | Optional `SerialLiveIntegration` | Sanity | — | Excluded from CI filter |
| Telnet negotiation | Yes | Loopback | Sanity | — | IAC / options / loops |
| TelnetSessionHost | Yes | `TelnetLoopback` (5) | Sanity | — | Production host |
| Telnet security policy | Yes | — | Sanity | — | Per-host ack |
| Capability gating | Yes | — | Sanity | — | No SFTP/tunnels on Serial/Telnet |
| Workspace restore | Yes (fields) | — | Sanity | — | Serial never auto-open |
| RDP / VNC / X11 | Gated | Unsupported | N/A | N/A | |

Standard filtered suite (Phase 4A): **190** passed  
(`Category!=LiveIntegration&Category!=LocalConPtyInteractive&Category!=SerialLiveIntegration`).

SSH/SFTP live suite: **discoverable 10**; not re-executed against this checkpoint unless the developer runs `tools/run-live-integration.ps1`.

Serial hardware live: **not executed** unless `XTERMINAL_SERIAL_PORT_A/B` set.
