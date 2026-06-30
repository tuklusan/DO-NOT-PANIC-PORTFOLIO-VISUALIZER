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
        OPENROUTER_AI_API_KEY = $false
        OPENROUTER_API_KEY = $false
        DEEPSEEK_API_KEY = $false
        PORTFOLIOSAVER_DEEPSEEK_API_KEY = $false
    }
}

if (Test-Path $SecretsPath) {
    $secrets = Get-Content -LiteralPath $SecretsPath -Raw | ConvertFrom-Json

    $deepSeekValue = [string]$secrets.DeepSeekApiKey
    $openRouterValue = $deepSeekValue
    if ($null -ne $secrets.PSObject.Properties['OpenRouterApiKey'] -and -not [string]::IsNullOrWhiteSpace([string]$secrets.OpenRouterApiKey)) {
        $openRouterValue = [string]$secrets.OpenRouterApiKey
    }

    $report.Keys.OPENROUTER_AI_API_KEY = Set-UserEnvironmentValue 'OPENROUTER_AI_API_KEY' $openRouterValue
    $report.Keys.OPENROUTER_API_KEY = Set-UserEnvironmentValue 'OPENROUTER_API_KEY' $openRouterValue
    $report.Keys.DEEPSEEK_API_KEY = Set-UserEnvironmentValue 'DEEPSEEK_API_KEY' $deepSeekValue
    $report.Keys.PORTFOLIOSAVER_DEEPSEEK_API_KEY = Set-UserEnvironmentValue 'PORTFOLIOSAVER_DEEPSEEK_API_KEY' $deepSeekValue
    $report.Applied = $true
}
else {
    foreach ($name in @(
        'OPENROUTER_AI_API_KEY',
        'OPENROUTER_API_KEY',
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
