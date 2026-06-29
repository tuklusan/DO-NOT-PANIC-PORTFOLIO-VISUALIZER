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
    [string]$Configuration = "Release"
)

$publishDir = Join-Path $PSScriptRoot "..\src\PortfolioSaver.Screensaver\bin\$Configuration\net10.0-windows\win-x64\publish"
$exePath = Join-Path $publishDir "PortfolioSaver.Screensaver.exe"
$scrPath = Join-Path $publishDir "PortfolioSaver.Screensaver.scr"

if (-not (Test-Path $exePath)) {
    throw "Published screensaver executable not found. Run publish.ps1 first."
}

Copy-Item $exePath $scrPath -Force
Write-Host "Created $scrPath"
