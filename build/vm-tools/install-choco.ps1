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
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\vm\VmPackageInstallCommon.ps1')

$logPath = 'C:\Temp\install-choco.log'
New-Item -ItemType Directory -Path 'C:\Temp' -Force | Out-Null
Set-Content -LiteralPath $logPath -Value "Chocolatey Install - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

function Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Message
    Write-Host $line
    Add-Content -LiteralPath $logPath -Value $line
}

Set-ExecutionPolicy Bypass -Scope Process -Force
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
$env:Path += ';C:\ProgramData\chocolatey\bin'

if (Get-Command choco.exe -ErrorAction SilentlyContinue) {
    Log "Chocolatey already present."
    choco --version
    return
}

$installScriptPath = Join-Path $env:TEMP 'install-chocolatey.ps1'
try {
    Invoke-DnppvCommandWithRetry -Operation 'download Chocolatey installer' -CheckLastExitCode $false -WarningSink { param($Message) Log $Message } -ScriptBlock {
        Invoke-WebRequest -Uri 'https://community.chocolatey.org/install.ps1' -OutFile $installScriptPath -UseBasicParsing -ErrorAction Stop
    }
    Invoke-DnppvCommandWithRetry -Operation 'install Chocolatey' -CheckLastExitCode $true -WarningSink { param($Message) Log $Message } -ScriptBlock {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installScriptPath
    }
}
finally {
    Remove-Item -LiteralPath $installScriptPath -Force -ErrorAction SilentlyContinue
}

choco --version
Log "Install log saved to $logPath"



