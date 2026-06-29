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
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$scriptPaths = @(
    'build\validation\Add-AuditChangeRequest.ps1',
    'build\validation\Analyze-VisualValidationArtifacts.ps1',
    'build\validation\Invoke-DeepSeekArtifactReview.ps1',
    'build\validation\Invoke-AutonomousVisualValidation.ps1',
    'build\vm\Invoke-VmBuildTest.ps1',
    'build\publish-inno-installer.ps1',
    'build\installer\Cleanup-DoNotPanicPortfolioVisualizer.ps1',
    'build\installer\Test-InnoInstallCycle.ps1'
)

foreach ($relativePath in $scriptPaths) {
    $path = Join-Path $repoRoot $relativePath
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors) {
        $messages = ($parseErrors | ForEach-Object { $_.Message }) -join '; '
        throw "PowerShell parser failed for ${relativePath}: $messages"
    }
}

$allowList = Join-Path $repoRoot 'build\validation\allowed-trace-patterns.txt'
if (-not (Test-Path -LiteralPath $allowList)) { throw 'Missing allowed-trace-patterns.txt.' }
if ([string]::IsNullOrWhiteSpace((Get-Content -Raw -LiteralPath $allowList))) { throw 'allowed-trace-patterns.txt is empty.' }

$gitignore = Join-Path $repoRoot '.gitignore'
if (Test-Path -LiteralPath $gitignore) {
    $ignored = Select-String -LiteralPath $gitignore -Pattern '^build/validation/artifacts/$' -Quiet
    if (-not $ignored) { throw 'Generated validation artifact directory is not ignored.' }
}

$autonomousScript = Join-Path $repoRoot 'build\validation\Invoke-AutonomousVisualValidation.ps1'
$autonomousText = Get-Content -Raw -LiteralPath $autonomousScript
if ($autonomousText -notmatch "'build/vm'") { throw 'Autonomous validation default CommitPaths does not include build/vm.' }

$vmScript = Join-Path $repoRoot 'build\vm\Invoke-VmBuildTest.ps1'
$vmText = Get-Content -Raw -LiteralPath $vmScript
$pathValidationIndex = $vmText.IndexOf('Test-Path $localResultDir', [StringComparison]::Ordinal)
$stdoutEmissionIndex = $vmText.IndexOf('Write-Output $localResultDirLine[0]', [StringComparison]::Ordinal)
if ($pathValidationIndex -lt 0 -or $stdoutEmissionIndex -lt 0 -or $stdoutEmissionIndex -lt $pathValidationIndex) { throw 'Invoke-VmBuildTest does not emit a validated LOCAL_RESULT_DIR on stdout.' }

$publishSafeTempScript = Join-Path $repoRoot 'build\publish-safe-temp.ps1'
$publishSafeTempText = Get-Content -Raw -LiteralPath $publishSafeTempScript
foreach ($requiredSnippet in @(
    'PortfolioSaverPublishWorkspace',
    'Copy-RequiredRepositoryItem',
    'Directory.Build.targets',
    'THIRD-PARTY-LICENSES'
)) {
    if ($publishSafeTempText -notmatch [regex]::Escape($requiredSnippet)) {
        throw "publish-safe-temp.ps1 is missing required safe-temp publish contract snippet: $requiredSnippet"
    }
}

$innoScript = Join-Path $repoRoot 'build\installer\DoNotPanicPortfolioVisualizer.iss'
if (-not (Test-Path -LiteralPath $innoScript)) { throw 'Missing Inno installer script.' }
$innoText = Get-Content -Raw -LiteralPath $innoScript
foreach ($requiredInnoSnippet in @(
    'PrivilegesRequired=admin',
    'LicenseFile={#LicenseFile}',
    'DefaultDirName={autopf}\{#AppPublisher}\{#AppFolderName}',
    'Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"',
    '-AllUsers'
)) {
    if ($innoText -notmatch [regex]::Escape($requiredInnoSnippet)) {
        throw "DoNotPanicPortfolioVisualizer.iss is missing required installer contract snippet: $requiredInnoSnippet"
    }
}
if ($innoText -match 'PrivilegesRequiredOverridesAllowed') {
    throw 'Inno installer must not allow non-admin privilege override.'
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('dnppv-validation-smoke-' + [Guid]::NewGuid().ToString('N'))
try {
    $singleRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000000'
    New-Item -ItemType Directory -Force -Path $singleRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000000'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed' } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $singleRun 'ux-deep-summary.json') -Encoding UTF8
    $analysisPath = Join-Path $tempRoot 'analysis.json'
    try {
        $analysisOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $singleRun -OutputPath $analysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    }
    catch {
        throw "Analyze-VisualValidationArtifacts failed for a single run directory: $($_.Exception.Message)"
    }
    if (-not ($analysisOutput -match 'ANALYSIS_REPORT=')) { throw 'Analyze-VisualValidationArtifacts did not emit ANALYSIS_REPORT.' }
    $report = Get-Content -Raw -LiteralPath $analysisPath | ConvertFrom-Json
    if (-not $report.clean) { throw 'Analyze-VisualValidationArtifacts reported findings for the clean single-run smoke fixture.' }

    $offlinePassRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000001'
    New-Item -ItemType Directory -Force -Path $offlinePassRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000001'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-during-runtime' } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $offlinePassRun 'ux-deep-summary.json') -Encoding UTF8
    '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=offline' |
        Set-Content -LiteralPath (Join-Path $offlinePassRun 'fault-injection-events.log') -Encoding UTF8
    @(
        '2026-01-01T00:00:01Z | INFO | event=RuntimeDataFreshnessChanged / previous_text=LIVE quote feed / data_freshness_text=OFFLINE - showing last values / failure_streak=2 / quote_count=12',
        '2026-01-01T00:00:02Z | INFO | event=RuntimeQuoteRequestFailed / data_freshness_text=OFFLINE - showing last values'
    ) |
        Set-Content -LiteralPath (Join-Path $offlinePassRun 'combined-trace-tail.txt') -Encoding UTF8
    $offlinePassAnalysisPath = Join-Path $tempRoot 'offline-pass-analysis.json'
    $offlinePassOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $offlinePassRun -OutputPath $offlinePassAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($offlinePassOutput -match 'ANALYSIS_REPORT=')) { throw 'Offline-pass analysis did not emit ANALYSIS_REPORT.' }
    $offlinePassReport = Get-Content -Raw -LiteralPath $offlinePassAnalysisPath | ConvertFrom-Json
    if (-not $offlinePassReport.clean) { throw 'Analyze-VisualValidationArtifacts reported findings for the offline UX proof fixture.' }

    $offlineFailRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000002'
    New-Item -ItemType Directory -Force -Path $offlineFailRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000002'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-during-runtime' } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $offlineFailRun 'ux-deep-summary.json') -Encoding UTF8
    '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=offline' |
        Set-Content -LiteralPath (Join-Path $offlineFailRun 'fault-injection-events.log') -Encoding UTF8
    'event=RuntimeQuoteRequestFailed / data_freshness_text=LIVE quote feed' |
        Set-Content -LiteralPath (Join-Path $offlineFailRun 'combined-trace-tail.txt') -Encoding UTF8
    $offlineFailAnalysisPath = Join-Path $tempRoot 'offline-fail-analysis.json'
    $offlineFailOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $offlineFailRun -OutputPath $offlineFailAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($offlineFailOutput -match 'ANALYSIS_REPORT=')) { throw 'Offline-fail analysis did not emit ANALYSIS_REPORT.' }
    $offlineFailReport = Get-Content -Raw -LiteralPath $offlineFailAnalysisPath | ConvertFrom-Json
    $offlineFinding = @($offlineFailReport.findings | Where-Object { $_.code -eq 'offline-ux-state-unverified' })
    if ($offlineFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts did not flag missing offline UX proof.' }
    $offlineDelayFinding = @($offlineFailReport.findings | Where-Object { $_.code -eq 'offline-ux-state-delay' })
    if ($offlineDelayFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts did not flag missing prompt offline UX transition proof.' }
    $offlineTraceAnomalyFinding = @($offlineFailReport.findings | Where-Object { $_.code -eq 'trace-anomalies' })
    if ($offlineTraceAnomalyFinding.Count -ne 0) { throw 'Offline-fail fixture produced unintended trace-anomaly findings.' }

    $offlineDelayedRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000019'
    New-Item -ItemType Directory -Force -Path $offlineDelayedRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000019'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-during-runtime' } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $offlineDelayedRun 'ux-deep-summary.json') -Encoding UTF8
    '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=offline' |
        Set-Content -LiteralPath (Join-Path $offlineDelayedRun 'fault-injection-events.log') -Encoding UTF8
    @(
        '2026-01-01T00:00:04Z | INFO | event=RuntimeDataFreshnessChanged / previous_text=LIVE quote feed / data_freshness_text=OFFLINE - showing last values / failure_streak=2 / quote_count=12',
        '2026-01-01T00:00:05Z | INFO | event=RuntimeQuoteRequestFailed / data_freshness_text=OFFLINE - showing last values'
    ) |
        Set-Content -LiteralPath (Join-Path $offlineDelayedRun 'combined-trace-tail.txt') -Encoding UTF8
    $offlineDelayedAnalysisPath = Join-Path $tempRoot 'offline-delayed-analysis.json'
    $offlineDelayedOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $offlineDelayedRun -OutputPath $offlineDelayedAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($offlineDelayedOutput -match 'ANALYSIS_REPORT=')) { throw 'Offline-delayed analysis did not emit ANALYSIS_REPORT.' }
    $offlineDelayedReport = Get-Content -Raw -LiteralPath $offlineDelayedAnalysisPath | ConvertFrom-Json
    $offlineDelayedFinding = @($offlineDelayedReport.findings | Where-Object { $_.code -eq 'offline-ux-state-delay' })
    if ($offlineDelayedFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts did not flag delayed prompt offline UX transition proof.' }
    $offlineDelayedUnexpectedFinding = @($offlineDelayedReport.findings | Where-Object { $_.code -ne 'offline-ux-state-delay' })
    if ($offlineDelayedUnexpectedFinding.Count -ne 0) { throw 'Offline-delayed fixture produced unintended additional findings.' }

    $offlineRuntimeNoActivationRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000018'
    New-Item -ItemType Directory -Force -Path $offlineRuntimeNoActivationRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000018'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-during-runtime' } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $offlineRuntimeNoActivationRun 'ux-deep-summary.json') -Encoding UTF8
    '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=none' |
        Set-Content -LiteralPath (Join-Path $offlineRuntimeNoActivationRun 'fault-injection-events.log') -Encoding UTF8
    'event=RuntimeQuoteRequestFailed / data_freshness_text=OFFLINE - showing last values' |
        Set-Content -LiteralPath (Join-Path $offlineRuntimeNoActivationRun 'combined-trace-tail.txt') -Encoding UTF8
    $offlineRuntimeNoActivationAnalysisPath = Join-Path $tempRoot 'offline-runtime-no-activation-analysis.json'
    $offlineRuntimeNoActivationOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $offlineRuntimeNoActivationRun -OutputPath $offlineRuntimeNoActivationAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($offlineRuntimeNoActivationOutput -match 'ANALYSIS_REPORT=')) { throw 'Offline-runtime-no-activation analysis did not emit ANALYSIS_REPORT.' }
    $offlineRuntimeNoActivationReport = Get-Content -Raw -LiteralPath $offlineRuntimeNoActivationAnalysisPath | ConvertFrom-Json
    $offlineRuntimeActivationFinding = @($offlineRuntimeNoActivationReport.findings | Where-Object { $_.code -eq 'offline-fault-injection-unverified' })
    if ($offlineRuntimeActivationFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts did not flag missing runtime offline fault activation.' }

    $configOfflinePassRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000016'
    New-Item -ItemType Directory -Force -Path $configOfflinePassRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000016'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-during-config-validation' } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $configOfflinePassRun 'ux-deep-summary.json') -Encoding UTF8
    @(
        '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=none',
        '2026-01-01T00:00:10Z event=FaultProfileSet details=profile=offline',
        '2026-01-01T00:00:20Z event=FaultProfileSet details=profile=none'
    ) | Set-Content -LiteralPath (Join-Path $configOfflinePassRun 'fault-injection-events.log') -Encoding UTF8
    $configOfflinePassAnalysisPath = Join-Path $tempRoot 'config-offline-pass-analysis.json'
    $configOfflinePassOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $configOfflinePassRun -OutputPath $configOfflinePassAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($configOfflinePassOutput -match 'ANALYSIS_REPORT=')) { throw 'Config-offline-pass analysis did not emit ANALYSIS_REPORT.' }
    $configOfflinePassReport = Get-Content -Raw -LiteralPath $configOfflinePassAnalysisPath | ConvertFrom-Json
    if (-not $configOfflinePassReport.clean) { throw 'Analyze-VisualValidationArtifacts required runtime offline freshness for config-only offline validation.' }

    $configOfflineNoActivationRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000017'
    New-Item -ItemType Directory -Force -Path $configOfflineNoActivationRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000017'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-during-config-validation' } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $configOfflineNoActivationRun 'ux-deep-summary.json') -Encoding UTF8
    '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=none' |
        Set-Content -LiteralPath (Join-Path $configOfflineNoActivationRun 'fault-injection-events.log') -Encoding UTF8
    $configOfflineNoActivationAnalysisPath = Join-Path $tempRoot 'config-offline-no-activation-analysis.json'
    $configOfflineNoActivationOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $configOfflineNoActivationRun -OutputPath $configOfflineNoActivationAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($configOfflineNoActivationOutput -match 'ANALYSIS_FINDINGS=1')) { throw "Config-offline-no-activation analysis did not report exactly one finding. Output: $configOfflineNoActivationOutput" }
    $configOfflineNoActivationReport = Get-Content -Raw -LiteralPath $configOfflineNoActivationAnalysisPath | ConvertFrom-Json
    $configOfflineActivationFinding = @($configOfflineNoActivationReport.findings | Where-Object { $_.code -eq 'offline-fault-injection-unverified' })
    if ($configOfflineActivationFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts did not flag missing config-only offline fault activation.' }

    $recoveryPassRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000003'
    New-Item -ItemType Directory -Force -Path $recoveryPassRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000003'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-then-recover-runtime'; TargetCaptureFrames = 2 } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $recoveryPassRun 'ux-deep-summary.json') -Encoding UTF8
    @(
        '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=none',
        '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=offline',
        '2026-01-01T00:05:00Z event=FaultProfileSet details=profile=none'
    ) | Set-Content -LiteralPath (Join-Path $recoveryPassRun 'fault-injection-events.log') -Encoding UTF8
    @(
        '2026-01-01T00:00:00Z | INFO | event=ApplySceneStateComplete / data_freshness_text=LIVE quote feed',
        '2026-01-01T00:00:01Z | INFO | event=RuntimeDataFreshnessChanged / previous_text=LIVE quote feed / data_freshness_text=OFFLINE - showing last values / failure_streak=2 / quote_count=12',
        '2026-01-01T00:00:02Z | INFO | event=RuntimeQuoteRequestFailed / data_freshness_text=OFFLINE - showing last values'
    ) | Set-Content -LiteralPath (Join-Path $recoveryPassRun 'combined-trace-tail.txt') -Encoding UTF8
    @(
        'timestamp=2026-01-01T00:02:00Z frame=1 phase=capture requested_fault_profile=offline-then-recover-runtime effective_fault_profile=offline latest_freshness=OFFLINE - showing last values latest_freshness_source=trace trace_age_seconds=20 ui_freshness=unavailable',
        'timestamp=2026-01-01T00:05:06Z frame=2 phase=after-recovery-clear requested_fault_profile=offline-then-recover-runtime effective_fault_profile=none latest_freshness=LIVE quote feed latest_freshness_source=ui-trace-stale trace_age_seconds=180 ui_freshness=LIVE quote feed',
        'timestamp=2026-01-01T00:05:07Z frame=2 phase=capture requested_fault_profile=offline-then-recover-runtime effective_fault_profile=none latest_freshness=LIVE quote feed latest_freshness_source=ui trace_age_seconds=10 ui_freshness=LIVE quote feed'
    ) | Set-Content -LiteralPath (Join-Path $recoveryPassRun 'runtime-freshness-events.log') -Encoding UTF8
    $recoveryPassAnalysisPath = Join-Path $tempRoot 'recovery-pass-analysis.json'
    $recoveryPassOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $recoveryPassRun -OutputPath $recoveryPassAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($recoveryPassOutput -match 'ANALYSIS_REPORT=')) { throw 'Recovery-pass analysis did not emit ANALYSIS_REPORT.' }
    $recoveryPassReport = Get-Content -Raw -LiteralPath $recoveryPassAnalysisPath | ConvertFrom-Json
    if (-not $recoveryPassReport.clean) { throw 'Analyze-VisualValidationArtifacts reported findings for the offline recovery proof fixture.' }

    $recoveryStaleTraceRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000010'
    New-Item -ItemType Directory -Force -Path $recoveryStaleTraceRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000010'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-then-recover-runtime'; TargetCaptureFrames = 2 } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $recoveryStaleTraceRun 'ux-deep-summary.json') -Encoding UTF8
    @(
        '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=offline',
        '2026-01-01T00:05:00Z event=FaultProfileSet details=profile=none'
    ) | Set-Content -LiteralPath (Join-Path $recoveryStaleTraceRun 'fault-injection-events.log') -Encoding UTF8
    @(
        '2026-01-01T00:00:01Z | INFO | event=RuntimeDataFreshnessChanged / previous_text=LIVE quote feed / data_freshness_text=OFFLINE - showing last values / failure_streak=2 / quote_count=12',
        '2026-01-01T00:00:02Z | INFO | event=RuntimeQuoteRequestFailed / data_freshness_text=OFFLINE - showing last values'
    ) | Set-Content -LiteralPath (Join-Path $recoveryStaleTraceRun 'combined-trace-tail.txt') -Encoding UTF8
    @(
        'timestamp=2026-01-01T00:02:00Z frame=1 phase=capture requested_fault_profile=offline-then-recover-runtime effective_fault_profile=offline latest_freshness=OFFLINE - showing last values latest_freshness_source=trace trace_age_seconds=20 ui_freshness=unavailable',
        'timestamp=2026-01-01T00:05:07Z frame=2 phase=after-recovery-clear requested_fault_profile=offline-then-recover-runtime effective_fault_profile=none latest_freshness=LIVE quote feed latest_freshness_source=ui-trace-stale trace_age_seconds=300 ui_freshness=LIVE quote feed',
        'timestamp=2026-01-01T00:06:00Z frame=3 phase=capture requested_fault_profile=offline-then-recover-runtime effective_fault_profile=none latest_freshness=OFFLINE - showing last values latest_freshness_source=trace trace_age_seconds=350 ui_freshness=unavailable'
    ) | Set-Content -LiteralPath (Join-Path $recoveryStaleTraceRun 'runtime-freshness-events.log') -Encoding UTF8
    $recoveryStaleTraceAnalysisPath = Join-Path $tempRoot 'recovery-stale-trace-analysis.json'
    $recoveryStaleTraceOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $recoveryStaleTraceRun -OutputPath $recoveryStaleTraceAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($recoveryStaleTraceOutput -match 'ANALYSIS_REPORT=')) { throw 'Recovery-stale-trace analysis did not emit ANALYSIS_REPORT.' }
    $recoveryStaleTraceReport = Get-Content -Raw -LiteralPath $recoveryStaleTraceAnalysisPath | ConvertFrom-Json
    $staleTraceFinding = @($recoveryStaleTraceReport.findings | Where-Object { $_.code -eq 'offline-recovery-ux-state-unverified' })
    if ($staleTraceFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts accepted stale trace-backed recovery proof.' }

    $recoveryCombinedOnlyRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000008'
    New-Item -ItemType Directory -Force -Path $recoveryCombinedOnlyRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000008'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-then-recover-runtime'; TargetCaptureFrames = 2 } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $recoveryCombinedOnlyRun 'ux-deep-summary.json') -Encoding UTF8
    @(
        '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=none',
        '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=offline',
        '2026-01-01T00:05:00Z event=FaultProfileSet details=profile=none'
    ) | Set-Content -LiteralPath (Join-Path $recoveryCombinedOnlyRun 'fault-injection-events.log') -Encoding UTF8
    @(
        '2026-01-01T00:00:01Z | INFO | event=RuntimeDataFreshnessChanged / previous_text=LIVE quote feed / data_freshness_text=OFFLINE - showing last values / failure_streak=2 / quote_count=12',
        '2026-01-01T00:00:02Z | INFO | event=RuntimeQuoteRequestFailed / data_freshness_text=OFFLINE - showing last values',
        '2026-01-01T00:05:07Z | INFO | event=RuntimeQuoteApplied / data_freshness_text=LIVE quote feed'
    ) | Set-Content -LiteralPath (Join-Path $recoveryCombinedOnlyRun 'combined-trace-tail.txt') -Encoding UTF8
    $recoveryCombinedOnlyAnalysisPath = Join-Path $tempRoot 'recovery-combined-only-analysis.json'
    $recoveryCombinedOnlyOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $recoveryCombinedOnlyRun -OutputPath $recoveryCombinedOnlyAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($recoveryCombinedOnlyOutput -match 'ANALYSIS_REPORT=')) { throw 'Recovery-combined-only analysis did not emit ANALYSIS_REPORT.' }
    $recoveryCombinedOnlyReport = Get-Content -Raw -LiteralPath $recoveryCombinedOnlyAnalysisPath | ConvertFrom-Json
    $combinedOnlyFinding = @($recoveryCombinedOnlyReport.findings | Where-Object { $_.code -eq 'offline-recovery-ux-state-unverified' })
    if ($combinedOnlyFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts accepted combined-trace-only offline recovery proof.' }

    $recoveryFailRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000004'
    New-Item -ItemType Directory -Force -Path $recoveryFailRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000004'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-then-recover-runtime'; TargetCaptureFrames = 2 } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $recoveryFailRun 'ux-deep-summary.json') -Encoding UTF8
    @(
        '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=none',
        '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=offline'
    ) |
        Set-Content -LiteralPath (Join-Path $recoveryFailRun 'fault-injection-events.log') -Encoding UTF8
    @(
        '2026-01-01T00:00:00Z | INFO | event=ApplySceneStateComplete / data_freshness_text=LIVE quote feed',
        '2026-01-01T00:00:01Z | INFO | event=RuntimeDataFreshnessChanged / previous_text=LIVE quote feed / data_freshness_text=OFFLINE - showing last values / failure_streak=2 / quote_count=12',
        '2026-01-01T00:00:02Z | INFO | event=RuntimeQuoteRequestFailed / data_freshness_text=OFFLINE - showing last values'
    ) | Set-Content -LiteralPath (Join-Path $recoveryFailRun 'combined-trace-tail.txt') -Encoding UTF8
    $recoveryFailAnalysisPath = Join-Path $tempRoot 'recovery-fail-analysis.json'
    $recoveryFailOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $recoveryFailRun -OutputPath $recoveryFailAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    $recoveryFailReport = Get-Content -Raw -LiteralPath $recoveryFailAnalysisPath | ConvertFrom-Json
    $recoveryFinding = @($recoveryFailReport.findings | Where-Object { $_.code -eq 'offline-recovery-ux-state-unverified' })
    if ($recoveryFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts did not flag missing offline recovery proof.' }

    $recoveryPartialFreshnessRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000007'
    New-Item -ItemType Directory -Force -Path $recoveryPartialFreshnessRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000007'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-then-recover-runtime'; TargetCaptureFrames = 2 } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $recoveryPartialFreshnessRun 'ux-deep-summary.json') -Encoding UTF8
    @(
        '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=offline',
        '2026-01-01T00:05:00Z event=FaultProfileSet details=profile=none'
    ) | Set-Content -LiteralPath (Join-Path $recoveryPartialFreshnessRun 'fault-injection-events.log') -Encoding UTF8
    @(
        '2026-01-01T00:00:01Z | INFO | event=RuntimeDataFreshnessChanged / previous_text=LIVE quote feed / data_freshness_text=OFFLINE - showing last values / failure_streak=2 / quote_count=12',
        '2026-01-01T00:00:02Z | INFO | event=RuntimeQuoteRequestFailed / data_freshness_text=OFFLINE - showing last values'
    ) | Set-Content -LiteralPath (Join-Path $recoveryPartialFreshnessRun 'combined-trace-tail.txt') -Encoding UTF8
    @(
        'timestamp=2026-01-01T00:02:00Z frame=1 phase=capture requested_fault_profile=offline-then-recover-runtime effective_fault_profile=offline latest_freshness=OFFLINE - showing last values',
        'timestamp=2026-01-01T00:05:07Z frame=2 phase=capture requested_fault_profile=offline-then-recover-runtime effective_fault_profile=none latest_freshness=OFFLINE - showing last values'
    ) | Set-Content -LiteralPath (Join-Path $recoveryPartialFreshnessRun 'runtime-freshness-events.log') -Encoding UTF8
    $recoveryPartialFreshnessAnalysisPath = Join-Path $tempRoot 'recovery-partial-freshness-analysis.json'
    $recoveryPartialFreshnessOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $recoveryPartialFreshnessRun -OutputPath $recoveryPartialFreshnessAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($recoveryPartialFreshnessOutput -match 'ANALYSIS_REPORT=')) { throw 'Recovery-partial-freshness analysis did not emit ANALYSIS_REPORT.' }
    $recoveryPartialFreshnessReport = Get-Content -Raw -LiteralPath $recoveryPartialFreshnessAnalysisPath | ConvertFrom-Json
    $partialFreshnessFinding = @($recoveryPartialFreshnessReport.findings | Where-Object { $_.code -eq 'offline-recovery-ux-state-unverified' })
    if ($partialFreshnessFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts did not flag partial runtime-freshness recovery proof.' }

    $recoveryMixedSourceRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000009'
    New-Item -ItemType Directory -Force -Path $recoveryMixedSourceRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000009'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-then-recover-runtime'; TargetCaptureFrames = 2 } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $recoveryMixedSourceRun 'ux-deep-summary.json') -Encoding UTF8
    @(
        '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=offline',
        '2026-01-01T00:05:00Z event=FaultProfileSet details=profile=none'
    ) | Set-Content -LiteralPath (Join-Path $recoveryMixedSourceRun 'fault-injection-events.log') -Encoding UTF8
    @(
        '2026-01-01T00:00:01Z | INFO | event=RuntimeDataFreshnessChanged / previous_text=LIVE quote feed / data_freshness_text=OFFLINE - showing last values / failure_streak=2 / quote_count=12',
        '2026-01-01T00:00:02Z | INFO | event=RuntimeQuoteRequestFailed / data_freshness_text=OFFLINE - showing last values',
        '2026-01-01T00:05:07Z | INFO | event=RuntimeQuoteApplied / data_freshness_text=LIVE quote feed'
    ) | Set-Content -LiteralPath (Join-Path $recoveryMixedSourceRun 'combined-trace-tail.txt') -Encoding UTF8
    'timestamp=2026-01-01T00:02:00Z frame=1 phase=capture requested_fault_profile=offline-then-recover-runtime effective_fault_profile=offline latest_freshness=OFFLINE - showing last values' |
        Set-Content -LiteralPath (Join-Path $recoveryMixedSourceRun 'runtime-freshness-events.log') -Encoding UTF8
    $recoveryMixedSourceAnalysisPath = Join-Path $tempRoot 'recovery-mixed-source-analysis.json'
    $recoveryMixedSourceOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $recoveryMixedSourceRun -OutputPath $recoveryMixedSourceAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($recoveryMixedSourceOutput -match 'ANALYSIS_REPORT=')) { throw 'Recovery-mixed-source analysis did not emit ANALYSIS_REPORT.' }
    $recoveryMixedSourceReport = Get-Content -Raw -LiteralPath $recoveryMixedSourceAnalysisPath | ConvertFrom-Json
    $mixedSourceFinding = @($recoveryMixedSourceReport.findings | Where-Object { $_.code -eq 'offline-recovery-ux-state-unverified' })
    if ($mixedSourceFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts accepted cross-file recovery ordering proof.' }

    $recoveryNoActivationRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000005'
    New-Item -ItemType Directory -Force -Path $recoveryNoActivationRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000005'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-then-recover-runtime'; TargetCaptureFrames = 2 } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $recoveryNoActivationRun 'ux-deep-summary.json') -Encoding UTF8
    '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=none' |
        Set-Content -LiteralPath (Join-Path $recoveryNoActivationRun 'fault-injection-events.log') -Encoding UTF8
    'event=ApplySceneStateComplete / data_freshness_text=LIVE quote feed' |
        Set-Content -LiteralPath (Join-Path $recoveryNoActivationRun 'combined-trace-tail.txt') -Encoding UTF8
    $recoveryNoActivationAnalysisPath = Join-Path $tempRoot 'recovery-no-activation-analysis.json'
    $recoveryNoActivationOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $recoveryNoActivationRun -OutputPath $recoveryNoActivationAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($recoveryNoActivationOutput -match 'ANALYSIS_REPORT=')) { throw 'Recovery-no-activation analysis did not emit ANALYSIS_REPORT.' }
    $recoveryNoActivationReport = Get-Content -Raw -LiteralPath $recoveryNoActivationAnalysisPath | ConvertFrom-Json
    $missingActivationFindings = @($recoveryNoActivationReport.findings | Where-Object { $_.code -eq 'offline-fault-injection-unverified' })
    if ($missingActivationFindings.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts did not flag missing offline activation for recovery profile.' }

    $recoveryInsufficientRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000006'
    New-Item -ItemType Directory -Force -Path $recoveryInsufficientRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000006'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-then-recover-runtime'; TargetCaptureFrames = 1 } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $recoveryInsufficientRun 'ux-deep-summary.json') -Encoding UTF8
    @(
        '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=offline',
        '2026-01-01T00:05:00Z event=FaultProfileSet details=profile=none'
    ) | Set-Content -LiteralPath (Join-Path $recoveryInsufficientRun 'fault-injection-events.log') -Encoding UTF8
    @(
        '2026-01-01T00:00:01Z | INFO | event=RuntimeDataFreshnessChanged / previous_text=LIVE quote feed / data_freshness_text=OFFLINE - showing last values / failure_streak=2 / quote_count=12',
        '2026-01-01T00:00:02Z | INFO | event=RuntimeQuoteRequestFailed / data_freshness_text=OFFLINE - showing last values',
        '2026-01-01T00:05:07Z | INFO | event=RuntimeQuoteApplied / data_freshness_text=LIVE quote feed'
    ) | Set-Content -LiteralPath (Join-Path $recoveryInsufficientRun 'combined-trace-tail.txt') -Encoding UTF8
    $recoveryInsufficientAnalysisPath = Join-Path $tempRoot 'recovery-insufficient-analysis.json'
    $recoveryInsufficientOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $recoveryInsufficientRun -OutputPath $recoveryInsufficientAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($recoveryInsufficientOutput -match 'ANALYSIS_REPORT=')) { throw 'Recovery-insufficient analysis did not emit ANALYSIS_REPORT.' }
    $recoveryInsufficientReport = Get-Content -Raw -LiteralPath $recoveryInsufficientAnalysisPath | ConvertFrom-Json
    $insufficientFinding = @($recoveryInsufficientReport.findings | Where-Object { $_.code -eq 'offline-recovery-insufficient-captures' })
    if ($insufficientFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts did not flag insufficient recovery capture frames.' }

    $captureStarvedRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000011'
    New-Item -ItemType Directory -Force -Path $captureStarvedRun | Out-Null
    @{
        ResultName = 'ux-deep-ssh-20990101-000011'
        ConfigPhaseStatus = 'Completed'
        DesktopPhaseStatus = 'Completed'
        FullScreenToggleStatus = 'Completed'
        DesktopShots = 3
        TargetCaptureFrames = 180
        Notes = @('Desktop capture count 3 was below 80 percent of estimated target 180; capture loop remained wall-clock bounded.')
    } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $captureStarvedRun 'ux-deep-summary.json') -Encoding UTF8
    $captureStarvedAnalysisPath = Join-Path $tempRoot 'capture-starved-with-note-analysis.json'
    $captureStarvedOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $captureStarvedRun -OutputPath $captureStarvedAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($captureStarvedOutput -match 'ANALYSIS_REPORT=')) { throw 'Capture-starved analysis did not emit ANALYSIS_REPORT.' }
    $captureStarvedReport = Get-Content -Raw -LiteralPath $captureStarvedAnalysisPath | ConvertFrom-Json
    $captureStarvedFinding = @($captureStarvedReport.findings | Where-Object { $_.code -eq 'capture-loop-starved' })
    if ($captureStarvedFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts did not flag capture-loop starvation with a low-yield note present.' }

    $captureStarvedRatioRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000012'
    New-Item -ItemType Directory -Force -Path $captureStarvedRatioRun | Out-Null
    @{
        ResultName = 'ux-deep-ssh-20990101-000012'
        ConfigPhaseStatus = 'Completed'
        DesktopPhaseStatus = 'Completed'
        FullScreenToggleStatus = 'Completed'
        DesktopShots = 10
        TargetCaptureFrames = 180
        Notes = @()
    } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $captureStarvedRatioRun 'ux-deep-summary.json') -Encoding UTF8
    $captureStarvedRatioAnalysisPath = Join-Path $tempRoot 'capture-starved-ratio-analysis.json'
    $captureStarvedRatioOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $captureStarvedRatioRun -OutputPath $captureStarvedRatioAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($captureStarvedRatioOutput -match 'ANALYSIS_REPORT=')) { throw 'Capture-starved ratio analysis did not emit ANALYSIS_REPORT.' }
    $captureStarvedRatioReport = Get-Content -Raw -LiteralPath $captureStarvedRatioAnalysisPath | ConvertFrom-Json
    $captureStarvedRatioFinding = @($captureStarvedRatioReport.findings | Where-Object { $_.code -eq 'capture-loop-starved' })
    if ($captureStarvedRatioFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts did not flag ratio-only capture-loop starvation.' }

    $shortCaptureRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000013'
    New-Item -ItemType Directory -Force -Path $shortCaptureRun | Out-Null
    @{
        ResultName = 'ux-deep-ssh-20990101-000013'
        ConfigPhaseStatus = 'Completed'
        DesktopPhaseStatus = 'Completed'
        FullScreenToggleStatus = 'Completed'
        DesktopShots = 1
        TargetCaptureFrames = 5
        Notes = @()
    } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $shortCaptureRun 'ux-deep-summary.json') -Encoding UTF8
    $shortCaptureAnalysisPath = Join-Path $tempRoot 'short-capture-analysis.json'
    $shortCaptureOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $shortCaptureRun -OutputPath $shortCaptureAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($shortCaptureOutput -match 'ANALYSIS_REPORT=')) { throw 'Short-capture analysis did not emit ANALYSIS_REPORT.' }
    $shortCaptureReport = Get-Content -Raw -LiteralPath $shortCaptureAnalysisPath | ConvertFrom-Json
    $shortCaptureFinding = @($shortCaptureReport.findings | Where-Object { $_.code -eq 'capture-loop-starved' })
    if ($shortCaptureFinding.Count -ne 0) { throw 'Analyze-VisualValidationArtifacts flagged capture-loop starvation for a short run below threshold.' }

    $sufficientCaptureRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000014'
    New-Item -ItemType Directory -Force -Path $sufficientCaptureRun | Out-Null
    @{
        ResultName = 'ux-deep-ssh-20990101-000014'
        ConfigPhaseStatus = 'Completed'
        DesktopPhaseStatus = 'Completed'
        FullScreenToggleStatus = 'Completed'
        DesktopShots = 8
        TargetCaptureFrames = 10
        Notes = @()
    } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $sufficientCaptureRun 'ux-deep-summary.json') -Encoding UTF8
    $sufficientCaptureAnalysisPath = Join-Path $tempRoot 'sufficient-capture-analysis.json'
    $sufficientCaptureOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $sufficientCaptureRun -OutputPath $sufficientCaptureAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($sufficientCaptureOutput -match 'ANALYSIS_REPORT=')) { throw 'Sufficient-capture analysis did not emit ANALYSIS_REPORT.' }
    $sufficientCaptureReport = Get-Content -Raw -LiteralPath $sufficientCaptureAnalysisPath | ConvertFrom-Json
    $sufficientCaptureFinding = @($sufficientCaptureReport.findings | Where-Object { $_.code -eq 'capture-loop-starved' })
    if ($sufficientCaptureFinding.Count -ne 0) { throw 'Analyze-VisualValidationArtifacts flagged capture-loop starvation at the 80 percent threshold.' }

    $missingCaptureCountRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000015'
    New-Item -ItemType Directory -Force -Path $missingCaptureCountRun | Out-Null
    @{
        ResultName = 'ux-deep-ssh-20990101-000015'
        ConfigPhaseStatus = 'Completed'
        DesktopPhaseStatus = 'Completed'
        FullScreenToggleStatus = 'Completed'
        TargetCaptureFrames = 180
        Notes = @('Desktop capture count 3 was below 80 percent of estimated target 180; capture loop remained wall-clock bounded.')
    } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $missingCaptureCountRun 'ux-deep-summary.json') -Encoding UTF8
    $missingCaptureCountAnalysisPath = Join-Path $tempRoot 'missing-capture-count-analysis.json'
    $missingCaptureCountOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $missingCaptureCountRun -OutputPath $missingCaptureCountAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($missingCaptureCountOutput -match 'ANALYSIS_REPORT=')) { throw 'Missing-capture-count analysis did not emit ANALYSIS_REPORT.' }
    $missingCaptureCountReport = Get-Content -Raw -LiteralPath $missingCaptureCountAnalysisPath | ConvertFrom-Json
    $missingCaptureCountFinding = @($missingCaptureCountReport.findings | Where-Object { $_.code -eq 'capture-loop-starved' })
    if ($missingCaptureCountFinding.Count -ne 0) { throw 'Analyze-VisualValidationArtifacts flagged capture-loop starvation when DesktopShots was absent.' }

    $auditFixture = Join-Path $tempRoot 'audit-state.json'
    @{
        pending_next_build_issues = @(
            @{ id = 'CR-091'; tracking_number = 'CR-091'; status = 'open'; title = 'Existing CR' }
        )
        current_priority_backlog = @(
            @{ id = 'CR-999'; tracking_number = 'CR-999'; status = 'open'; title = 'Unrelated umbrella must not affect allocation' }
        )
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $auditFixture -Encoding UTF8
    $crOutput = & (Join-Path $repoRoot 'build\validation\Add-AuditChangeRequest.ps1') `
        -AuditPath $auditFixture `
        -Title 'Smoke-created CR' `
        -Area 'validation_smoke' `
        -Severity 'Medium' `
        -Priority 1 `
        -Evidence @('Synthetic evidence')
    $expectedCurrentSchemaId = 'CR-092'
    if (-not ($crOutput -match "CHANGE_REQUEST_ID=$expectedCurrentSchemaId")) { throw "Add-AuditChangeRequest did not allocate $expectedCurrentSchemaId from current audit schema. Output: $crOutput" }
    $auditAfter = Get-Content -Raw -LiteralPath $auditFixture | ConvertFrom-Json
    $created = @($auditAfter.pending_next_build_issues | Where-Object { $_.id -eq $expectedCurrentSchemaId })
    if ($created.Count -ne 1) { throw 'Add-AuditChangeRequest did not append exactly one pending_next_build_issues entry.' }

    $legacyAuditFixture = Join-Path $tempRoot 'legacy-audit-state.json'
    @{
        change_requests = @(
            @{ id = 'CR-010'; tracking_number = 'CR-010'; status = 'open'; title = 'Legacy CR' }
        )
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $legacyAuditFixture -Encoding UTF8
    $legacyCrOutput = & (Join-Path $repoRoot 'build\validation\Add-AuditChangeRequest.ps1') `
        -AuditPath $legacyAuditFixture `
        -Title 'Smoke-created legacy CR' `
        -Area 'validation_smoke' `
        -Severity 'Medium' `
        -Priority 1 `
        -Evidence @('Synthetic evidence')
    if (-not ($legacyCrOutput -match 'CHANGE_REQUEST_ID=CR-011')) { throw "Add-AuditChangeRequest did not preserve legacy change_requests schema. Output: $legacyCrOutput" }
    $legacyAuditAfter = Get-Content -Raw -LiteralPath $legacyAuditFixture | ConvertFrom-Json
    $legacyCreated = @($legacyAuditAfter.change_requests | Where-Object { $_.id -eq 'CR-011' })
    if ($legacyCreated.Count -ne 1) { throw 'Add-AuditChangeRequest did not append exactly one legacy change_requests entry.' }

    $dualAuditFixture = Join-Path $tempRoot 'dual-audit-state.json'
    @{
        pending_next_build_issues = $null
        change_requests = @(
            @{ id = 'CR-020'; tracking_number = 'CR-020'; status = 'open'; title = 'Legacy CR' }
        )
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $dualAuditFixture -Encoding UTF8
    $dualCrOutput = & (Join-Path $repoRoot 'build\validation\Add-AuditChangeRequest.ps1') `
        -AuditPath $dualAuditFixture `
        -Title 'Smoke-created current-preferred CR' `
        -Area 'validation_smoke' `
        -Severity 'Medium' `
        -Priority 1 `
        -Evidence @('Synthetic evidence')
    if (-not ($dualCrOutput -match 'CHANGE_REQUEST_ID=CR-021')) { throw "Add-AuditChangeRequest did not allocate CR-021 from dual schema. Output: $dualCrOutput" }
    $dualAuditAfter = Get-Content -Raw -LiteralPath $dualAuditFixture | ConvertFrom-Json
    $dualCreated = @($dualAuditAfter.pending_next_build_issues | Where-Object { $_.id -eq 'CR-021' })
    if ($dualCreated.Count -ne 1) { throw 'Add-AuditChangeRequest did not prefer pending_next_build_issues in a dual-schema audit file.' }
    $legacyCount = @($dualAuditAfter.change_requests | Where-Object { $null -ne $_ }).Count
    if ($legacyCount -ne 1) { throw 'Add-AuditChangeRequest unexpectedly modified legacy change_requests in a dual-schema audit file.' }

    $invalidAuditFixture = Join-Path $tempRoot 'invalid-audit-state.json'
    @{ unrelated = @() } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $invalidAuditFixture -Encoding UTF8
    $invalidSucceeded = $false
    try {
        & (Join-Path $repoRoot 'build\validation\Add-AuditChangeRequest.ps1') `
            -AuditPath $invalidAuditFixture `
            -Title 'Should fail' `
            -Area 'validation_smoke' `
            -Severity 'Medium' `
            -Evidence @('Synthetic evidence') | Out-Null
        $invalidSucceeded = $true
    }
    catch {
        if ($_.Exception.Message -notmatch 'pending_next_build_issues or change_requests') {
            throw
        }
    }
    if ($invalidSucceeded) { throw 'Add-AuditChangeRequest accepted an invalid audit schema.' }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}

Write-Output 'VALIDATION_SCRIPT_SMOKE_TEST=Passed'
