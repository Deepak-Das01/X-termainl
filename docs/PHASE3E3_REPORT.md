# Phase 3E.3 — Live integration hang and validation

Sanitized milestone report. No credentials, private hosts, fingerprints, or absolute developer paths.

## Prior live outcome

- Eight tests reported as passing.
- Two tests (`Live_shell_success_and_failure_commands_with_session_local_osc`, `Live_local_port_forward_loopback_end_to_end`) did not report completion before the run was cancelled.
- No final 10/10 summary.

## Confirmed root causes

### Production: SSH shell dispose deadlock

`SshSessionHost.DisconnectCoreAsync` awaited the shell read loop **before** disposing `ShellStream`. SSH.NET `ReadAsync` often ignores cancellation until the stream is closed, so dispose could wait indefinitely. Intentional disconnect also failed to suppress `AutoReconnect`, which could start a reconnect while tearing down.

**Fix:** dispose the shell first, await the read task with a bounded timeout, suppress AutoReconnect during intentional disconnect, and bound `SshClient.Disconnect`/`Dispose`.

### Production: port-forward disconnect hang

`PortForwardRuntimeService` teardown called `SshClient.Disconnect` without a timeout. SSH.NET can block while forwarded ports wind down.

**Fix:** bound disconnect/dispose (8s) and clear forward entry references after stop.

### Harness / environment

- Live sessions now set `AutoReconnect = false`.
- Live network tests share `[Collection("LiveIntegration")]` (serialized) to avoid authorized-server contention — not a substitute for the dispose fix.
- Shell/port tests use bounded `WaitAsync`/`ExpectAsync`, unsubscribe handlers, and bounded `DisposeAsync`.
- Runner adds `-Filter` and `--blame-hang` as a final safeguard.

## Live validation status

Confirmed on the authorized private server (developer interactive runner):

| Run | Result |
|-----|--------|
| `-Filter ShellOsc` | **1/1** passed (~2s) |
| `-Filter All` (`Category=LiveIntegration`) | **10/10** passed (~3.9s), exit code 0 |

Session-local OSC only — not a claim of full Bash/Zsh hook compatibility.

```powershell
powershell -NoProfile -File tools\run-live-integration.ps1
powershell -NoProfile -File tools\run-live-integration.ps1 -Filter ShellOsc
powershell -NoProfile -File tools\run-live-integration.ps1 -Filter PortForward
```
