# Validation matrix (Phase 4B)

Legend: **Unit** · **Protocol live** · **GUI** · **E2E** · **Not executed** · **Unsupported**

| Area | Unit | Protocol live | GUI | Notes |
|------|------|---------------|-----|-------|
| Prior Phase 4A Serial/Telnet | Yes | Telnet loopback | Partial | Serial hardware outstanding |
| RDP profiles / security defaults | Yes | Not executed | Not executed | ActiveX availability probed |
| RDP embedded ActiveX | Partial | Not executed | Not executed | Requires MsRdpClient9 |
| VNC RFB client | Yes | `VncLoopback` | Not executed | Raw + CopyRect |
| VNC security policy | Yes | Loopback None+ack | Not executed | |
| Graphical workspace tabs | Yes | — | Not executed | Restore disconnected |
| SSH/SFTP live | Gate | Opt-in 10 | — | Prior Phase 3E.3 |
| RDP/VNC/X11 terminal hosts | Gated | — | — | Graphical path for RDP/VNC |

Standard filtered suite (Phase 4B): **207** passed  
(`Category!=LiveIntegration&Category!=LocalConPtyInteractive&Category!=SerialLiveIntegration`).
