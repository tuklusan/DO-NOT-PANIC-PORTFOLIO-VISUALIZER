[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Start-ElevatedUninstall {
    $scriptPath = if (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
        $PSCommandPath
    }
    elseif (-not [string]::IsNullOrWhiteSpace($script:PSCommandPath)) {
        $script:PSCommandPath
    }
    else {
        $MyInvocation.MyCommand.Definition
    }
    $arguments = "-ExecutionPolicy Bypass -File `"$scriptPath`""
    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $arguments | Out-Null
}

function Stop-PortfolioSaverProcesses {
    $installedExecutables = @(
        (Join-Path $env:WINDIR "System32\PortfolioSaver.Screensaver.scr"),
        (Join-Path $env:WINDIR "System32\PortfolioSaver.Config.exe"),
        (Join-Path $env:WINDIR "System32\PortfolioSaver.Desktop.exe")
    )

    Get-Process PortfolioSaver.Screensaver,PortfolioSaver.Config,PortfolioSaver.Desktop -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    $candidates = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ExecutablePath -and ($installedExecutables -contains $_.ExecutablePath)
    }

    foreach ($candidate in $candidates) {
        Invoke-CimMethod -InputObject $candidate -MethodName Terminate -ErrorAction SilentlyContinue | Out-Null
    }

    Start-Sleep -Seconds 2
}

if (-not (Test-IsAdministrator)) {
    Write-Host "Requesting administrator rights to uninstall the screensaver..."
    Start-ElevatedUninstall
    exit 0
}

$stateRoot = Join-Path $env:ProgramData "PortfolioSaverScreensaver"
$manifestPath = Join-Path $stateRoot "installed-files.txt"
$uninstallRegistryKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PortfolioSaverScreensaver"
$localDataRoot = Join-Path $env:LOCALAPPDATA "PortfolioSaver"
$managedBackgroundCache = Join-Path $env:LOCALAPPDATA "PortfolioSaver\Backgrounds\ExchangePhotoCache"
$managedHistoryCache = Join-Path $env:LOCALAPPDATA "PortfolioSaver\Caches\History"
$symbolProfileCache = Join-Path $localDataRoot "symbol-profiles.json"
$providerBudgetLedger = Join-Path $localDataRoot "provider-query-usage.json"

Stop-PortfolioSaverProcesses

if (-not (Test-Path $manifestPath)) {
    throw "Install manifest not found: $manifestPath"
}

$paths = Get-Content -LiteralPath $manifestPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$files = $paths | Where-Object { Test-Path $_ -PathType Leaf } | Sort-Object Length -Descending
$directories = $paths | Where-Object { Test-Path $_ -PathType Container } | Sort-Object Length -Descending

foreach ($file in $files) {
    Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path $file)) {
        Write-Host "Removed file $file"
    }
    else {
        Write-Host "File still present after uninstall attempt: $file"
    }
}

foreach ($directory in $directories) {
    if ((Get-ChildItem -LiteralPath $directory -Force -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0) {
        Remove-Item -LiteralPath $directory -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path $directory)) {
            Write-Host "Removed directory $directory"
        }
        else {
            Write-Host "Directory still present after uninstall attempt: $directory"
        }
    }
}

Remove-Item -LiteralPath $manifestPath -Force -ErrorAction SilentlyContinue
if (Test-Path $stateRoot) {
    if ((Get-ChildItem -LiteralPath $stateRoot -Force -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0) {
        Remove-Item -LiteralPath $stateRoot -Force -ErrorAction SilentlyContinue
    }
}

if (Test-Path $uninstallRegistryKey) {
    Remove-Item -LiteralPath $uninstallRegistryKey -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path $managedBackgroundCache) {
    Remove-Item -LiteralPath $managedBackgroundCache -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path $managedBackgroundCache)) {
        Write-Host "Removed managed background cache $managedBackgroundCache"
    }
    else {
        Write-Host "Managed background cache is still present after uninstall attempt: $managedBackgroundCache"
    }
}

if (Test-Path $managedHistoryCache) {
    Remove-Item -LiteralPath $managedHistoryCache -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path $managedHistoryCache)) {
        Write-Host "Removed managed history cache $managedHistoryCache"
    }
    else {
        Write-Host "Managed history cache is still present after uninstall attempt: $managedHistoryCache"
    }
}

foreach ($derivedCache in @($symbolProfileCache, $providerBudgetLedger)) {
    if (Test-Path $derivedCache) {
        Remove-Item -LiteralPath $derivedCache -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path $derivedCache)) {
            Write-Host "Removed derived cache $derivedCache"
        }
        else {
            Write-Host "Derived cache is still present after uninstall attempt: $derivedCache"
        }
    }
}

if (Test-Path $localDataRoot) {
    $remaining = Get-ChildItem -LiteralPath $localDataRoot -Force -ErrorAction SilentlyContinue
    if (($remaining | Measure-Object).Count -eq 0) {
        Remove-Item -LiteralPath $localDataRoot -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "DO NOT PANIC PORTFOLIO VISUALIZER uninstall complete."
