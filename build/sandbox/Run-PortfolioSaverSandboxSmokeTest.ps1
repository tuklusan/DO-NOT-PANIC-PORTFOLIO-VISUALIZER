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
[CmdletBinding()]
param(
    [string]$WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resultsRoot = Join-Path $WorkspaceRoot "build\sandbox\results"
$logPath = Join-Path $resultsRoot "sandbox-smoke-test.log"
$resultPath = Join-Path $resultsRoot "sandbox-smoke-test.json"

New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null
Remove-Item -LiteralPath $logPath,$resultPath -Force -ErrorAction SilentlyContinue

function Write-Log {
    param([string]$Message)

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Add-Content -LiteralPath $logPath -Value "[$timestamp] $Message" -Encoding UTF8
}

try {
    Write-Log "Starting sandbox repository smoke test."

    if (-not (Test-Path -LiteralPath $WorkspaceRoot)) {
        throw "Sandbox workspace root not found: $WorkspaceRoot"
    }

    $solutionPath = Join-Path $WorkspaceRoot "DoNotPanicPortfolioVisualizer.sln"
    $licensePath = Join-Path $WorkspaceRoot "LICENSE"
    $readmePath = Join-Path $WorkspaceRoot "README.md"

    foreach ($path in @($solutionPath, $licensePath, $readmePath)) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Required sandbox smoke file is missing: $path"
        }
    }

    $solutionText = Get-Content -LiteralPath $solutionPath -Raw
    if ($solutionText -notmatch [regex]::Escape('Project(') -or $solutionText -notmatch [regex]::Escape('PortfolioSaver.Desktop')) {
        throw "Solution file does not contain the expected desktop project entries."
    }

    $summary = [ordered]@{
        status = "Passed"
        checkedAt = (Get-Date).ToString("o")
        solution = "DoNotPanicPortfolioVisualizer.sln"
    }

    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    Write-Log "Sandbox repository smoke test passed."
    exit 0
}
catch {
    $summary = [ordered]@{
        status = "Failed"
        checkedAt = (Get-Date).ToString("o")
        error = $_.Exception.Message
    }

    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    Write-Log ("Sandbox repository smoke test failed: {0}" -f $_.Exception.Message)
    exit 1
}
