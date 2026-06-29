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
Set-StrictMode -Version Latest

function Get-RepoRoot {
    $root = & git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
        throw 'git repository root could not be resolved.'
    }

    return $root.Trim()
}

function Get-DeepSeekApiKey {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $key = [Environment]::GetEnvironmentVariable('DEEPSEEK_API_KEY')
    if (-not [string]::IsNullOrWhiteSpace($key)) { return $key }

    $key = [Environment]::GetEnvironmentVariable('PORTFOLIOSAVER_DEEPSEEK_API_KEY')
    if (-not [string]::IsNullOrWhiteSpace($key)) { return $key }

    # Local-only ignored test secret overlay. This file must never be committed.
    $secretsPath = Join-Path $RepositoryRoot 'build\vm\test-secrets.json'
    if (Test-Path -LiteralPath $secretsPath) {
        try {
            $secrets = Get-Content -Raw -LiteralPath $secretsPath | ConvertFrom-Json
            if ($secrets.PSObject.Properties.Name -contains 'DeepSeekApiKey' -and
                -not [string]::IsNullOrWhiteSpace([string]$secrets.DeepSeekApiKey)) {
                return [string]$secrets.DeepSeekApiKey
            }
        }
        catch {
            Write-Warning "Invalid JSON in build\vm\test-secrets.json; fix or delete the file if DeepSeek key resolution needs it. $($_.Exception.Message)"
        }
    }

    throw "DeepSeek API access is mandatory for this project's workflow, but no DeepSeek key was found in DEEPSEEK_API_KEY, PORTFOLIOSAVER_DEEPSEEK_API_KEY, or build\vm\test-secrets.json. Hard stop: do not commit, push, or run local/VM validation until DeepSeek access is available."
}
