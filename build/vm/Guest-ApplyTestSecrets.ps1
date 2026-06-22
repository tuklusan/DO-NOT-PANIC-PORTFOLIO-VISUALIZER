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
    [string]$RootPath = 'C:\vmharness\portfolio-saver',
    [string]$SecretsPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($SecretsPath)) {
    $SecretsPath = Join-Path $RootPath 'artifacts\test-secrets.json'
}

function Set-UserEnvironmentValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        [Environment]::SetEnvironmentVariable($Name, $null, 'User')
        return $false
    }

    [Environment]::SetEnvironmentVariable($Name, $Value.Trim(), 'User')
    return $true
}

$report = [ordered]@{
    GeneratedAt = (Get-Date).ToString('o')
    SecretsPath = $SecretsPath
    Applied = $false
    Keys = [ordered]@{
        DEEPSEEK_API_KEY = $false
        PORTFOLIOSAVER_DEEPSEEK_API_KEY = $false
    }
}

if (Test-Path $SecretsPath) {
    $secrets = Get-Content -LiteralPath $SecretsPath -Raw | ConvertFrom-Json

    $deepSeekValue = [string]$secrets.DeepSeekApiKey
    $report.Keys.DEEPSEEK_API_KEY = Set-UserEnvironmentValue 'DEEPSEEK_API_KEY' $deepSeekValue
    $report.Keys.PORTFOLIOSAVER_DEEPSEEK_API_KEY = Set-UserEnvironmentValue 'PORTFOLIOSAVER_DEEPSEEK_API_KEY' $deepSeekValue
    $report.Applied = $true
}
else {
    foreach ($name in @(
        'DEEPSEEK_API_KEY',
        'PORTFOLIOSAVER_DEEPSEEK_API_KEY'))
    {
        [Environment]::SetEnvironmentVariable($name, $null, 'User')
    }
}

$logsRoot = Join-Path $RootPath 'logs'
New-Item -ItemType Directory -Force -Path $logsRoot | Out-Null
$reportPath = Join-Path $logsRoot ("test-secrets-{0:yyyyMMdd-HHmmss}.json" -f (Get-Date))
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Output ("TEST_SECRETS_REPORT=" + $reportPath)
