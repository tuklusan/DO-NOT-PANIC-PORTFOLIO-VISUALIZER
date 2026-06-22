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
$ErrorActionPreference = 'Continue'

$log = 'C:\Temp\fix-scoop-sysinternals.log'
Set-Content -LiteralPath $log -Value "Fix Scoop/Sysinternals - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

function Log {
    param([string]$m)
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $m
    Write-Host $line
    Add-Content -LiteralPath $log -Value $line
}

$scoop = Join-Path $env:USERPROFILE 'scoop\shims\scoop.cmd'
if (-not (Test-Path -LiteralPath $scoop)) {
    Log "Scoop not found."
    exit 1
}

Log "Checking scoop status"
& $scoop --version

Log "Resetting main bucket"
& $scoop bucket rm main
& $scoop bucket add main

Log "Installing sysinternals via scoop"
& $scoop install sysinternals

if ($LASTEXITCODE -ne 0) {
    Log "Scoop sysinternals install failed. Trying direct Microsoft package via Chocolatey fallback package."
    $choco = 'C:\ProgramData\chocolatey\bin\choco.exe'
    if (Test-Path -LiteralPath $choco) {
        & $choco install procexp -y --no-progress --limit-output
    }
}

Log "Done."
