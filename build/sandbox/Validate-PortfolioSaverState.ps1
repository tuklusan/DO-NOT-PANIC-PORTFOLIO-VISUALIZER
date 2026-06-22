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
    [ValidateSet("Installed", "Uninstalled")]
    [string]$ExpectedState = "Installed"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scrPath = Join-Path $env:WINDIR "System32\PortfolioSaver.Screensaver.scr"
$manifestPath = Join-Path $env:ProgramData "PortfolioSaverScreensaver\installed-files.txt"
$uninstallKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PortfolioSaverScreensaver"

$checks = @(
    [pscustomobject]@{
        Name = "Screensaver file"
        Present = Test-Path $scrPath
        Details = $scrPath
    },
    [pscustomobject]@{
        Name = "Install manifest"
        Present = Test-Path $manifestPath
        Details = $manifestPath
    },
    [pscustomobject]@{
        Name = "Uninstall registry key"
        Present = Test-Path $uninstallKey
        Details = $uninstallKey
    }
)

$expectedPresent = $ExpectedState -eq "Installed"
$failedChecks = $checks | Where-Object { $_.Present -ne $expectedPresent }

Write-Host ""
Write-Host "PortfolioSaver expected state: $ExpectedState"
Write-Host ""

foreach ($check in $checks) {
    $status = if ($check.Present) { "Present" } else { "Missing" }
    Write-Host ("{0,-24} {1,-8} {2}" -f $check.Name, $status, $check.Details)
}

Write-Host ""
if ($failedChecks.Count -eq 0) {
    Write-Host "Validation passed."
    exit 0
}

Write-Host "Validation failed."
exit 1
