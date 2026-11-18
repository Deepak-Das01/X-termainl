# Opt-in live integration runner for X-Terminal Phase 3E.
# Does NOT print or persist the password. Run manually in an interactive PowerShell window.
# Usage:
#   pwsh -NoProfile -File tools\run-live-integration.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$configDir = Join-Path $env:USERPROFILE '.xterminal'
$configPath = Join-Path $configDir 'integration.local.json'
New-Item -ItemType Directory -Force -Path $configDir | Out-Null

if (-not (Test-Path $configPath)) {
  @'
{
  "enable": true,
  "host": "server.example.com",
  "port": 22,
  "username": "username",
  "auth": "Password",
  "expectedHostKeyFingerprint": ""
}
'@ | Set-Content -Path $configPath -Encoding UTF8
  Write-Host "Created template: $configPath"
  Write-Host "Edit host/username/fingerprint, then re-run this script."
  exit 2
}

$cfg = Get-Content $configPath -Raw | ConvertFrom-Json
if (-not $cfg.host -or $cfg.host -eq 'server.example.com') {
  Write-Host "Edit $configPath with your authorized test host before continuing."
  exit 2
}

$secure = Read-Host -Prompt "SSH password for $($cfg.username)@$($cfg.host) (input hidden)" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
  $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
} finally {
  [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) | Out-Null
}

$env:XTERMINAL_IT_ENABLE = '1'
$env:XTERMINAL_IT_HOST = [string]$cfg.host
$env:XTERMINAL_IT_PORT = [string]($(if ($cfg.port) { $cfg.port } else { 22 }))
$env:XTERMINAL_IT_USER = [string]$cfg.username
$env:XTERMINAL_IT_AUTH = [string]($(if ($cfg.auth) { $cfg.auth } else { 'Password' }))
if ($cfg.expectedHostKeyFingerprint) {
  $env:XTERMINAL_IT_HOSTKEY_SHA256 = [string]$cfg.expectedHostKeyFingerprint
}
$env:XTERMINAL_IT_PASSWORD = $plain

try {
  Write-Host "Running LiveIntegration tests (password not echoed)..."
  dotnet test "$repo\XTerminal.Tests\XTerminal.Tests.csproj" -c Release --filter "Category=LiveIntegration"
  $code = $LASTEXITCODE
} finally {
  Remove-Item Env:XTERMINAL_IT_PASSWORD -ErrorAction SilentlyContinue
  $plain = $null
  [GC]::Collect()
}

exit $code
