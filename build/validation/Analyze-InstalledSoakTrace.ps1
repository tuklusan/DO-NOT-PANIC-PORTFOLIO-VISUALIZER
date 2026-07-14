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
    [Parameter(Mandatory = $true)][string]$ResultRoot,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-CircularTraceText {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }

    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path).Path)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes).Replace("`0", '')
    return @($text -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Select-StrictTransportFailure {
    param([Parameter(Mandatory = $true)][string[]]$Lines)

    $failurePattern = '(?i)(PendingReadDrainFailed|Transport.*Failed|Unable to read data from the transport connection|forcibly closed|SocketException|connection reset|timed out|timeout)'
    return @($Lines | Where-Object {
            $_ -match $failurePattern -and
            $_ -notmatch '(?i)YFinanceServerFaultInjection' -and
            $_ -notmatch '(?i)event=ServerStartup' -and
            $_ -notmatch '(?i)program=YFinance\.NET\.Server .* event=ServerStarted'
        })
}

function Measure-LineCount {
    param($Lines)
    return @($Lines).Count
}

function Select-Http429Evidence {
    param([Parameter(Mandatory = $true)][string[]]$Lines)

    $rateLimitPattern = '(?i)(status(_code)?=429|http_status=429|response_status=429|\bHTTP\s*429\b|HTTP/\d+(?:\.\d+)?\s+429\b|Too Many Requests|rate limit(?:ed)?)'
    return @($Lines | Where-Object { $_ -match $rateLimitPattern })
}

$root = (Resolve-Path -LiteralPath $ResultRoot).Path
$traceDir = Join-Path $root 'trace'
$desktopLines = @(Read-CircularTraceText -Path (Join-Path $traceDir 'trace.circular.log'))
$yfinanceLines = @(Read-CircularTraceText -Path (Join-Path $traceDir 'yfinance.circular.log'))
$allLines = @($desktopLines + $yfinanceLines)
$summaryPath = Join-Path $root 'summary.json'
$summaryPath = if (Test-Path -LiteralPath $summaryPath) { $summaryPath } else { Join-Path $root 'ux-deep-summary.json' }
$summary = if (Test-Path -LiteralPath $summaryPath) { Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json } else { [pscustomobject]@{} }
$slowScene = @($desktopLines | Where-Object { $_ -match 'event=SceneSchedulerActionSlow' })
$renderSurfaceRecovery = @($desktopLines | Where-Object { $_ -match 'event=RenderSurfaceRecoveryRequested' })
$transportFailures = @(Select-StrictTransportFailure -Lines $yfinanceLines)
$http429Evidence = @(Select-Http429Evidence -Lines $allLines)

$report = [ordered]@{
    resultRoot = $root
    summary = $summary
    counts = [ordered]@{
        fatalCrashException = @($allLines | Where-Object { $_ -match '(?i)(DispatcherUnhandledException|UnhandledException|Fatal|crash)' }).Count
        error = @($allLines | Where-Object { $_ -match '(?i)\bERROR\b' }).Count
        warn = @($allLines | Where-Object { $_ -match '(?i)\bWARN\b' }).Count
        http429 = Measure-LineCount $http429Evidence
        sceneSchedulerActionSlow = Measure-LineCount $slowScene
        strictTransportFailure = Measure-LineCount $transportFailures
        runtimeQuoteApplied = @($desktopLines | Where-Object { $_ -match 'event=RuntimeQuoteApplied(?!\w)' }).Count
        renderSurfaceHeartbeat = @($desktopLines | Where-Object { $_ -match 'event=RenderSurfaceHeartbeat(?!\w)' }).Count
        renderSurfaceRecoveryRequested = Measure-LineCount $renderSurfaceRecovery
        renderSurfaceHeartbeatRecovered = @($desktopLines | Where-Object { $_ -match 'event=RenderSurfaceHeartbeatRecovered' }).Count
        worldMarketsFetchComplete = @($desktopLines | Where-Object { $_ -match 'event=WorldMarketsFetchComplete' }).Count
        worldMarketsUiPatchComplete = @($desktopLines | Where-Object { $_ -match 'event=WorldMarketsUiPatchComplete' }).Count
        backgroundRotationChosen = @($desktopLines | Where-Object { $_ -match 'event=BackgroundRotationChosen' }).Count
        backgroundTransitionComplete = @($desktopLines | Where-Object { $_ -match 'event=BackgroundTransitionComplete' }).Count
        warmGraphsCompleted = @($desktopLines | Where-Object { $_ -match 'WarmGraphsAsync completed' }).Count
    }
    samples = [ordered]@{
        slowScene = @($slowScene | Select-Object -First 20)
        strictTransportFailure = @($transportFailures | Select-Object -First 20)
        http429 = @($http429Evidence | Select-Object -First 20)
        renderSurfaceRecovery = @($renderSurfaceRecovery | Select-Object -First 20)
        runtimeErrors = @($allLines | Where-Object { $_ -match '(?i)(DispatcherUnhandledException|UnhandledException|Fatal|crash|\bERROR\b)' } | Select-Object -First 20)
    }
}

$json = $report | ConvertTo-Json -Depth 8
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $json
} else {
    $target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Set-Content -LiteralPath $target -Value $json -Encoding UTF8
    Write-Output $target
}
