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

$outPath = 'C:\Temp\vm-tool-inventory.txt'
New-Item -ItemType Directory -Path 'C:\Temp' -Force | Out-Null

function Write-Section {
    param([string]$Title)
    Add-Content -LiteralPath $outPath -Value ""
    Add-Content -LiteralPath $outPath -Value ("=" * 90)
    Add-Content -LiteralPath $outPath -Value $Title
    Add-Content -LiteralPath $outPath -Value ("=" * 90)
}

Set-Content -LiteralPath $outPath -Value "VM Tool Inventory - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

Write-Section "System"
Get-ComputerInfo |
    Select-Object WindowsProductName, WindowsVersion, OsHardwareAbstractionLayer, OsName, OsVersion, CsName |
    Format-List |
    Out-String |
    Add-Content -LiteralPath $outPath

Write-Section "Chocolatey / Scoop"
$choco = 'C:\ProgramData\chocolatey\bin\choco.exe'
$scoop = Join-Path $env:USERPROFILE 'scoop\shims\scoop.cmd'
if (Test-Path -LiteralPath $choco) {
    "Chocolatey: $(& $choco --version 2>$null)" | Add-Content -LiteralPath $outPath
    & $choco list --local-only --limit-output 2>$null | Add-Content -LiteralPath $outPath
} else {
    "Chocolatey: NOT FOUND" | Add-Content -LiteralPath $outPath
}
if (Test-Path -LiteralPath $scoop) {
    "Scoop: $(& $scoop --version 2>$null)" | Add-Content -LiteralPath $outPath
    & $scoop list 2>$null | Add-Content -LiteralPath $outPath
} else {
    "Scoop: NOT FOUND" | Add-Content -LiteralPath $outPath
}

Write-Section "Installed Programs (Registry)"
$uninstallRoots = @(
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
Get-ItemProperty -Path $uninstallRoots -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName } |
    Select-Object DisplayName, DisplayVersion, Publisher, InstallDate |
    Sort-Object DisplayName -Unique |
    Format-Table -AutoSize |
    Out-String -Width 260 |
    Add-Content -LiteralPath $outPath

Write-Section "Known Command Checks"
$commands = @(
    'python', 'py', 'pip', 'git', 'node', 'npm', 'choco', 'scoop', 'rg', 'jq',
    'WinAppDriver', 'plink', 'pscp', 'AutoHotkey', 'nssm'
)
foreach ($name in $commands) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($null -ne $cmd) {
        ("{0,-18} {1}" -f $name, $cmd.Source) | Add-Content -LiteralPath $outPath
    } else {
        ("{0,-18} NOT FOUND" -f $name) | Add-Content -LiteralPath $outPath
    }
}

Write-Section "Interesting File Hits (Program Files + Tools)"
$roots = @(
    'C:\Program Files',
    'C:\Program Files (x86)',
    'C:\Tools',
    'C:\Windows',
    $env:USERPROFILE
)
$patterns = @(
    'windriver', 'winappdriver', 'pywin', 'python', 'autohotkey', 'node',
    'appium', 'selenium', 'sysinternals', 'putty', 'ripgrep', 'jq', 'nssm'
)

foreach ($root in $roots) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    "`n-- Scan Root: $root" | Add-Content -LiteralPath $outPath
    $allFiles = Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue
    foreach ($pat in $patterns) {
        $hits = $allFiles |
            Where-Object { $_.Name -match $pat } |
            Select-Object -First 20 -ExpandProperty FullName
        if ($hits) {
            "Pattern [$pat]:" | Add-Content -LiteralPath $outPath
            $hits | Add-Content -LiteralPath $outPath
        }
    }
}

Write-Section "Done"
"Inventory saved to $outPath" | Add-Content -LiteralPath $outPath
Write-Host "Inventory saved to $outPath"
