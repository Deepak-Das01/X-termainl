# Integration testing (opt-in)

Live SSH/SFTP tests against an authorized private server are **optional** and never required by GitHub Actions.

## Rules

- Target only a server the developer explicitly authorizes.
- Supply host/user/fingerprint at runtime via ignored local config and/or environment variables.
- Supply passwords only via an in-process secure prompt (see script below) — never commit them.
- Live tests require **process-level** `XTERMINAL_IT_ENABLE=1`. A local file `enable` flag alone cannot turn them on.
- Respect centralized host-key verification; never bypass it. An expected SHA-256 fingerprint is **required**.
- Operate only inside uniquely named temporary remote directories created by the tests.
- Delete test artifacts afterward.
- Skipped / gated live tests are **not** counted as successful live validation.
- TCP/DNS probe success does **not** prove SSH authentication.

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
  "expectedHostKeyFingerprint": "SHA256:… or 64-char hex"
}
```

Do **not** put a password, passphrase, or other secret field in this file. The runner and gate reject such fields.

## Environment variables

| Variable | Purpose |
|----------|---------|
| `XTERMINAL_IT_ENABLE` | `1` required to execute live suite |
| `XTERMINAL_IT_HOST` | Host override |
| `XTERMINAL_IT_PORT` | Port (1–65535; invalid values fail validation) |
| `XTERMINAL_IT_USER` | Username |
| `XTERMINAL_IT_AUTH` | `Password` or `PrivateKey` |
| `XTERMINAL_IT_PASSWORD` | Process-only password (set by secure script; whitespace preserved) |
| `XTERMINAL_IT_KEY_PATH` | Private key path when applicable |
| `XTERMINAL_IT_HOSTKEY_SHA256` | Expected SHA-256 fingerprint (required) |
| `XTERMINAL_APPDATA` | Isolated app data root |

## Secure local runner

From the repository root, in an **interactive** PowerShell window (5.1 or 7+):

```powershell
powershell -NoProfile -File tools\run-live-integration.ps1
# or: pwsh -NoProfile -File tools\run-live-integration.ps1
```

The script:

1. Validates existing `%USERPROFILE%\.xterminal\integration.local.json` (does not overwrite a valid file).
2. Requires `enable: true` and a non-empty expected host-key fingerprint.
3. Prompts for a password without echoing it; preserves exact characters.
4. Sets process environment variables, runs `--filter Category=LiveIntegration`.
5. Restores prior environment values (including clearing the password) in `finally`.
6. Propagates the `dotnet test` exit code.
7. Writes sanitized TRX output under `artifacts/live-integration/` (gitignored).

## What the live suite covers

- Successful DNS+TCP probe (`FailureKind.None`) — not authentication.
- Authenticated SSH via production `SshTransportFactory` with host-key match.
- Remote command with a unique marker.
- Interactive shell session via production `SshSessionHost`.
- Host-key mismatch rejection (isolated trust store; no unconditional accept).
- SFTP lifecycle via production `SftpSessionService` (upload/download/hash/rename/chmod/cleanup).
- Loopback-only local port forward end-to-end.
- Diagnostics pipeline with sanitized export.

### Known live limitations

- Persistent remote shell-integration hooks (OSC 133 exit status / cwd from shell rc files) are **not** enabled by tests; tests may emit **session-local** OSC sequences only.
- Agent / keyboard-interactive auth methods are not supported by the live gate.
- Recursive folder transfer and rename conflict policy are covered only where the production conflict policy APIs are exercised in the SFTP lifecycle test.

## CI / default unit suite

```text
dotnet test --filter "Category!=LiveIntegration&Category!=LocalConPtyInteractive"
```

Private-server integration and environment-sensitive interactive ConPTY echo checks are excluded from GitHub Actions.
