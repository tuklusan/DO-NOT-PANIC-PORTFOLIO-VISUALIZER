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

$targetDir = 'C:\Tools\Sysinternals'
$zipPath = 'C:\Temp\SysinternalsSuite.zip'
$url = 'https://download.sysinternals.com/files/SysinternalsSuite.zip'

New-Item -ItemType Directory -Path 'C:\Temp' -Force | Out-Null
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $zipPath
Expand-Archive -LiteralPath $zipPath -DestinationPath $targetDir -Force

Write-Host "Installed Sysinternals to $targetDir"
