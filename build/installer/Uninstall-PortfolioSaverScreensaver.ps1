# ============================================================================
# Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
# Proprietary rights reserved except as expressly licensed herein.
#
# DO NOT PANIC PORTFOLIO VIEWER
# This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
# personal, educational, or hobbyist use only. Commercial exploitation,
# corporate internal operations, or AI model training are strictly forbidden.
#
# ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
# which is licensed under the Apache License, Version 2.0. A copy of the Apache
# License is provided within the distribution environment.
#
# FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
# It does not provide financial, investment, legal, or tax advice. All data
# calculation and scraping outputs are provided 'AS IS' with zero guarantee
# of real-time accuracy or upstream availability.
#
# This file is subject to the terms and conditions defined in the LICENSE
# file located in the root directory of this source code repository.
# Removal or modification of this legal notice constitutes copyright infringement.
# ============================================================================
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$migrationMarkerName = "DoNotPanicPortfolioVisualizer-migration-complete"

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

function Get-NativeSystemDirectory {
    $system32Path = Join-Path $env:WINDIR "System32"
    $sysnativePath = Join-Path $env:WINDIR "Sysnative"

    if (-not [Environment]::Is64BitProcess -and [Environment]::Is64BitOperatingSystem -and (Test-Path $sysnativePath)) {
        return $sysnativePath
    }

    return $system32Path
}

function Stop-PortfolioSaverProcesses {
    $installRoot = Get-NativeSystemDirectory
    $installedExecutables = @(
        (Join-Path $installRoot "PortfolioSaver.Screensaver.scr"),
        (Join-Path $installRoot "PortfolioSaver.Config.exe"),
        (Join-Path $installRoot "PortfolioSaver.Desktop.exe")
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

function Convert-ManifestPathToNativePath {
    param([Parameter(Mandatory = $true)][string]$ManifestPath)

    if (-not [System.IO.Path]::IsPathRooted($ManifestPath)) {
        throw "Install manifest contains a relative path, which cannot be safely removed: $ManifestPath"
    }

    try {
        $displaySystem32 = [System.IO.Path]::GetFullPath((Join-Path $env:WINDIR "System32")).TrimEnd('\')
        $sysnativeSystem32 = [System.IO.Path]::GetFullPath((Join-Path $env:WINDIR "Sysnative")).TrimEnd('\')
        $nativeSystemDirectory = [System.IO.Path]::GetFullPath((Get-NativeSystemDirectory)).TrimEnd('\')
        $fullPath = [System.IO.Path]::GetFullPath($ManifestPath)
    }
    catch {
        throw "Install manifest contains an invalid path, which cannot be safely removed: ${ManifestPath}: $($_.Exception.Message)"
    }

    $displayPrefix = $displaySystem32 + [System.IO.Path]::DirectorySeparatorChar
    $sysnativePrefix = $sysnativeSystem32 + [System.IO.Path]::DirectorySeparatorChar

    if ($fullPath.StartsWith($sysnativePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return Join-Path $nativeSystemDirectory $fullPath.Substring($sysnativePrefix.Length)
    }

    if (-not $displaySystem32.Equals($nativeSystemDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
        if ($fullPath.StartsWith($displayPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return Join-Path $nativeSystemDirectory $fullPath.Substring($displayPrefix.Length)
        }
    }

    return $ManifestPath
}

function Test-IsPathUnderRoot {
    param(
        [Parameter(Mandatory = $true)][string]$CandidatePath,
        [Parameter(Mandatory = $true)][string]$RootPath
    )

    $candidateFullPath = [System.IO.Path]::GetFullPath($CandidatePath)
    $rootFullPath = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\', '/')
    $rootPrefix = $rootFullPath + [System.IO.Path]::DirectorySeparatorChar

    return $candidateFullPath.Equals($rootFullPath, [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidateFullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-IsOwnedInstallPath {
    param(
        [Parameter(Mandatory = $true)][string]$CandidatePath,
        [Parameter(Mandatory = $true)][string[]]$AllowedRoots
    )

    foreach ($allowedRoot in $AllowedRoots) {
        if (Test-IsPathUnderRoot -CandidatePath $CandidatePath -RootPath $allowedRoot) {
            return $true
        }
    }

    return $false
}

function Test-IsOwnedRootPath {
    param(
        [Parameter(Mandatory = $true)][string]$CandidatePath,
        [Parameter(Mandatory = $true)][string[]]$AllowedRoots
    )

    $candidateFullPath = [System.IO.Path]::GetFullPath($CandidatePath).TrimEnd('\', '/')
    foreach ($allowedRoot in $AllowedRoots) {
        $allowedFullPath = [System.IO.Path]::GetFullPath($allowedRoot).TrimEnd('\', '/')
        if ($candidateFullPath.Equals($allowedFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Remove-ManagedDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path $Path)) {
        return
    }

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return
    }

    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-Warning "Skipping managed directory reparse point during uninstall: $Path"
        return
    }

    Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $Path) {
        Write-Host "Managed directory is still present after uninstall attempt: $Path"
    }
}

function Remove-EmptyManagedDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path $Path)) {
        return
    }

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return
    }

    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-Warning "Skipping managed parent reparse point during uninstall: $Path"
        return
    }

    $remaining = Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if (($remaining | Measure-Object).Count -eq 0) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-IsAdministrator)) {
    Write-Host "Requesting administrator rights to uninstall the screensaver..."
    Start-ElevatedUninstall
    exit 0
}

$stateRoot = Join-Path $env:ProgramData "DoNotPanicPortfolioVisualizer"
$legacyStateRoot = Join-Path $env:ProgramData "PortfolioSaverScreensaver"
$manifestPath = Join-Path $stateRoot "installed-files.txt"
$legacyManifestPath = Join-Path $legacyStateRoot "installed-files.txt"
$uninstallRegistryKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DoNotPanicPortfolioVisualizer"
$legacyUninstallRegistryKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PortfolioSaverScreensaver"
$localDataRoot = Join-Path $env:LOCALAPPDATA "DoNotPanicPortfolioVisualizer"
$legacyLocalDataRoot = Join-Path $env:LOCALAPPDATA "PortfolioSaver"
$managedBackgroundCache = Join-Path $localDataRoot "Backgrounds\ExchangePhotoCache"
$managedHistoryCache = Join-Path $localDataRoot "Caches\History"
$managedTraceRoot = Join-Path $localDataRoot "Trace"
$symbolProfileCache = Join-Path $localDataRoot "symbol-profiles.json"
$providerBudgetLedger = Join-Path $localDataRoot "provider-query-usage.json"
$ownedInstallRoots = @((Get-NativeSystemDirectory), $stateRoot, $legacyStateRoot)

Stop-PortfolioSaverProcesses

$manifestPaths = @($manifestPath, $legacyManifestPath) | Where-Object { Test-Path $_ }
if (($manifestPaths | Measure-Object).Count -eq 0) {
    throw "No install manifest found at '$manifestPath' or '$legacyManifestPath'. Uninstall cannot guarantee full payload removal."
}
$paths = $manifestPaths |
    ForEach-Object {
        Write-Host "Reading install manifest $_"
        Get-Content -LiteralPath $_ -ErrorAction Stop
    } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { Convert-ManifestPathToNativePath -ManifestPath $_ } |
    Where-Object {
        if (Test-IsOwnedInstallPath -CandidatePath $_ -AllowedRoots $ownedInstallRoots) {
            return $true
        }

        Write-Warning "Skipping install manifest path outside owned roots: $_"
        return $false
    } |
    Sort-Object -Unique
$files = $paths | Where-Object { Test-Path $_ -PathType Leaf } | Sort-Object Length -Descending
$directories = $paths | Where-Object { Test-Path $_ -PathType Container } | Sort-Object {
    ([System.IO.Path]::GetFullPath($_).TrimEnd([char[]]@('\', '/')) -split '[\\/]').Count
} -Descending

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
    if (Test-IsOwnedRootPath -CandidatePath $directory -AllowedRoots $ownedInstallRoots) {
        Write-Warning "Skipping manifest root directory entry during uninstall: $directory"
        continue
    }

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

foreach ($installManifestPath in @($manifestPath, $legacyManifestPath)) {
    Remove-Item -LiteralPath $installManifestPath -Force -ErrorAction SilentlyContinue
}
if (Test-Path $stateRoot) {
    if ((Get-ChildItem -LiteralPath $stateRoot -Force -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0) {
        Remove-Item -LiteralPath $stateRoot -Force -ErrorAction SilentlyContinue
    }
}

if (Test-Path $uninstallRegistryKey) {
    Remove-Item -LiteralPath $uninstallRegistryKey -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path $legacyUninstallRegistryKey) {
    Remove-Item -LiteralPath $legacyUninstallRegistryKey -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path $managedBackgroundCache) {
    Remove-ManagedDirectory -Path $managedBackgroundCache
    if (-not (Test-Path $managedBackgroundCache)) {
        Write-Host "Removed managed background cache $managedBackgroundCache"
    }
    else {
        Write-Host "Managed background cache is still present after uninstall attempt: $managedBackgroundCache"
    }
}

if (Test-Path $managedHistoryCache) {
    Remove-ManagedDirectory -Path $managedHistoryCache
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

Remove-ManagedDirectory -Path $managedTraceRoot
Remove-EmptyManagedDirectory -Path (Join-Path $localDataRoot "Backgrounds")
Remove-EmptyManagedDirectory -Path (Join-Path $localDataRoot "Caches")

Remove-Item -LiteralPath (Join-Path $localDataRoot $migrationMarkerName) -Force -ErrorAction SilentlyContinue

if (Test-Path $legacyLocalDataRoot) {
    Write-Host "Legacy local data preserved for safety: $legacyLocalDataRoot"
}

if (Test-Path $localDataRoot) {
    $remaining = Get-ChildItem -LiteralPath $localDataRoot -Force -ErrorAction SilentlyContinue
    if (($remaining | Measure-Object).Count -eq 0) {
        Remove-Item -LiteralPath $localDataRoot -Force -ErrorAction SilentlyContinue
    }
}

if (Test-Path $legacyStateRoot) {
    $remainingLegacyState = Get-ChildItem -LiteralPath $legacyStateRoot -Force -ErrorAction SilentlyContinue
    if (($remainingLegacyState | Measure-Object).Count -eq 0) {
        Remove-Item -LiteralPath $legacyStateRoot -Force -ErrorAction SilentlyContinue
    }
    else {
        Write-Host "Legacy installer state root preserved because it still contains untracked files: $legacyStateRoot"
    }
}

Write-Host "DO NOT PANIC PORTFOLIO VISUALIZER uninstall complete."
