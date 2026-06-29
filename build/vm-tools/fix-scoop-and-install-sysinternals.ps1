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
