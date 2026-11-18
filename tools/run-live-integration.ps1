# Opt-in live integration runner for X-Terminal (Phase 3E.1).
# Supports Windows PowerShell 5.1 and PowerShell 7+.
# Does NOT print or persist the password. Run in an interactive window.
# Usage:
#   powershell -NoProfile -File tools\run-live-integration.ps1
#   pwsh -NoProfile -File tools\run-live-integration.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

function Restore-EnvVar {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [AllowNull()]$PriorValue
  )
  if ($null -eq $PriorValue) {
    Remove-Item -LiteralPath "Env:$Name" -ErrorAction SilentlyContinue
  } else {
    Set-Item -LiteralPath "Env:$Name" -Value ([string]$PriorValue)
  }
}

function ConvertFrom-SecureStringPlain {
  param([Parameter(Mandatory = $true)][System.Security.SecureString]$Secure)
  $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
  try {
    # PtrToStringBSTR preserves exact Unicode content (including whitespace).
    return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
  } finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) | Out-Null
  }
}

$configDir = Join-Path $env:USERPROFILE '.xterminal'
$configPath = Join-Path $configDir 'integration.local.json'
$outDir = Join-Path $repo 'artifacts\live-integration'
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if (-not (Test-Path -LiteralPath $configPath)) {
  @'
{
  "enable": true,
  "host": "server.example.com",
  "port": 22,
  "username": "username",
  "auth": "Password",
  "expectedHostKeyFingerprint": ""
}
'@ | Set-Content -LiteralPath $configPath -Encoding UTF8
  Write-Host "Created template: $configPath"
  Write-Host "Edit host/username/expectedHostKeyFingerprint, set enable=true, then re-run."
  exit 2
}

# Do not overwrite a valid existing configuration.
try {
  $raw = Get-Content -LiteralPath $configPath -Raw -ErrorAction Stop
  $cfg = $raw | ConvertFrom-Json -ErrorAction Stop
} catch {
  Write-Host "ERROR: integration.local.json is malformed. Fix the file without putting secrets in it."
  exit 2
}

# Reject prohibited secret fields in the JSON text (case-insensitive property names).
if ($raw -match '(?i)"\s*(password|passphrase|secret|privateKey|private_key|credential|token|api[_-]?key)\s*"\s*:') {
  Write-Host "ERROR: configuration must not contain passwords, passphrases, or other secrets."
  exit 2
}

if ($null -eq $cfg.enable -or [bool]$cfg.enable -ne $true) {
  Write-Host "ERROR: local configuration enable flag is not true. Set `"enable`": true in:"
  Write-Host "  $configPath"
  exit 2
}

if (-not $cfg.host -or [string]$cfg.host -eq 'server.example.com' -or [string]::IsNullOrWhiteSpace([string]$cfg.host)) {
  Write-Host "ERROR: edit $configPath with your authorized test host before continuing."
  exit 2
}

if (-not $cfg.username -or [string]::IsNullOrWhiteSpace([string]$cfg.username)) {
  Write-Host "ERROR: username is required in $configPath"
  exit 2
}

$fp = $null
if ($cfg.PSObject.Properties.Name -contains 'expectedHostKeyFingerprint' -and $cfg.expectedHostKeyFingerprint) {
  $fp = [string]$cfg.expectedHostKeyFingerprint
} elseif ($cfg.PSObject.Properties.Name -contains 'expectedHostKeyFingerprintSha256' -and $cfg.expectedHostKeyFingerprintSha256) {
  $fp = [string]$cfg.expectedHostKeyFingerprintSha256
}

if ([string]::IsNullOrWhiteSpace($fp)) {
  Write-Host "ERROR: expectedHostKeyFingerprint (SHA-256) is required in $configPath"
  exit 2
}

$port = 22
if ($cfg.PSObject.Properties.Name -contains 'port' -and $null -ne $cfg.port -and [string]$cfg.port -ne '') {
  try {
    $port = [int]$cfg.port
  } catch {
    Write-Host "ERROR: port must be an integer between 1 and 65535."
    exit 2
  }
  if ($port -lt 1 -or $port -gt 65535) {
    Write-Host "ERROR: port must be an integer between 1 and 65535."
    exit 2
  }
}

$auth = 'Password'
if ($cfg.PSObject.Properties.Name -contains 'auth' -and $cfg.auth) {
  $auth = [string]$cfg.auth
} elseif ($cfg.PSObject.Properties.Name -contains 'authMethod' -and $cfg.authMethod) {
  $auth = [string]$cfg.authMethod
}

Write-Host "Config: $([string]$cfg.username)@$([string]$cfg.host):$port auth=$auth (fingerprint configured)"
Write-Host "Password will not be echoed."

$secure = $null
$plain = $null
$code = 1

# Snapshot prior env values for full restore.
$prior = @{
  XTERMINAL_IT_ENABLE         = $env:XTERMINAL_IT_ENABLE
  XTERMINAL_IT_HOST           = $env:XTERMINAL_IT_HOST
  XTERMINAL_IT_PORT           = $env:XTERMINAL_IT_PORT
  XTERMINAL_IT_USER           = $env:XTERMINAL_IT_USER
  XTERMINAL_IT_AUTH           = $env:XTERMINAL_IT_AUTH
  XTERMINAL_IT_PASSWORD       = $env:XTERMINAL_IT_PASSWORD
  XTERMINAL_IT_HOSTKEY_SHA256 = $env:XTERMINAL_IT_HOSTKEY_SHA256
  XTERMINAL_IT_KEY_PATH       = $env:XTERMINAL_IT_KEY_PATH
}

try {
  $secure = Read-Host -Prompt "SSH password for $($cfg.username)@$($cfg.host)" -AsSecureString
  $plain = ConvertFrom-SecureStringPlain -Secure $secure

  if ($null -eq $plain -or $plain.Length -eq 0) {
    Write-Host "ERROR: password is required."
    exit 2
  }

  $env:XTERMINAL_IT_ENABLE = '1'
  $env:XTERMINAL_IT_HOST = [string]$cfg.host
  $env:XTERMINAL_IT_PORT = [string]$port
  $env:XTERMINAL_IT_USER = [string]$cfg.username
  $env:XTERMINAL_IT_AUTH = $auth
  $env:XTERMINAL_IT_HOSTKEY_SHA256 = $fp
  $env:XTERMINAL_IT_PASSWORD = $plain

  if ($cfg.PSObject.Properties.Name -contains 'privateKeyPath' -and $cfg.privateKeyPath) {
    $env:XTERMINAL_IT_KEY_PATH = [string]$cfg.privateKeyPath
  }

  $trx = Join-Path $outDir ("live-" + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + ".trx")
  Write-Host "Running Category=LiveIntegration (detailed; secrets not logged)..."
  Write-Host "Results: $trx"

  & dotnet test "$repo\XTerminal.Tests\XTerminal.Tests.csproj" `
    -c Release `
    --filter "Category=LiveIntegration" `
    --logger "trx;LogFileName=$trx" `
    --logger "console;verbosity=detailed" `
    --results-directory $outDir

  $code = $LASTEXITCODE
  if ($null -eq $code) { $code = 1 }

  Write-Host "dotnet test exit code: $code"
  if ($code -eq 0) {
    Write-Host "NOTE: Only executed LiveIntegration tests count. Skipped/gated tests are not live passes."
  }
}
finally {
  # Clear password material first.
  if ($null -ne $plain) {
    $plain = $null
  }
  if ($null -ne $secure) {
    $secure.Dispose()
    $secure = $null
  }

  Restore-EnvVar -Name 'XTERMINAL_IT_PASSWORD' -PriorValue $prior.XTERMINAL_IT_PASSWORD
  Restore-EnvVar -Name 'XTERMINAL_IT_ENABLE' -PriorValue $prior.XTERMINAL_IT_ENABLE
  Restore-EnvVar -Name 'XTERMINAL_IT_HOST' -PriorValue $prior.XTERMINAL_IT_HOST
  Restore-EnvVar -Name 'XTERMINAL_IT_PORT' -PriorValue $prior.XTERMINAL_IT_PORT
  Restore-EnvVar -Name 'XTERMINAL_IT_USER' -PriorValue $prior.XTERMINAL_IT_USER
  Restore-EnvVar -Name 'XTERMINAL_IT_AUTH' -PriorValue $prior.XTERMINAL_IT_AUTH
  Restore-EnvVar -Name 'XTERMINAL_IT_HOSTKEY_SHA256' -PriorValue $prior.XTERMINAL_IT_HOSTKEY_SHA256
  Restore-EnvVar -Name 'XTERMINAL_IT_KEY_PATH' -PriorValue $prior.XTERMINAL_IT_KEY_PATH

  [GC]::Collect()
}

exit $code
