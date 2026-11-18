# Validation matrix (Phase 4C)

Legend: **Unit** · **Protocol live** · **GUI** · **E2E** · **Not executed** · **Unsupported** · **Blocked**

| Area | Unit | Protocol live | GUI | Notes |
|------|------|---------------|-----|-------|
| Prior Phase 4A Serial/Telnet | Yes | Telnet loopback | Partial | Serial hardware outstanding |
| RDP profiles / security defaults | Yes | Not executed | Partial | ActiveX availability probed |
| RDP embedded ActiveX | Partial | Not executed | Partial | Requires MsRdpClient9 |
| VNC RFB client | Yes | `VncLoopback` | Partial | Raw + CopyRect; hardened bounds |
| VNC SSH tunnel ownership | Yes (validator) | Not executed | Partial | Requires SSH profile + runtime |
| VNC security policy | Yes | Loopback None+ack | Partial | |
| X11 preferences / gate | Yes | Blocked | Partial | No x11-req backend |
| X11 cookie / setup parser | Yes | Synthetic only | — | Not live forwarding |
| X server manager | Yes | — | Partial | BYO; no auto-install |
| Graphical workspace tabs | Yes | — | Partial | Restore disconnected |
| SSH/SFTP live | Gate | Opt-in 10 | — | Re-run with secure runner |
| Native VNC TLS | Unsupported | — | — | |

Standard filtered suite (Phase 4C): **232** passed  
(`Category!=LiveIntegration&Category!=LocalConPtyInteractive&Category!=SerialLiveIntegration`).
