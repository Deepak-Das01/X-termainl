# Integration testing (opt-in)

Live SSH/SFTP tests against an authorized private server are **optional** and never required by GitHub Actions.

## Rules

- Target only a server the developer explicitly authorizes.
- Supply host/user at runtime via ignored local config and/or environment variables.
- Supply passwords only via an in-process secure prompt (see script below) — never commit them.
- Respect centralized host-key verification; never bypass it.
- Operate only inside uniquely named temporary remote directories created by the tests.
- Delete test artifacts afterward.
- Skipped / gated live tests are **not** counted as successful live validation.

## Isolated application profile

To validate the GUI without touching production AppData:

```powershell
$iso = Join-Path $env:TEMP ("xterminal-it-" + [guid]::NewGuid().ToString("N"))
$env:XTERMINAL_APPDATA = $iso
# Or: Start-Process .\XTerminal.exe -ArgumentList "--appdata",$iso
```

## Local configuration (not committed)

Create `%USERPROFILE%\.xterminal\integration.local.json`:

```json
{
  "enable": true,
  "host": "server.example.com",
  "port": 22,
  "username": "username",
  "auth": "Password",
  "expectedHostKeyFingerprint": ""
}
```

Do **not** put a password field in this file.

## Environment variables

| Variable | Purpose |
|----------|---------|
| `XTERMINAL_IT_ENABLE` | `1` enables live suite |
| `XTERMINAL_IT_HOST` | Host override |
| `XTERMINAL_IT_PORT` | Port (default 22) |
| `XTERMINAL_IT_USER` | Username |
| `XTERMINAL_IT_AUTH` | `Password` or key auth label |
| `XTERMINAL_IT_PASSWORD` | Process-only password (set by secure script) |
| `XTERMINAL_IT_KEY_PATH` | Private key path when applicable |
| `XTERMINAL_IT_HOSTKEY_SHA256` | Expected fingerprint (optional) |
| `XTERMINAL_APPDATA` | Isolated app data root |

## Secure local runner

From the repository root, in an **interactive** PowerShell window:

```powershell
pwsh -NoProfile -File tools\run-live-integration.ps1
```

The script prompts for a password without echoing it, sets process environment variables, runs `--filter Category=LiveIntegration`, then clears the password from the environment.

## CI / default unit suite

```text
dotnet test --filter "Category!=LiveIntegration&Category!=LocalConPtyInteractive"
```

Private-server integration and environment-sensitive interactive ConPTY echo checks are excluded from GitHub Actions.
