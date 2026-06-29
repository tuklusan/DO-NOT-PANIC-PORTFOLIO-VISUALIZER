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

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$manifestScript = Join-Path $PSScriptRoot "generate-release-manifest.ps1"
$screensaverProject = Join-Path $root "src\PortfolioSaver.Screensaver\PortfolioSaver.Screensaver.csproj"
$configProject = Join-Path $root "src\PortfolioSaver.Config\PortfolioSaver.Config.csproj"

dotnet publish $screensaverProject -c $Configuration -r win-x64 --self-contained false
dotnet publish $configProject -c $Configuration -r win-x64 --self-contained false

$screensaverPublishDir = Join-Path $root "src\PortfolioSaver.Screensaver\bin\$Configuration\net10.0-windows\win-x64\publish"
$configPublishDir = Join-Path $root "src\PortfolioSaver.Config\bin\$Configuration\net10.0-windows\win-x64\publish"
$desktopPublishDir = Join-Path $root "src\PortfolioSaver.Desktop\bin\$Configuration\net10.0-windows\win-x64\publish"

& $manifestScript -PublishDir $screensaverPublishDir
& $manifestScript -PublishDir $configPublishDir
