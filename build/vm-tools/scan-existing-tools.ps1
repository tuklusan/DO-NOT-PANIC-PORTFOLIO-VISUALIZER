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

$outPath = 'C:\Temp\existing-tools-focused.txt'
Set-Content -LiteralPath $outPath -Value "Focused Tool Scan - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

$roots = @(
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
)

$pattern = 'Python|AutoHotkey|WinAppDriver|Windows Application Driver|Appium|Node|Git|Sysinternals|Selenium|Visual Studio|SDK|Driver|Oracle|VirtualBox|PuTTY|NSSM|7-Zip|jq|ripgrep|pywin|WDK|WINDRIVER'

Get-ItemProperty -Path $roots -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -and $_.DisplayName -match $pattern } |
    Select-Object DisplayName, DisplayVersion, Publisher |
    Sort-Object DisplayName -Unique |
    Format-Table -AutoSize |
    Out-String -Width 240 |
    Add-Content -LiteralPath $outPath

"Saved to $outPath" | Add-Content -LiteralPath $outPath
Get-Content -LiteralPath $outPath
