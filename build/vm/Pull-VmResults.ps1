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
