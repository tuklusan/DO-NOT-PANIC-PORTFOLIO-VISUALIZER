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
