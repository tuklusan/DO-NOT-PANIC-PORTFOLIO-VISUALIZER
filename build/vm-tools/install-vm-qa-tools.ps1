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

$logPath = 'C:\Temp\vm-qa-tools-install.log'
New-Item -ItemType Directory -Path 'C:\Temp' -Force | Out-Null
Set-Content -LiteralPath $logPath -Value "VM QA Tools Install - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

function Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Message
    Write-Host $line
    Add-Content -LiteralPath $logPath -Value $line
}

$choco = 'C:\ProgramData\chocolatey\bin\choco.exe'
if (-not (Test-Path -LiteralPath $choco)) {
    Log "Chocolatey not found. Cannot continue."
    exit 1
}

$packages = @(
    'git',
    'python',
    '7zip',
    'jq',
    'ripgrep',
    'sysinternals',
    'autohotkey',
    'nssm',
    'nodejs-lts',
    'winappdriver'
)

foreach ($pkg in $packages) {
    Log "Installing via choco: $pkg"
    & $choco install $pkg -y --no-progress --limit-output
    if ($LASTEXITCODE -eq 0) {
        Log "Installed or already present: $pkg"
    } else {
        Log "Install failed (exit $LASTEXITCODE): $pkg"
    }
}

# Refresh path for current process.
$env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
            [System.Environment]::GetEnvironmentVariable('Path', 'User')

if (Get-Command node -ErrorAction SilentlyContinue) {
    Log "Installing global npm package: appium"
    npm install -g appium
    Log "npm exit code: $LASTEXITCODE"
} else {
    Log "Skipping appium install; node not found."
}

$python = $null
if (Get-Command py -ErrorAction SilentlyContinue) {
    $python = 'py'
} elseif (Get-Command python -ErrorAction SilentlyContinue) {
    $python = 'python'
}

if ($null -ne $python) {
    Log "Installing Python packages for UI automation/testing"
    & $python -m pip install --upgrade pip
    & $python -m pip install pywinauto pywin32 pyautogui pillow requests lxml pytest
    Log "Python package installation attempted."
} else {
    Log "Skipping Python package installs; Python launcher not found."
}

Log "Final quick versions:"
if (Get-Command choco -ErrorAction SilentlyContinue) { Log ("choco=" + (& choco --version 2>$null)) }
if (Get-Command scoop -ErrorAction SilentlyContinue) { Log ("scoop=" + (& scoop --version 2>$null)) }
if (Get-Command git -ErrorAction SilentlyContinue) { Log ("git=" + (& git --version 2>$null)) }
if (Get-Command node -ErrorAction SilentlyContinue) { Log ("node=" + (& node --version 2>$null)) }
if (Get-Command python -ErrorAction SilentlyContinue) { Log ("python=" + (& python --version 2>&1)) }
if (Get-Command py -ErrorAction SilentlyContinue) { Log ("py=" + (& py --version 2>&1)) }

Log "Install log saved to $logPath"
