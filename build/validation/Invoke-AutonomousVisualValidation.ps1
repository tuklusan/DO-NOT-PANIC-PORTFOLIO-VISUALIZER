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
# -SkipDeepSeekReview was intentionally removed; see AGENTS.md for the mandatory DeepSeek gate policy.
param(
    [ValidateRange(1, 100)][int]$VmCycles = 2,
    [ValidateRange(1, 100)][int]$RequiredConsecutiveCleanRuns = 2,
    [ValidateRange(1, 10080)][int]$GuestScreensaverDurationMinutes = 30,
    [ValidateRange(1, 3600)][int]$CaptureIntervalSeconds = 10,
    [ValidateSet('none', 'offline-at-start', 'offline-during-config-validation', 'offline-during-runtime', 'offline-then-recover-runtime', 'high-latency-yfinance', 'upstream-throttled', 'timeout')]
    [string[]]$FaultProfiles = @('none'),
    [string]$VmHost = $env:PORTFOLIOSAVER_VM_HOST,
    [int]$VmPort = $(if ($env:PORTFOLIOSAVER_VM_PORT) { [int]$env:PORTFOLIOSAVER_VM_PORT } else { 22 }),
    [string]$RootPath = 'C:\vmharness\portfolio-saver',
    [switch]$SkipLocalTests,
    [switch]$SkipVm,
    [switch]$CreateChangeRequests,
    [switch]$CommitBeforeValidation,
    [switch]$PushBeforeValidation,
    [switch]$AcknowledgeExternalReviewSecretScan,
    [string]$CommitMessage = 'Add autonomous visual validation loop',
    [string[]]$CommitPaths = @('.gitignore','AGENTS.md','README.md','build/validation','build/vm')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactRoot = Join-Path $repoRoot 'build\validation\artifacts'
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

function Invoke-CheckedCommand {
    param([string]$FilePath,[string[]]$Arguments,[string]$Label)
    Write-Host "[$(Get-Date -Format o)] $Label"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE." }
}

function Assert-CommandAvailable {
    param([string]$CommandName,[string]$InstallHint)
    if ($null -eq (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$CommandName' is not available. $InstallHint"
    }
}

function Get-PendingChangePaths {
    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($line in @(& git status --porcelain=v1)) {
        if ($line.Length -le 3) { continue }
        $path = $line.Substring(3).Trim()
        if ($path -match ' -> ') { $path = ($path -split ' -> ')[-1].Trim() }
        if (-not [string]::IsNullOrWhiteSpace($path)) { [void]$paths.Add($path) }
    }
    return @($paths | Select-Object -Unique)
}

function Test-PathWithinCommitPaths {
    param([string]$Path)
    $normalized = $Path.Replace('\', '/').TrimEnd('/')
    foreach ($commitPath in $CommitPaths) {
        $allowed = $commitPath.Replace('\', '/').TrimEnd('/')
        if ($normalized.Equals($allowed, [StringComparison]::OrdinalIgnoreCase) -or $normalized.StartsWith($allowed + '/', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Test-ProbablyTextFile {
    param([string]$Path)
    $extension = [IO.Path]::GetExtension($Path).ToLowerInvariant()
    return $extension -notin @('.png','.jpg','.jpeg','.gif','.ico','.bmp','.zip','.7z','.exe','.dll','.pdb','.bin','.scr')
}

function Assert-NoSecretRiskInPendingChanges {
    $pendingPaths = @(Get-PendingChangePaths)
    $secretPathPattern = '(?i)(secret|credential|api[-_]?key|token|password|private|test-secrets\.json|\.env$|\.pem$|\.pfx$)'
    $badPaths = @($pendingPaths | Where-Object { $_ -match $secretPathPattern })
    if ($badPaths.Count -gt 0) { throw "Pending path(s) look secret-bearing; refusing autonomous review/commit: $($badPaths -join ', ')" }

    $secretValuePattern = '(?i)(authorization:\s*bearer\s+[A-Za-z0-9._-]{20,}|(?:export\s+)?[A-Za-z0-9_]*(api[_-]?key|password|token|secret)[A-Za-z0-9_]*\s*[:=]\s*(["''][^"'']{8,}|\S{8,}))'
    $unstagedDiff = @(& git diff --no-ext-diff --unified=0 -- . ':!build/deepseek-review/**')
    $stagedDiff = @(& git diff --cached --no-ext-diff --unified=0 -- . ':!build/deepseek-review/**')
    $diffHits = @( ($unstagedDiff + $stagedDiff) | Select-String -Pattern $secretValuePattern | Select-Object -First 5)
    if ($diffHits.Count -gt 0) { throw "Pending diff contains secret-like content; refusing autonomous review/commit. First hit: $($diffHits[0].Line.Trim())" }

    foreach ($relativePath in $pendingPaths) {
        if (-not (Test-Path -LiteralPath $relativePath -PathType Leaf)) { continue }
        if (-not (Test-ProbablyTextFile -Path $relativePath)) { throw "Pending non-text file cannot be reviewed/secret-scanned autonomously: $relativePath" }
        $contentHits = @(Select-String -LiteralPath $relativePath -Pattern $secretValuePattern -ErrorAction SilentlyContinue | Select-Object -First 3)
        if ($contentHits.Count -gt 0) { throw "Pending file contains secret-like content; refusing autonomous review/commit: $relativePath" }
    }
}

function Invoke-DeepSeekGate {
    if (-not $AcknowledgeExternalReviewSecretScan) { throw 'External DeepSeek review requires -AcknowledgeExternalReviewSecretScan after local secret-risk checks.' }
    Assert-CommandAvailable -CommandName 'pwsh' -InstallHint 'Install PowerShell 7 before running autonomous validation.'
    $reviewScriptPath = Join-Path $repoRoot 'build\Run-DeepSeekCodeReview.ps1'
    if (-not (Test-Path -LiteralPath $reviewScriptPath)) { throw "DeepSeek review script is missing: $reviewScriptPath" }
    $workflowGatePath = Join-Path $repoRoot 'build\Test-DeepSeekWorkflowGate.ps1'
    if (-not (Test-Path -LiteralPath $workflowGatePath)) { throw "DeepSeek workflow gate script is missing: $workflowGatePath" }
    Invoke-CheckedCommand -FilePath 'pwsh' -Arguments @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File','.\build\Test-DeepSeekWorkflowGate.ps1') -Label 'DeepSeek live workflow gate'
    Assert-NoSecretRiskInPendingChanges
    Invoke-CheckedCommand -FilePath 'pwsh' -Arguments @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File','.\build\Run-DeepSeekCodeReview.ps1','-SendForReview','-AcknowledgeSecretScan','-IncludeUntracked','-MaxTokens','8192') -Label 'DeepSeek review gate'
}

function Invoke-ValidationCheckpoint {
    $pendingPaths = @(Get-PendingChangePaths)
    if ($pendingPaths.Count -eq 0) { Write-Host "[$(Get-Date -Format o)] No local changes to commit before local/VM validation."; return }
    $outsideCommitPaths = @($pendingPaths | Where-Object { -not (Test-PathWithinCommitPaths -Path $_) })
    if ($outsideCommitPaths.Count -gt 0) { throw ('Pending changes outside CommitPaths would make reviewed/validated state inconsistent: ' + ($outsideCommitPaths -join ', ')) }
    if (-not $CommitBeforeValidation) { throw 'Pending changes exist. Rerun with -CommitBeforeValidation after review, or commit the intended changes before starting validation.' }
    Assert-NoSecretRiskInPendingChanges
    Invoke-CheckedCommand -FilePath 'git' -Arguments (@('add','--') + $CommitPaths) -Label 'Stage declared validation workflow paths'
    $staged = @(& git diff --cached --name-only)
    if ($staged.Count -eq 0) { Write-Host 'No staged changes after declared-path staging; skipping commit.'; return }
    Invoke-CheckedCommand -FilePath 'git' -Arguments @('commit','-m',$CommitMessage) -Label 'Commit before local/VM validation'
    if ($PushBeforeValidation) { Invoke-CheckedCommand -FilePath 'git' -Arguments @('push') -Label 'Push before local/VM validation' }
}

function Assert-VmTargetConfigured {
    if ($SkipVm) { return }
    if ([string]::IsNullOrWhiteSpace($VmHost)) { throw 'VM validation requested but no VM host is configured. Pass -VmHost or set PORTFOLIOSAVER_VM_HOST.' }
    $vmScriptPath = Join-Path $repoRoot 'build\vm\Invoke-VmBuildTest.ps1'
    if (-not (Test-Path -LiteralPath $vmScriptPath)) { throw "VM validation script is missing: $vmScriptPath" }
    $parseErrors = $null
    $tokens = $null
    $vmScriptAst = [System.Management.Automation.Language.Parser]::ParseFile($vmScriptPath, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) { throw "VM validation script '$vmScriptPath' could not be parsed." }
    $paramBlock = $vmScriptAst.ParamBlock
    if ($null -eq $paramBlock) { throw "VM validation script '$vmScriptPath' has no param block; FaultProfile parameter is required." }
    $hasFaultProfile = @($paramBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath } | Where-Object { $_ -ieq 'FaultProfile' }).Count -gt 0
    if (-not $hasFaultProfile) {
        throw "VM validation script '$vmScriptPath' must expose a FaultProfile parameter for degraded-mode matrix runs."
    }
}

function Save-ValidationCycleSummary {
    param($Cycles,[int]$ConsecutiveClean)
    $cycleArray = if ($null -eq $Cycles) { @() } else { @(foreach ($cycle in $Cycles) { $cycle }) }
    $summary = [ordered]@{
        generatedAt = (Get-Date).ToString('o')
        requiredConsecutiveCleanRuns = $RequiredConsecutiveCleanRuns
        consecutiveCleanRuns = $ConsecutiveClean
        vmCyclesRequested = $VmCycles
        guestScreensaverDurationMinutes = $GuestScreensaverDurationMinutes
        captureIntervalSeconds = $CaptureIntervalSeconds
        faultProfiles = @($FaultProfiles)
        completed = ($ConsecutiveClean -ge $RequiredConsecutiveCleanRuns)
        cycles = $cycleArray
    }
    $path = Join-Path $artifactRoot ('autonomous-visual-validation-summary-{0}.json' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $summary | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $path -Encoding UTF8
    Write-Output ("AUTONOMOUS_VALIDATION_SUMMARY=" + $path)
}

Push-Location $repoRoot
try {
    if ($RequiredConsecutiveCleanRuns -gt $VmCycles) { throw 'RequiredConsecutiveCleanRuns cannot exceed VmCycles.' }
    # Keep this explicit so a future default-value edit cannot create an empty VM fault matrix.
    if ($FaultProfiles.Count -eq 0) { throw 'FaultProfiles must contain at least one profile.' }
    Assert-VmTargetConfigured
    Invoke-DeepSeekGate
    Invoke-ValidationCheckpoint
    Invoke-CheckedCommand -FilePath 'dotnet' -Arguments @('restore','.\DoNotPanicPortfolioVisualizer.sln','--disable-parallel','--nologo') -Label 'Local restore'
    Invoke-CheckedCommand -FilePath 'dotnet' -Arguments @('build','.\DoNotPanicPortfolioVisualizer.sln','-c','Release','--nologo','--no-restore') -Label 'Local Release build'
    if (-not $SkipLocalTests) { Invoke-CheckedCommand -FilePath 'dotnet' -Arguments @('test','.\tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj','-c','Release','--nologo','--no-build') -Label 'Local Release tests' }
    $cycles = New-Object System.Collections.Generic.List[object]
    $consecutiveClean = 0
    if ($SkipVm) { Save-ValidationCycleSummary -Cycles $cycles -ConsecutiveClean $consecutiveClean; return }
    for ($cycle = 1; $cycle -le $VmCycles -and $consecutiveClean -lt $RequiredConsecutiveCleanRuns; $cycle++) {
        $cycleFaultProfile = $FaultProfiles[($cycle - 1) % $FaultProfiles.Count]
        Write-Host "[$(Get-Date -Format o)] VM UX validation cycle $cycle of $VmCycles using FaultProfile=$cycleFaultProfile"
        $started = Get-Date
        $vmOutput = & .\build\vm\Invoke-VmBuildTest.ps1 -VmHost $VmHost -VmPort $VmPort -RootPath $RootPath -PushWorkspace -RunUxDeep -GuestScreensaverDurationMinutes $GuestScreensaverDurationMinutes -CaptureIntervalSeconds $CaptureIntervalSeconds -FaultProfile $cycleFaultProfile -UxTimeoutSeconds ([Math]::Max(2400, ($GuestScreensaverDurationMinutes * 60) + 1800))
        if ($LASTEXITCODE -ne 0) { throw "VM UX validation cycle $cycle failed with exit code $LASTEXITCODE." }
        $resultLine = [string[]]@($vmOutput | Where-Object { $_ -like 'LOCAL_RESULT_DIR=*' } | Select-Object -Last 1)
        if ($resultLine.Count -eq 0) { throw "VM UX validation cycle $cycle did not report LOCAL_RESULT_DIR." }
        $resultDir = $resultLine[0].Substring('LOCAL_RESULT_DIR='.Length)
        if ([string]::IsNullOrWhiteSpace($resultDir) -or -not (Test-Path -LiteralPath $resultDir -PathType Container)) { throw "VM UX validation cycle $cycle reported an invalid LOCAL_RESULT_DIR: $resultDir" }
        $analysisPath = Join-Path $artifactRoot ('visual-validation-cycle-{0:D2}-{1}.json' -f $cycle, (Get-Date -Format 'yyyyMMdd-HHmmss'))
        $analysisOutput = & .\build\validation\Analyze-VisualValidationArtifacts.ps1 -ResultRoot $resultDir -OutputPath $analysisPath -CreateChangeRequests:$CreateChangeRequests
        if ($LASTEXITCODE -ne 0) { throw "Artifact analysis failed for VM UX validation cycle $cycle." }
        $analysisOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { Write-Host $_ }
        $cleanLine = [string[]]@($analysisOutput | Where-Object { $_ -like 'ANALYSIS_CLEAN=*' } | Select-Object -Last 1)
        $isClean = ($cleanLine.Count -gt 0 -and $cleanLine[0].Substring('ANALYSIS_CLEAN='.Length) -eq 'True')
        if ($isClean) { $consecutiveClean++ } else { $consecutiveClean = 0 }
        [void]$cycles.Add([pscustomobject]@{ cycle=$cycle; faultProfile=$cycleFaultProfile; startedAt=$started.ToString('o'); finishedAt=(Get-Date).ToString('o'); resultDir=$resultDir; analysisPath=$analysisPath; clean=$isClean; consecutiveCleanAfterCycle=$consecutiveClean })
    }
    Save-ValidationCycleSummary -Cycles $cycles -ConsecutiveClean $consecutiveClean
    if ($consecutiveClean -lt $RequiredConsecutiveCleanRuns) { throw "Autonomous validation ended without $RequiredConsecutiveCleanRuns consecutive clean VM runs." }
}
finally { Pop-Location }
