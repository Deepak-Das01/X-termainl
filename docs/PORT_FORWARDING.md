# Port forwarding

X-Terminal supports SSH local, remote and dynamic (SOCKS5) forwards via SSH.NET.

## Ownership

| Kind | Ownership |
|------|-----------|
| Ports workspace tunnel | Dedicated SSH client or shared lease from `SshConnectionManager` |
| Session-embedded forwards | Owned by the SSH shell session client |

Stopping a Ports-workspace tunnel does not close unrelated terminal sessions. Closing a terminal tab does not stop Ports-workspace tunnels that hold their own client or lease.

Jump-host sessions keep exclusive ownership (no lease share across bastions) — see Phase 3A.

## Security

- Default bind address: loopback (`127.0.0.1`).
- Binding to non-loopback (`0.0.0.0` / Allow LAN) requires explicit confirmation.
- Remote forwards may still be constrained by `GatewayPorts` on the SSH server.
- Dynamic SOCKS listeners default to loopback; do not expose unauthenticated SOCKS to the LAN.
- Byte counters: **N/A** — SSH.NET does not expose trustworthy per-forward traffic counters.

## Runtime states

Stopped → Starting → Running → Stopping → Stopped  
Failures use **Failed** or **Disconnected**.

## Limitations

- No separate SOCKS authentication layer.
- Active connection counts are approximate (listener up ≠ verified destination).
- Reconnect-with-SSH does not silently adopt a different SSH identity.
