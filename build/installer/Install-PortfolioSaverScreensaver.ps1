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
param(
    [string]$StagingRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$migrationMarkerName = "DoNotPanicPortfolioVisualizer-migration-complete"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-NativeSystemDirectory {
    $system32Path = Join-Path $env:WINDIR "System32"
    $sysnativePath = Join-Path $env:WINDIR "Sysnative"

    if (-not [Environment]::Is64BitProcess -and [Environment]::Is64BitOperatingSystem -and (Test-Path $sysnativePath)) {
        return $sysnativePath
    }

    return $system32Path
}

function Copy-ToPersistentStagingRoot {
    $persistentRoot = Join-Path $env:TEMP ("PortfolioSaverScreensaverInstaller-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $persistentRoot | Out-Null

    foreach ($item in (Get-ChildItem -LiteralPath $PSScriptRoot -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $persistentRoot -Recurse -Force
    }

    return $persistentRoot
}

function Start-ElevatedInstall {
    $persistentRoot = Copy-ToPersistentStagingRoot
    $scriptPath = Join-Path $persistentRoot "Install-PortfolioSaverScreensaver.ps1"
    $arguments = "-ExecutionPolicy Bypass -File `"$scriptPath`" -StagingRoot `"$persistentRoot`""
    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $arguments | Out-Null
}

function Copy-LegacyLocalData {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$TargetRoot
    )

    $copyErrors = New-Object System.Collections.Generic.List[string]
    $sourceOpenFailed = -1
    $sourceIsReparsePoint = -2
    $enumerationFailed = -3
    $sourceWasEmpty = -4
    $itemsSeen = 0
    $SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot).TrimEnd([char[]]@('\', '/'))
    $TargetRoot = [System.IO.Path]::GetFullPath($TargetRoot).TrimEnd([char[]]@('\', '/'))
    if ([System.IO.Path]::GetPathRoot($SourceRoot).TrimEnd([char[]]@('\', '/')).Equals($SourceRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Warning "Legacy local data source is a drive or share root and will not be copied: $SourceRoot"
        return $sourceOpenFailed
    }

    $sourceRootPrefix = $SourceRoot + [System.IO.Path]::DirectorySeparatorChar
    $targetRootPrefix = $TargetRoot + [System.IO.Path]::DirectorySeparatorChar

    try {
        $sourceInfo = Get-Item -LiteralPath $SourceRoot -ErrorAction Stop
    }
    catch {
        Write-Warning "Legacy local data source could not be opened: ${SourceRoot}: $($_.Exception.Message)"
        return $sourceOpenFailed
    }

    if (($sourceInfo.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-Warning "Legacy local data root is a reparse point and will not be copied: $SourceRoot"
        return $sourceIsReparsePoint
    }

    if (-not $sourceInfo.PSIsContainer) {
        Write-Warning "Legacy local data source is not a directory and will not be copied: $SourceRoot"
        return $sourceOpenFailed
    }

    New-Item -ItemType Directory -Force -Path $TargetRoot | Out-Null
    $directoriesToVisit = New-Object System.Collections.Generic.Stack[string]
    $directoriesToVisit.Push($SourceRoot)

    while ($directoriesToVisit.Count -gt 0) {
        $currentDirectory = $directoriesToVisit.Pop()
        try {
            $children = @(Get-ChildItem -LiteralPath $currentDirectory -Force -ErrorAction Stop)
        }
        catch {
            Write-Warning "Legacy local data enumeration failed for ${currentDirectory}: $($_.Exception.Message)"
            $copyErrors.Add($currentDirectory)
            continue
        }

        foreach ($child in $children) {
            $itemsSeen++
            $sourceFullPath = [System.IO.Path]::GetFullPath($child.FullName)
            if (-not $sourceFullPath.StartsWith($sourceRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                $copyErrors.Add($child.FullName)
                Write-Warning "Legacy local data path escaped source root and was skipped: $($child.FullName)"
                continue
            }

            if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                Write-Warning "Skipping legacy local data reparse point: $($child.FullName)"
                continue
            }

            $relativePath = $sourceFullPath.Substring($SourceRoot.Length).TrimStart('\')
            $targetPath = Join-Path $TargetRoot $relativePath
            $targetFullPath = [System.IO.Path]::GetFullPath($targetPath)
            if (-not ($targetFullPath.Equals($TargetRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
                $targetFullPath.StartsWith($targetRootPrefix, [System.StringComparison]::OrdinalIgnoreCase))) {
                $copyErrors.Add($child.FullName)
                Write-Warning "Legacy local data path escaped target root and was skipped: $($child.FullName)"
                continue
            }

            if ($child.PSIsContainer) {
                try {
                    New-Item -ItemType Directory -Force -Path $targetFullPath | Out-Null
                    $directoriesToVisit.Push($child.FullName)
                }
                catch {
                    $copyErrors.Add($child.FullName)
                    Write-Warning "Legacy local data directory was not created: ${targetFullPath}: $($_.Exception.Message)"
                }
                continue
            }

            if (Test-Path $targetFullPath) {
                continue
            }

            try {
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $targetFullPath) | Out-Null
                Copy-Item -LiteralPath $child.FullName -Destination $targetFullPath -ErrorAction Stop
            }
            catch {
                $copyErrors.Add($child.FullName)
                Write-Warning "Legacy local data file was not copied: $($child.FullName): $($_.Exception.Message)"
            }
        }
    }

    if ($itemsSeen -eq 0) {
        return $sourceWasEmpty
    }

    return $copyErrors.Count
}

if (-not (Test-IsAdministrator)) {
    Write-Host "Requesting administrator rights to install the screensaver..."
    Start-ElevatedInstall
    exit 0
}

$sourceRoot = Join-Path $PSScriptRoot "payload"
if (-not (Test-Path $sourceRoot)) {
    throw "Installer payload folder not found: $sourceRoot"
}
$desktopPayloadPath = Join-Path $sourceRoot "PortfolioSaver.Desktop.exe"
$displayVersion = if (Test-Path $desktopPayloadPath) {
    try {
        [System.Diagnostics.FileVersionInfo]::GetVersionInfo($desktopPayloadPath).ProductVersion
    }
    catch {
        Write-Warning "Could not read product version from ${desktopPayloadPath}: $($_.Exception.Message)"
        "unknown"
    }
}
else {
    "unknown"
}

$installRoot = Get-NativeSystemDirectory
$installRootDisplay = Join-Path $env:WINDIR "System32"
$stateRoot = Join-Path $env:ProgramData "DoNotPanicPortfolioVisualizer"
$localDataRoot = Join-Path $env:LOCALAPPDATA "DoNotPanicPortfolioVisualizer"
$legacyLocalDataRoot = Join-Path $env:LOCALAPPDATA "PortfolioSaver"
$manifestPath = Join-Path $stateRoot "installed-files.txt"
$uninstallScriptTarget = Join-Path $stateRoot "Uninstall-PortfolioSaverScreensaver.ps1"
$uninstallRegistryKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DoNotPanicPortfolioVisualizer"
$legacyUninstallRegistryKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PortfolioSaverScreensaver"
$legacyMigrationCompleted = $false

New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
if ((Test-Path $legacyLocalDataRoot) -and -not (Test-Path (Join-Path $localDataRoot $migrationMarkerName))) {
    $copyErrorCount = Copy-LegacyLocalData -SourceRoot $legacyLocalDataRoot -TargetRoot $localDataRoot
    if ($copyErrorCount -eq 0) {
        $legacyMigrationCompleted = $true
        Write-Host "Copied legacy local data from $legacyLocalDataRoot to $localDataRoot"
    }
    elseif ($copyErrorCount -lt 0) {
        switch ($copyErrorCount) {
            -1 { Write-Warning "Legacy local data source could not be opened; legacy data was not migrated and will be preserved for retry." }
            -2 { Write-Warning "Legacy local data source is a reparse point; legacy data was not migrated and will be preserved." }
            -3 { Write-Warning "Legacy local data copy could not enumerate the source; legacy data was not migrated and will be preserved for retry." }
            -4 { Write-Warning "Legacy local data source is empty; no migration marker was written." }
            default { Write-Warning "Legacy local data copy failed; legacy data was not migrated and will be preserved for retry." }
        }
    }
    else {
        Write-Warning "Legacy local data copy skipped $copyErrorCount file(s); legacy data migration is incomplete and the source will be preserved for retry."
    }
}

foreach ($path in @(
    $localDataRoot,
    (Join-Path $localDataRoot "Trace"),
    (Join-Path $localDataRoot "Backgrounds\ExchangePhotoCache"),
    (Join-Path $localDataRoot "Caches\History")
)) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

$installedPaths = New-Object System.Collections.Generic.List[string]
$directories = Get-ChildItem $sourceRoot -Recurse -Directory | Sort-Object FullName
foreach ($directory in $directories) {
    $relativePath = $directory.FullName.Substring($sourceRoot.Length).TrimStart('\')
    $targetDirectory = if ([string]::IsNullOrWhiteSpace($relativePath)) { $installRoot } else { Join-Path $installRoot $relativePath }
    $manifestDirectory = if ([string]::IsNullOrWhiteSpace($relativePath)) { $installRootDisplay } else { Join-Path $installRootDisplay $relativePath }
    New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    $installedPaths.Add($manifestDirectory)
}

$files = Get-ChildItem $sourceRoot -Recurse -File | Sort-Object FullName
foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($sourceRoot.Length).TrimStart('\')
    $targetPath = Join-Path $installRoot $relativePath
    $manifestTargetPath = Join-Path $installRootDisplay $relativePath
    $targetDirectory = Split-Path -Parent $targetPath
    New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $targetPath -Force
    $installedPaths.Add($manifestTargetPath)
    Write-Host "Installed $relativePath"
}

$uninstallSource = Join-Path $PSScriptRoot "Uninstall-PortfolioSaverScreensaver.ps1"
Copy-Item -LiteralPath $uninstallSource -Destination $uninstallScriptTarget -Force
$installedPaths.Add($uninstallScriptTarget)

$installedPaths | Sort-Object -Unique | Set-Content -LiteralPath $manifestPath -Encoding ASCII

$uninstallCommand = "powershell.exe -ExecutionPolicy Bypass -File `"$uninstallScriptTarget`""
$screensaverPath = Join-Path $installRootDisplay "PortfolioSaver.Screensaver.scr"
New-Item -Path $uninstallRegistryKey -Force | Out-Null
Set-ItemProperty -Path $uninstallRegistryKey -Name "DisplayName" -Value "DO NOT PANIC PORTFOLIO VISUALIZER"
Set-ItemProperty -Path $uninstallRegistryKey -Name "Publisher" -Value "SANYALnet Labs"
Set-ItemProperty -Path $uninstallRegistryKey -Name "DisplayVersion" -Value $displayVersion
Set-ItemProperty -Path $uninstallRegistryKey -Name "InstallDate" -Value (Get-Date -Format "yyyyMMdd")
Set-ItemProperty -Path $uninstallRegistryKey -Name "InstallLocation" -Value $installRootDisplay
Set-ItemProperty -Path $uninstallRegistryKey -Name "DisplayIcon" -Value $screensaverPath
Set-ItemProperty -Path $uninstallRegistryKey -Name "UninstallString" -Value $uninstallCommand
Set-ItemProperty -Path $uninstallRegistryKey -Name "QuietUninstallString" -Value $uninstallCommand
Set-ItemProperty -Path $uninstallRegistryKey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallRegistryKey -Name "NoRepair" -Value 1 -Type DWord
if (Test-Path $legacyUninstallRegistryKey) {
    Write-Host "Removing legacy PortfolioSaver uninstall registry entry after installing the renamed product registration."
    Remove-Item -LiteralPath $legacyUninstallRegistryKey -Recurse -Force -ErrorAction SilentlyContinue
}
if ($legacyMigrationCompleted) {
    try {
        Set-Content -LiteralPath (Join-Path $localDataRoot $migrationMarkerName) -Value (Get-Date).ToString('o') -Encoding UTF8 -ErrorAction Stop
    }
    catch {
        Write-Warning "Install completed, but migration marker could not be written. Legacy migration may be retried on the next install: $($_.Exception.Message)"
    }
}

if (-not [string]::IsNullOrWhiteSpace($StagingRoot) -and (Test-Path $StagingRoot)) {
    $stagingFullPath = [System.IO.Path]::GetFullPath($StagingRoot)
    $tempFullPath = [System.IO.Path]::GetFullPath($env:TEMP)
    $cleanupAllowed = $stagingFullPath.StartsWith($tempFullPath, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $stagingFullPath).StartsWith("PortfolioSaverScreensaverInstaller-", [System.StringComparison]::OrdinalIgnoreCase)

    if (-not $cleanupAllowed) {
        Write-Host "Skipping staging cleanup for non-temporary path: $stagingFullPath"
    }
    else {
        # EncodedCommand keeps the cleanup path out of command-line parsing rules.
        $escapedStagingRoot = $StagingRoot.Replace("'", "''")
        $cleanupScript = "Start-Sleep -Seconds 5; Remove-Item -LiteralPath '$escapedStagingRoot' -Recurse -Force -ErrorAction SilentlyContinue"
        $encodedCleanupScript = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cleanupScript))
        Start-Process -FilePath "powershell.exe" -WindowStyle Hidden -ArgumentList "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCleanupScript" | Out-Null
    }
}

Write-Host ""
Write-Host "DO NOT PANIC PORTFOLIO VISUALIZER installed to $installRootDisplay"
Write-Host "Launch 'PortfolioSaver.Desktop.exe' for the primary desktop experience."
Write-Host "The screensaver component is available via Windows Screen Saver Settings as 'PortfolioSaver.Screensaver'."
Write-Host "To remove it later, run:"
Write-Host "powershell.exe -ExecutionPolicy Bypass -File `"$uninstallScriptTarget`""
