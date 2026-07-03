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
    [ValidateSet("Online", "Offline")]
    [string]$Mode = "Online",

    [string]$WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resultsRoot = Join-Path $WorkspaceRoot ("build\sandbox\results\" + $Mode.ToLowerInvariant())
$logPath = Join-Path $resultsRoot "ui-validation.log"
$resultPath = Join-Path $resultsRoot "ui-validation.json"

New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null
Remove-Item -LiteralPath $logPath,$resultPath -Force -ErrorAction SilentlyContinue

function Write-Log {
    param([string]$Message)

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Add-Content -LiteralPath $logPath -Value "[$timestamp] $Message" -Encoding UTF8
}

try {
    Write-Log "Starting $Mode sandbox UI validation for DO NOT PANIC PORTFOLIO VISUALIZER."
    if ($Mode -eq "Offline") {
        Write-Log "Offline mode selected; validation is limited to local repository/UI configuration checks."
    }
    else {
        Write-Log "Online mode selected; validation still avoids external network calls and checks local repository/UI configuration."
    }

    if (-not (Test-Path -LiteralPath $WorkspaceRoot)) {
        throw "Sandbox workspace root not found: $WorkspaceRoot"
    }

    $configXamlPath = Join-Path $WorkspaceRoot "src\PortfolioSaver.Settings\Windows\MainWindow.xaml"
    $desktopProjectPath = Join-Path $WorkspaceRoot "src\PortfolioSaver.Desktop\PortfolioSaver.Desktop.csproj"
    $installerScriptPath = Join-Path $WorkspaceRoot "build\installer\DoNotPanicPortfolioVisualizer.iss"

    $requiredFiles = @($configXamlPath, $desktopProjectPath, $installerScriptPath)
    foreach ($path in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Required sandbox validation file is missing: $path"
        }
    }

    $configXaml = Get-Content -LiteralPath $configXamlPath -Raw
    if ($configXaml -notmatch [regex]::Escape("DO NOT PANIC PORTFOLIO VISUALIZER Config - 1.0")) {
        throw "Config title does not contain the expected 1.0 title."
    }

    if ($configXaml -match "Config\s+-\s+BETA") {
        throw "Config title contains stale beta labeling."
    }

    $summary = [ordered]@{
        mode = $Mode
        status = "Passed"
        checkedAt = (Get-Date).ToString("o")
        configTitle = "DO NOT PANIC PORTFOLIO VISUALIZER Config - 1.0"
        modeBehavior = if ($Mode -eq "Offline") { "LocalOnlyOffline" } else { "LocalOnlyOnline" }
    }

    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    Write-Log "Sandbox UI validation passed."
    exit 0
}
catch {
    $summary = [ordered]@{
        mode = $Mode
        status = "Failed"
        checkedAt = (Get-Date).ToString("o")
        error = $_.Exception.Message
    }

    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    Write-Log ("Sandbox UI validation failed: {0}" -f $_.Exception.Message)
    exit 1
}
