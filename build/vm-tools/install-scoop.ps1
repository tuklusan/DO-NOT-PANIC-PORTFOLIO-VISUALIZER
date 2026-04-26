$ErrorActionPreference = 'Stop'

try {
    Set-ExecutionPolicy RemoteSigned -Scope CurrentUser -Force -ErrorAction Stop
} catch {
    Write-Host "Execution policy update skipped: $($_.Exception.Message)"
}

$installerPath = Join-Path $env:TEMP 'install-scoop.ps1'
Invoke-WebRequest -UseBasicParsing -Uri 'https://get.scoop.sh' -OutFile $installerPath

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).
    IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)

if ($isAdmin) {
    & $installerPath -RunAsAdmin
} else {
    & $installerPath
}

$scoopPath = Join-Path $env:USERPROFILE 'scoop\shims'
if (Test-Path -LiteralPath $scoopPath) {
    $env:Path += ";$scoopPath"
}

scoop --version
