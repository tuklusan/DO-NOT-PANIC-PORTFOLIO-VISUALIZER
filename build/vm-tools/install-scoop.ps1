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

try {
    Set-ExecutionPolicy RemoteSigned -Scope CurrentUser -Force -ErrorAction Stop
} catch {
    Write-Host "Execution policy update skipped: $($_.Exception.Message)"
}

$installerPath = Join-Path $env:TEMP 'install-scoop.ps1'
Invoke-WebRequest -UseBasicParsing -Uri 'https://get.scoop.sh' -OutFile $installerPath

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).
    IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)

if ($isAdmin) {
    & $installerPath -RunAsAdmin
} else {
    & $installerPath
}

$scoopPath = Join-Path $env:USERPROFILE 'scoop\shims'
if (Test-Path -LiteralPath $scoopPath) {
    $env:Path += ";$scoopPath"
}

scoop --version
