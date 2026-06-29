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

$logPath = 'C:\Temp\vm-qa-tools-install-resume.log'
New-Item -ItemType Directory -Path 'C:\Temp' -Force | Out-Null
Set-Content -LiteralPath $logPath -Value "VM QA Tools Resume - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

function Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Message
    Write-Host $line
    Add-Content -LiteralPath $logPath -Value $line
}

function Choco-Install {
    param([string]$Name)
    $choco = 'C:\ProgramData\chocolatey\bin\choco.exe'
    if (-not (Test-Path -LiteralPath $choco)) {
        Log "Chocolatey missing; cannot install $Name"
        return $false
    }

    Log "choco install $Name"
    & $choco install $Name -y --no-progress --limit-output
    if ($LASTEXITCODE -eq 0) {
        Log "OK: $Name"
        return $true
    }

    Log "FAILED: $Name (exit $LASTEXITCODE)"
    return $false
}

function Scoop-Install {
    param([string]$Name)
    $scoop = Join-Path $env:USERPROFILE 'scoop\shims\scoop.cmd'
    if (-not (Test-Path -LiteralPath $scoop)) {
        Log "Scoop missing; cannot install $Name"
        return $false
    }
    Log "scoop install $Name"
    & $scoop install $Name
    if ($LASTEXITCODE -eq 0) {
        Log "OK: scoop/$Name"
        return $true
    }
    Log "FAILED: scoop/$Name (exit $LASTEXITCODE)"
    return $false
}

# Ensure no hung installers remain.
Get-Process -Name choco,AutoHotkey* -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Log "Stopped hung choco/autohotkey processes."

$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')

if (-not (Choco-Install -Name 'sysinternals')) {
    Log "Trying sysinternals via scoop as fallback."
    Scoop-Install -Name 'sysinternals' | Out-Null
}

# Use portable AHK package to avoid interactive installer hangs.
if (-not (Choco-Install -Name 'autohotkey.portable')) {
    Choco-Install -Name 'autohotkey' | Out-Null
}

Choco-Install -Name 'nssm' | Out-Null
Choco-Install -Name 'nodejs-lts' | Out-Null
Choco-Install -Name 'winappdriver' | Out-Null

$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')

if (Get-Command node -ErrorAction SilentlyContinue) {
    Log "Installing appium globally with npm."
    npm install -g appium
    Log "npm exit code: $LASTEXITCODE"
}

if (Get-Command py -ErrorAction SilentlyContinue) {
    Log "Installing python UI/testing packages."
    py -m pip install --upgrade pip
    py -m pip install pywinauto pywin32 pyautogui pillow requests lxml pytest
    Log "Python package install attempted."
}

Log "Resume run completed."
