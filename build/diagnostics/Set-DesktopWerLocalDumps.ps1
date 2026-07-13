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

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('Enable', 'Disable', 'Show')]
    [string]$Mode = 'Show',

    [ValidateRange(1, 50)]
    [int]$DumpCount = 10,

    [ValidateSet('Custom', 'Full')]
    [string]$DumpType = 'Full',

    [string]$DumpFolder = (Join-Path $env:LOCALAPPDATA 'DoNotPanicPortfolioVisualizer\CrashDumps'),

    [switch]$AcknowledgePrivateDumpContents
)

$ErrorActionPreference = 'Stop'

$executableName = 'PortfolioSaver.Desktop.exe'
$localDumpsRoot = 'HKCU:\Software\Microsoft\Windows\Windows Error Reporting\LocalDumps'
$appDumpKey = Join-Path $localDumpsRoot $executableName

function Convert-DumpTypeToWerValue {
    param([string]$Value)
    switch ($Value) {
        'Custom' { return 1 }
        'Full' { return 2 }
    }
}

function Get-DesktopWerLocalDumpsState {
    if (-not (Test-Path -LiteralPath $appDumpKey)) {
        return [pscustomobject]@{
            Executable = $executableName
            Enabled = $false
            RegistryKey = $appDumpKey
            DumpFolder = $null
            DumpCount = $null
            DumpType = $null
        }
    }

    $item = Get-ItemProperty -LiteralPath $appDumpKey
    [pscustomobject]@{
        Executable = $executableName
        Enabled = $true
        RegistryKey = $appDumpKey
        DumpFolder = $item.DumpFolder
        DumpCount = $item.DumpCount
        DumpType = $item.DumpType
    }
}

if ($Mode -eq 'Show') {
    Get-DesktopWerLocalDumpsState | ConvertTo-Json -Depth 3
    return
}

if ($Mode -eq 'Enable' -and -not $AcknowledgePrivateDumpContents) {
    throw 'Enable requires -AcknowledgePrivateDumpContents because WER dumps may contain private process memory, user paths, and credentials.'
}

if ($Mode -eq 'Enable') {
    $resolvedDumpFolder = [System.IO.Path]::GetFullPath($DumpFolder)
    if ($PSCmdlet.ShouldProcess($appDumpKey, "Enable WER LocalDumps for $executableName")) {
        New-Item -ItemType Directory -Force -Path $resolvedDumpFolder | Out-Null
        New-Item -Path $appDumpKey -Force | Out-Null
        New-ItemProperty -LiteralPath $appDumpKey -Name DumpFolder -Value $resolvedDumpFolder -PropertyType ExpandString -Force | Out-Null
        New-ItemProperty -LiteralPath $appDumpKey -Name DumpCount -Value $DumpCount -PropertyType DWord -Force | Out-Null
        New-ItemProperty -LiteralPath $appDumpKey -Name DumpType -Value (Convert-DumpTypeToWerValue $DumpType) -PropertyType DWord -Force | Out-Null
    }

    Get-DesktopWerLocalDumpsState | ConvertTo-Json -Depth 3
    return
}

if ($Mode -eq 'Disable') {
    if ($PSCmdlet.ShouldProcess($appDumpKey, "Disable WER LocalDumps for $executableName")) {
        Remove-Item -LiteralPath $appDumpKey -Recurse -Force -ErrorAction SilentlyContinue
    }

    Get-DesktopWerLocalDumpsState | ConvertTo-Json -Depth 3
}
