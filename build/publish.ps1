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
