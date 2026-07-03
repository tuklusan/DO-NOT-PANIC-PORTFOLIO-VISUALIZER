# ============================================================================
# Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
# Proprietary rights reserved except as expressly licensed herein.
#
# DO NOT PANIC PORTFOLIO VISUALIZER
# This file is governed by the SANYALnet Labs Non-Commercial License in the
# root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
# for AI/ML model training are prohibited unless separately authorized.
#
# Attribution is required: "Based on original work by Supratim Sanyal of
# SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
# patent, trademark, and governing-law provisions.
# ============================================================================
#requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$AllUsers
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Stop-ProductProcesses {
    $processes = @(Get-Process PortfolioSaver.Desktop,PortfolioSaver.Config,YFinance.NET.Server -ErrorAction SilentlyContinue)
    $serverHosts = @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -like '*\YFinanceServer\YFinance.NET.Server.dll*' -or
            $_.CommandLine -like '*/YFinanceServer/YFinance.NET.Server.dll*'
        })

    foreach ($process in $processes) {
        try {
            if ($process.MainWindowHandle -ne 0) {
                [void]$process.CloseMainWindow()
            }
        }
        catch {
            Write-Verbose "Graceful close request failed for process $($process.ProcessName): $($_.Exception.Message)"
        }
    }

    Start-Sleep -Seconds 2
    $processes |
        Where-Object { -not $_.HasExited } |
        Stop-Process -Force -ErrorAction SilentlyContinue

    foreach ($serverHost in $serverHosts) {
        try {
            Invoke-CimMethod -InputObject $serverHost -MethodName Terminate -ErrorAction SilentlyContinue | Out-Null
        }
        catch {
            Write-Verbose "YFinance.NET dotnet-host termination failed for pid $($serverHost.ProcessId): $($_.Exception.Message)"
        }
    }
}

function Test-IsSafeProfileLocalAppDataRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
        $leaf = Split-Path -Leaf $fullPath
        if (-not $leaf.Equals('DoNotPanicPortfolioVisualizer', [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }

        $parent = Split-Path -Parent $fullPath
        if ([string]::IsNullOrWhiteSpace($parent)) {
            return $false
        }

        $parentLeaf = Split-Path -Leaf $parent
        if (-not $parentLeaf.Equals('Local', [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }

        $appDataPath = Split-Path -Parent $parent
        if ([string]::IsNullOrWhiteSpace($appDataPath)) {
            return $false
        }

        return (Split-Path -Leaf $appDataPath).Equals('AppData', [StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        return $false
    }
}

function Remove-OwnedLocalAppDataRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-IsSafeProfileLocalAppDataRoot -Path $Path)) {
        Write-Warning "Skipping unsafe Local AppData cleanup path: $Path"
        return
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return
    }

    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-Warning "Skipping Local AppData cleanup reparse point: $Path"
        return
    }

    Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $Path) {
        Write-Warning "Product Local AppData root is still present after cleanup attempt: $Path"
    }
    else {
        Write-Host "Removed product Local AppData root: $Path"
    }
}

function Test-IsSafeProgramFilesInstallRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
        if (-not (Split-Path -Leaf $fullPath).Equals('DoNotPanicPortfolioVisualizer', [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }

        $publisherRoot = Split-Path -Parent $fullPath
        if ([string]::IsNullOrWhiteSpace($publisherRoot) -or
            -not (Split-Path -Leaf $publisherRoot).Equals('SANYALnet Labs', [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }

        $programFilesRoot = Split-Path -Parent $publisherRoot
        if ([string]::IsNullOrWhiteSpace($programFilesRoot)) {
            return $false
        }

        $allowedRoots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\', '/') }

        return $allowedRoots -contains ([IO.Path]::GetFullPath($programFilesRoot).TrimEnd('\', '/'))
    }
    catch {
        return $false
    }
}

function Start-DelayedInstallRootCleanup {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-IsSafeProgramFilesInstallRoot -Path $Path)) {
        Write-Warning "Skipping delayed install-root cleanup for unsafe path: $Path"
        return
    }

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -ne $item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-Warning "Skipping delayed install-root cleanup for reparse point: $Path"
        return
    }

    $expectedRoot = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'SANYALnet Labs\DoNotPanicPortfolioVisualizer')).TrimEnd('\', '/')
    $installRootLiteral = ConvertTo-Json -InputObject $Path -Compress
    $expectedRootLiteral = ConvertTo-Json -InputObject $expectedRoot -Compress
    $cleanupScript = @'
Set-StrictMode -Version Latest
$ErrorActionPreference = 'SilentlyContinue'

$InstallRoot = __INSTALL_ROOT__
$ExpectedRoot = __EXPECTED_ROOT__

Start-Sleep -Seconds 5
try {
    $normalizedInstallRoot = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\', '/')
    $normalizedExpectedRoot = [IO.Path]::GetFullPath($ExpectedRoot).TrimEnd('\', '/')
    if ($normalizedInstallRoot.Equals($normalizedExpectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $item = Get-Item -LiteralPath $InstallRoot -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
            $deadline = (Get-Date).AddSeconds(45)
            do {
                Remove-Item -LiteralPath $InstallRoot -Recurse -Force -ErrorAction SilentlyContinue
                if (-not (Test-Path -LiteralPath $InstallRoot)) {
                    break
                }

                Start-Sleep -Seconds 2
            } while ((Get-Date) -lt $deadline)
        }

        $publisherRoot = Split-Path -Parent $InstallRoot
        if ((Test-Path -LiteralPath $publisherRoot) -and -not (Get-ChildItem -LiteralPath $publisherRoot -Force -ErrorAction SilentlyContinue)) {
            Remove-Item -LiteralPath $publisherRoot -Force -ErrorAction SilentlyContinue
        }
    }
}
'@
    $cleanupScript = $cleanupScript.Replace('__INSTALL_ROOT__', $installRootLiteral).Replace('__EXPECTED_ROOT__', $expectedRootLiteral)
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cleanupScript))

    try {
        Start-Process -FilePath powershell.exe -WindowStyle Hidden -ArgumentList @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-EncodedCommand',
            $encodedCommand
        )
    }
    catch {
        Write-Warning "Could not schedule delayed install-root cleanup: $($_.Exception.Message)"
    }
}

function Get-ProductLocalAppDataRoots {
    $roots = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        [void]$roots.Add((Join-Path $env:LOCALAPPDATA 'DoNotPanicPortfolioVisualizer'))
    }

    if (-not $AllUsers) {
        return @($roots)
    }

    Write-Host 'All-users uninstall cleanup enabled; removing app-owned Local AppData roots for local user profiles reported by Windows.'
    try {
        $profiles = @(Get-CimInstance Win32_UserProfile -ErrorAction Stop | Where-Object { -not $_.Special -and -not [string]::IsNullOrWhiteSpace($_.LocalPath) })
    }
    catch {
        Write-Warning "Could not enumerate all Windows user profiles for cleanup; current-user cleanup will still run. $($_.Exception.Message)"
        return @($roots)
    }

    foreach ($profile in $profiles) {
        try {
            $profilePath = [IO.Path]::GetFullPath([string]$profile.LocalPath)
            if (-not (Test-Path -LiteralPath $profilePath -PathType Container)) {
                continue
            }

            [void]$roots.Add((Join-Path $profilePath 'AppData\Local\DoNotPanicPortfolioVisualizer'))
        }
        catch {
            Write-Warning "Skipping user profile during cleanup because it could not be inspected: $($profile.LocalPath): $($_.Exception.Message)"
        }
    }

    return @($roots)
}

Stop-ProductProcesses
foreach ($root in Get-ProductLocalAppDataRoots) {
    Remove-OwnedLocalAppDataRoot -Path $root
}

$installRoot = Split-Path -Parent $PSScriptRoot
Start-DelayedInstallRootCleanup -Path $installRoot

Write-Host 'DO NOT PANIC PORTFOLIO VISUALIZER uninstall cleanup complete.'

