# Integration testing (opt-in)

Live SSH/SFTP tests against an authorized private server are **optional** and never run in GitHub Actions by default.

## Rules

- Target only a server the developer explicitly authorizes.
- Supply host/user credentials at runtime (environment variables or a local ignored file).
- Never commit hostnames, IPs, usernames, passwords, or private keys.
- Respect centralized host-key verification; never bypass it.
- Operate only inside a uniquely named temporary remote directory created by the test.
- Delete test artifacts afterward.

## Local configuration (not committed)

Create `%USERPROFILE%\.xterminal\integration.local.json` (gitignored patterns apply when placed under the repo as `integration.local.*`):

```json
{
  "host": "server.example.com",
  "port": 22,
  "username": "username",
  "auth": "Password"
}
```

Password/passphrase must come from Windows Credential Manager or a secure prompt — not from the JSON file in shared environments.

## Environment variables (optional)

| Variable | Purpose |
|----------|---------|
| `XTERMINAL_IT_ENABLE` | Set to `1` to enable opt-in tests |
| `XTERMINAL_IT_HOST` | Documentation-safe override host |
| `XTERMINAL_IT_PORT` | Port (default 22) |
| `XTERMINAL_IT_USER` | Username |

## CI

GitHub Actions runs unit/regression tests only. Private-server integration is excluded from CI.
