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
param(
    [string]$VmHost = '192.168.56.102',
    [int]$VmPort = 22,
    [string]$RootPath = 'C:\vmharness\portfolio-saver',
    [string]$RemotePath,
    [string]$LocalDestinationRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'VmSshCommon.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($LocalDestinationRoot)) {
    $LocalDestinationRoot = Join-Path $repoRoot 'build\vm\artifacts\ssh-runs'
}

$bundle = $null
try {
    New-Item -ItemType Directory -Force -Path $LocalDestinationRoot | Out-Null
    $bundle = New-VmSshSessionBundle -HostName $VmHost -Port $VmPort

    if ([string]::IsNullOrWhiteSpace($RemotePath)) {
        $latestCommand = @"
`$root = Join-Path '$RootPath' 'results'
`$latest = Get-ChildItem -LiteralPath `$root -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (`$null -eq `$latest) {
    throw 'No remote result directories were found.'
}
Write-Output `$latest.FullName
"@
        $latest = Invoke-VmPwshCommand -Bundle $bundle -Command $latestCommand -TimeOutSeconds 120
        $RemotePath = ($latest.Output -join [Environment]::NewLine).Trim()
    }

    $leafName = Split-Path -Path $RemotePath -Leaf
    $localTarget = Join-Path $LocalDestinationRoot $leafName
    if (Test-Path $localTarget) {
        Remove-Item -LiteralPath $localTarget -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-VmSshStep "Pulling remote result bundle $RemotePath"
    Receive-VmItem -Bundle $bundle -RemotePath $RemotePath -LocalDestination $LocalDestinationRoot
    Write-Output ("LOCAL_RESULT_DIR=" + $localTarget)
}
finally {
    if ($null -ne $bundle) {
        Remove-VmSshSessionBundle -Bundle $bundle
    }
}
