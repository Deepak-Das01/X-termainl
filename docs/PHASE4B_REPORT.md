# Phase 4B report — RDP and VNC remote desktop

## Summary

Graphical remote desktop sessions are integrated as first-class workspace tabs (`DesktopTabViewModel`), separate from the terminal byte-stream pipeline. RDP uses Windows MsRdpClient9 ActiveX; VNC uses a first-party bounded RFB client with loopback tests.

## Architecture

See [`REMOTE_DESKTOP_ARCHITECTURE.md`](REMOTE_DESKTOP_ARCHITECTURE.md).

## Validation (this checkpoint)

| Suite | Result |
|-------|--------|
| Release build | 0 errors |
| Filtered automated | **207** passed |
| VNC loopback | Included in filtered suite |
| RDP live | **Not executed** |
| VNC live (external) | **Not executed** |
| SSH/SFTP live 10 | Discoverable; not re-run |
| Telnet loopback 5 | Preserved |

## Remaining limitations

WPF airspace over RDP HWND; no graphical split panes; VNC encodings limited to Raw/CopyRect; no TLS VNC; live graphical endpoints not assumed.
