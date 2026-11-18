# Telnet protocol

X-Terminal Phase 4A implements a real Telnet client (`TelnetSessionHost` + `TelnetNegotiationEngine`), not raw TCP.

## Security

Telnet is **plaintext and unencrypted**. Network observers can read terminal data, including credentials typed at login prompts.

Controls:

- Prominent pre-connect warning (`TelnetSecurityPolicy`)
- Per-host acknowledgment store (`telnet-ack.json`) — not shared across unrelated hosts
- No SSH credential reuse; `user@host` targets are rejected at validation
- Reconnect is **opt-in** per profile (`AutoReconnect`)
- Status bar labels connections as unencrypted
- Prefer SSH when available

Private Input Mode does **not** encrypt network traffic.

## Supported negotiation options

| Option | Behavior |
|--------|----------|
| ECHO | Accept remote WILL; client typically WONT local echo |
| SUPPRESS-GO-AHEAD | DO/WILL as appropriate |
| TERMINAL-TYPE | WILL + SEND/IS subnegotiation |
| NAWS | WILL + width/height subnegotiation on resize |
| BINARY | Accepted when offered |
| Others | Rejected with DONT/WONT |

Loop prevention: max negotiations per option. Subnegotiation buffer bounded (2048 bytes). Escaped `0xFF` (`IAC IAC`) handled for application data.

## Connection behavior

- TCP connect with timeout
- Initial client desires: DO SGA, WILL TERMINAL-TYPE, WILL NAWS, DO ECHO
- Application bytes separated from IAC negotiation
- Cancellation, remote close, DNS/refused/timeout classification
- Idempotent dispose

## Loopback integration tests

`Category=TelnetLoopback` starts an ephemeral `127.0.0.1` listener (OS-assigned port), exercises production `TelnetSessionHost`, and cleans up. No OS Telnet service is installed. Included in the standard filtered suite.

## Limitations

- Not all Telnet servers behave identically.
- Many RFC option extensions are intentionally unsupported.
- Do not use Telnet across untrusted networks.
