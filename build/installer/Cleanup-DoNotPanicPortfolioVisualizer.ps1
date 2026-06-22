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
#requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$AllUsers
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Stop-ProductProcesses {
    $processes = @(Get-Process PortfolioSaver.Desktop,PortfolioSaver.Config,PortfolioSaver.Screensaver,YFinance.NET.Server -ErrorAction SilentlyContinue)
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

Write-Host 'DO NOT PANIC PORTFOLIO VISUALIZER uninstall cleanup complete.'
