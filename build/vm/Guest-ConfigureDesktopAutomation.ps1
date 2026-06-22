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
    [Parameter(Mandatory = $true)]
    [string]$RootPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-Shortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [string]$Arguments = '',
        [string]$WorkingDirectory = ''
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    if (-not [string]::IsNullOrWhiteSpace($Arguments)) {
        $shortcut.Arguments = $Arguments
    }
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $shortcut.WorkingDirectory = $WorkingDirectory
    }
    $shortcut.Save()
}

$scriptsPath = Join-Path $RootPath 'scripts'
$agentPath = Join-Path $RootPath 'publish\agent\PortfolioSaver.VmAgent.exe'
$startupPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
$agentShortcutPath = Join-Path $startupPath 'PortfolioSaver VmAgent.lnk'
$agentLauncherPath = Join-Path $scriptsPath 'Start-PortfolioSaverVmAgent.cmd'

New-Item -ItemType Directory -Force -Path $scriptsPath,$startupPath | Out-Null

$agentLauncher = @"
@echo off
taskkill /IM PortfolioSaver.VmAgent.exe /F >nul 2>&1
if not exist "$agentPath" exit /b 0
cd /d "$RootPath"
start "" /min "$agentPath" --root-path "$RootPath"
"@
Set-Content -LiteralPath $agentLauncherPath -Value $agentLauncher -Encoding ASCII

New-Shortcut -ShortcutPath $agentShortcutPath -TargetPath $agentLauncherPath -WorkingDirectory $RootPath

Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name ScreenSaveActive -Value '0'
Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name ScreenSaverIsSecure -Value '0'
Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name ScreenSaveTimeOut -Value '0'

# The harness intentionally does not write Winlogon\DefaultPassword.
# The test user must already be logged into the interactive VM desktop session.
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' -Name DefaultPassword -ErrorAction SilentlyContinue
Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' -Name AutoAdminLogon -Value '0'

[pscustomobject]@{
    RootPath = $RootPath
    AgentPath = $agentPath
    AgentShortcutPath = $agentShortcutPath
    ScreenSaveActive = (Get-ItemProperty -Path 'HKCU:\Control Panel\Desktop').ScreenSaveActive
    AutoAdminLogon = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon').AutoAdminLogon
} | ConvertTo-Json -Compress
