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
    [Parameter(Mandatory = $true)][string]$AnalysisReportPath,
    [string]$OutputPath,
    [string]$Endpoint = "https://api.deepseek.com",
    [string]$Model = "",
    [long]$MaxArtifactFileBytes = 52428800,
    [int]$MaxArtifactCharacters = 200000,
    [int]$MaxResponseCharacters = 400000,
    [int]$MaxTokens = 8192,
    [switch]$AcknowledgeEndpointOverride
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This script sends bounded test-artifact excerpts to DeepSeek for advisory review.
# Artifact producers must not write credentials to traces/logs; this script also
# performs best-effort secret scanning and redacts nothing automatically.

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$commonPath = Join-Path $repoRoot 'build\DeepSeekWorkflowCommon.ps1'
if (-not (Test-Path -LiteralPath $commonPath)) { throw "Missing DeepSeek workflow common module: $commonPath" }
. $commonPath
if ($null -eq (Get-Command Get-RepoRoot -ErrorAction SilentlyContinue)) {
    throw "DeepSeek workflow common module did not export Get-RepoRoot: $commonPath"
}
if ($null -eq (Get-Command Get-DeepSeekApiKey -ErrorAction SilentlyContinue)) {
    throw "DeepSeek workflow common module did not export Get-DeepSeekApiKey: $commonPath"
}

function Assert-GitIgnored([string]$Path, [string]$FailureMessage) {
    if ($null -eq (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "Git is required to verify ignored DeepSeek artifact-review output paths. $FailureMessage"
    }

    & git check-ignore -q -- $Path
    if ($LASTEXITCODE -ne 0) { throw $FailureMessage }
}

function Get-TextSample {
    param([string]$Path,[int]$MaxCharacters = 12000)
    try {
        $item = Get-Item -LiteralPath $Path -ErrorAction Stop
        if ($item.Length -gt $MaxArtifactFileBytes) {
            return "[skipped: file exceeds MaxArtifactFileBytes=$MaxArtifactFileBytes; bytes=$($item.Length)]"
        }

        $text = Get-Content -Raw -LiteralPath $Path -ErrorAction Stop
        if ($text.Length -le $MaxCharacters) { return $text }
        $cut = $text.LastIndexOf("`n", [Math]::Min($MaxCharacters, $text.Length) - 1)
        if ($cut -lt 1) { $cut = [Math]::Min($MaxCharacters, $text.Length) }
        return $text.Substring(0, $cut) + "`n...[line-safe truncated by Invoke-DeepSeekArtifactReview.ps1]..."
    }
    catch {
        return "[unreadable: $($_.Exception.Message)]"
    }
}

function Get-InterestingTextSample {
    param([string]$Path)
    $item = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -ne $item -and $item.Length -gt $MaxArtifactFileBytes) {
        return "[skipped: file exceeds MaxArtifactFileBytes=$MaxArtifactFileBytes; bytes=$($item.Length)]"
    }

    $highSignalPattern = '(?i)\b(network_lost|faultprofileset|runtimequoterequestfailed|clientresponseerror|quoterequestfailed|clientoperationerror)\b|\bsimulated network outage\b'
    $interestingPattern = '(?i)\b(error|fatal|exception|failed|timeout|warning|warn|missing|blank|jitter|burst|unhandled|closed|close|validation)\b|' + $highSignalPattern
    $allHits = @(Select-String -LiteralPath $Path -Pattern $interestingPattern -ErrorAction SilentlyContinue)
    # Keep the review packet bounded while preserving startup context, targeted degradation evidence, and final state.
    $selectedHits = @(
        $allHits | Select-Object -First 80
        $allHits | Where-Object { $_.Line -match $highSignalPattern } | Select-Object -First 80
        $allHits | Select-Object -Last 80
    )
    $hits = @($selectedHits | Sort-Object Path, LineNumber -Unique)
    if ($hits.Count -eq 0) { return Get-TextSample -Path $Path -MaxCharacters 6000 }
    $sample = (($hits | ForEach-Object { "{0}:{1}: {2}" -f (Split-Path -Leaf $Path), $_.LineNumber, $_.Line.Trim() }) -join "`n")
    if ($sample.Length -le 24000) { return $sample }
    $cut = $sample.LastIndexOf("`n", 23999)
    if ($cut -lt 1) { $cut = 24000 }
    return $sample.Substring(0, $cut) + "`n...[artifact interesting-line sample truncated at 24000 characters]..."
}

function Assert-NoLikelySecrets([string]$Text) {
    $patterns = @(
        '(?im)(api[_-]?key|secret|token|password)\s*[:=]\s*[''"]?(?!(test|example|placeholder|dummy|sample|REPLACE_WITH_))([A-Za-z0-9_\-+/=]{16,})[''"]?',
        '(?im)Authorization\s*[:=]\s*[''"]?Bearer\s+(sk-[A-Za-z0-9_-]{20,}|[A-Za-z0-9_\-+/=]{32,})[''"]?',
        '(?im)sk-(?!test|example|placeholder|dummy|sample)[A-Za-z0-9_-]{20,}',
        '(?im)(password|pwd|user id|uid)\s*[:=]\s*[''"]?[^;,\r\n''"]{8,}[''"]?',
        '(?s)-----BEGIN [A-Z ]*PRIVATE KEY-----.*?-----END [A-Z ]*PRIVATE KEY-----'
    )
    foreach ($pattern in $patterns) {
        if ($Text -match $pattern) {
            throw 'Potential secret material detected in DeepSeek artifact-review packet. Redact artifacts before sending.'
        }
    }
}

Push-Location $repoRoot
try {
    $envModel = [Environment]::GetEnvironmentVariable('DEEPSEEK_MODEL')
    if (-not $PSBoundParameters.ContainsKey('Model') -and -not [string]::IsNullOrWhiteSpace($envModel)) {
        $Model = $envModel
    }
    if ([string]::IsNullOrWhiteSpace($Model)) {
        # Keep this aligned with build/Run-DeepSeekCodeReview.ps1 and build/Test-DeepSeekWorkflowGate.ps1.
        $Model = 'deepseek-v4-flash'
    }

    $resolvedResultRoot = (Resolve-Path -LiteralPath $ResultRoot).Path
    $resolvedAnalysisPath = (Resolve-Path -LiteralPath $AnalysisReportPath).Path
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $OutputPath = Join-Path $repoRoot ('build\validation\artifacts\deepseek-artifact-review-{0}.md' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Path $OutputPath -Parent) | Out-Null

    $analysisText = Get-TextSample -Path $resolvedAnalysisPath -MaxCharacters 50000
    $artifactFiles = @(Get-ChildItem -LiteralPath $resolvedResultRoot -File -Recurse -ErrorAction SilentlyContinue)
    $screenshotLines = @($artifactFiles |
        Where-Object { $_.Extension -match '(?i)^\.(png|jpg|jpeg)$' } |
        Select-Object -First 80 |
        ForEach-Object { "image: $($_.FullName.Substring($resolvedResultRoot.Length).TrimStart('\','/')) bytes=$($_.Length) modified=$($_.LastWriteTime.ToString('o'))" })
    $sceneCaptureTraceLines = @($artifactFiles |
        Where-Object { $_.Name -match '(?i)^trace\.circular\.log$' } |
        ForEach-Object {
            Select-String -LiteralPath $_.FullName -Pattern 'SceneCaptureComplete|SceneCaptureCleanupQueued' -ErrorAction SilentlyContinue |
                Select-Object -First 120 |
                ForEach-Object { "{0}:{1}: {2}" -f (Split-Path -Leaf $_.Path), $_.LineNumber, $_.Line.Trim() }
        })
    $textArtifactSections = New-Object System.Collections.Generic.List[string]
    foreach ($artifact in @($artifactFiles | Where-Object { $_.Name -match '(?i)(trace|circular|events|log|summary|json|txt|csv)' } | Sort-Object Length | Select-Object -First 30)) {
        [void]$textArtifactSections.Add("# Artifact sample: $($artifact.FullName.Substring($resolvedResultRoot.Length).TrimStart('\','/'))")
        [void]$textArtifactSections.Add((Get-InterestingTextSample -Path $artifact.FullName))
    }

    $packet = @"
# Advisory DeepSeek Test-Artifact Review

You are providing a second opinion on test result artifacts for DO NOT PANIC PORTFOLIO VISUALIZER.

This is advisory only. Codex/the project owner will make the final pass/fail and CR-generation decisions.

Focus on:
- anomalies visible in trace/log excerpts
- likely UX issues implied by screenshot metadata and harness summaries
- missing evidence or weak proof
- possible false positives in the deterministic analysis
- specific follow-up checks that would improve confidence

Return findings first, ordered by severity. If the deterministic analysis appears clean, say whether the artifacts still suggest residual risk.

# Deterministic Analysis Report

$analysisText

# Screenshot/Image Artifact Inventory

$($screenshotLines -join "`n")

# App-Native Scene Capture Trace Timing

These trace lines are authoritative for when app-native screenshots were created inside the running application. File modified timestamps may reflect later artifact copy time.

$($sceneCaptureTraceLines -join "`n")

# Trace/Log/Summary Artifact Samples

$($textArtifactSections -join "`n`n")
"@

    $originalPacketLength = $packet.Length
    Assert-NoLikelySecrets $packet
    if ($originalPacketLength -gt $MaxArtifactCharacters) {
        $omitted = $originalPacketLength - $MaxArtifactCharacters
        Write-Warning "DeepSeek artifact review packet truncated from $originalPacketLength to $MaxArtifactCharacters characters; omitted $omitted characters."
        $cut = $packet.LastIndexOf("`n", [Math]::Min($MaxArtifactCharacters, $packet.Length) - 1)
        if ($cut -lt 1) { $cut = [Math]::Min($MaxArtifactCharacters, $packet.Length) }
        $packet = $packet.Substring(0, $cut) + "`n...[line-safe truncated by Invoke-DeepSeekArtifactReview.ps1; omitted at least $omitted characters]..."
        Assert-NoLikelySecrets $packet
    }

    $apiKey = Get-DeepSeekApiKey -RepositoryRoot $repoRoot
    $Endpoint = $Endpoint.TrimEnd('/')
    if (-not $Endpoint.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase)) {
        throw "DeepSeek artifact review endpoint must use HTTPS: $Endpoint"
    }
    $trustedDefaultEndpoint = 'https://api.deepseek.com'
    if (-not $Endpoint.Equals($trustedDefaultEndpoint, [StringComparison]::OrdinalIgnoreCase) -and
        -not $AcknowledgeEndpointOverride) {
        throw "DeepSeek artifact review endpoint '$Endpoint' differs from trusted default '$trustedDefaultEndpoint'. Rerun with -AcknowledgeEndpointOverride only if this destination is intentional."
    }

    $body = @{
        model = $Model
        messages = @(
            @{ role = 'system'; content = 'You are a senior QA/release engineer reviewing test result artifacts as an advisory second opinion. Be concrete and evidence-driven.' },
            @{ role = 'user'; content = $packet }
        )
        temperature = 0.1
        max_tokens = $MaxTokens
    } | ConvertTo-Json -Depth 8

    try {
        $response = Invoke-RestMethod -Method Post -Uri "$Endpoint/chat/completions" -Headers @{
            Authorization = "Bearer $apiKey"
            'Content-Type' = 'application/json'
        } -Body $body -TimeoutSec 180
    }
    catch {
        $status = $null
        if ($null -ne $_.Exception.Response -and $null -ne $_.Exception.Response.StatusCode) {
            $status = [int]$_.Exception.Response.StatusCode
        }
        $statusText = if ($null -eq $status) { 'unavailable' } else { [string]$status }
        throw "DeepSeek artifact review request failed. HTTP status: $statusText. Verify endpoint, model, API key, network connectivity, and artifact secret hygiene; raw response details are intentionally redacted."
    }

    if ($null -eq $response.choices -or $response.choices.Count -lt 1 -or
        $null -eq $response.choices[0].message -or
        [string]::IsNullOrWhiteSpace([string]$response.choices[0].message.content)) {
        throw 'DeepSeek artifact review response did not contain choices[0].message.content.'
    }

    $content = [string]$response.choices[0].message.content
    if ($response.choices[0].PSObject.Properties.Name -contains 'finish_reason' -and
        [string]::Equals([string]$response.choices[0].finish_reason, 'length', [StringComparison]::OrdinalIgnoreCase)) {
        Write-Warning 'DeepSeek artifact review response reported finish_reason=length; advisory report may be incomplete.'
    }
    if ($content.Length -gt $MaxResponseCharacters) {
        Write-Warning "DeepSeek artifact review response truncated from $($content.Length) to $MaxResponseCharacters characters."
        $content = $content.Substring(0, $MaxResponseCharacters) + "`n...[truncated by Invoke-DeepSeekArtifactReview.ps1]..."
    }

    Set-Content -LiteralPath $OutputPath -Value $content -Encoding UTF8
    $relativeOutputPath = (Resolve-Path -LiteralPath $OutputPath -Relative).TrimStart('.', '\', '/')
    Assert-GitIgnored $relativeOutputPath "DeepSeek artifact review output must be ignored by git: $relativeOutputPath"
    Write-Output ("DEEPSEEK_ARTIFACT_REVIEW=" + $OutputPath)
}
finally {
    Pop-Location
}
