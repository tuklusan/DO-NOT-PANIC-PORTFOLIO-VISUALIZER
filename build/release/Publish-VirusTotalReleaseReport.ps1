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
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = 'tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER',
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$Tag = 'v0.9.0-beta7',
    [string]$InstallerAssetPattern = 'DoNotPanicPortfolioVisualizerSetup-*.exe',
    [string]$OutputDirectory,
    [ValidateRange(20, 3600)]
    [int]$PollIntervalSeconds = 20,
    [ValidateRange(1, 90)]
    [int]$MaxPollAttempts = 12,
    [ValidateRange(5, 300)]
    [int]$RequestTimeoutSeconds = 60,
    [string]$VirusTotalBaseUri = 'https://www.virustotal.com/api/v3',
    [switch]$AllowIncompleteAnalysis,
    [switch]$AllowMissingApiKey,
    [switch]$SkipComment,
    [switch]$RequireComment,
    [switch]$AllowCommentFailure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    $root = & git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
        throw 'git repository root could not be resolved.'
    }

    return (Resolve-Path -LiteralPath $root.Trim() -ErrorAction Stop).Path
}

function Get-SecretFromLocalTestSecrets {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $secretsPath = Join-Path (Join-Path (Join-Path $RepositoryRoot 'build') 'vm') 'test-secrets.json'
    if (-not (Test-Path -LiteralPath $secretsPath -PathType Leaf)) {
        return ''
    }

    & git check-ignore -q -- $secretsPath 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'build/vm/test-secrets.json is not ignored by git; refusing to read VirusTotal secrets from that file.'
        return ''
    }

    try {
        $secrets = Get-Content -Raw -LiteralPath $secretsPath | ConvertFrom-Json
        foreach ($name in @('VirusTotalApiKey', 'VIRUSTOTAL_API_KEY')) {
            if ($secrets.PSObject.Properties.Name -contains $name) {
                $value = [string]$secrets.$name
                if (-not [string]::IsNullOrWhiteSpace($value)) {
                    Write-Warning 'Using VirusTotalApiKey from ignored build/vm/test-secrets.json.'
                    return $value
                }
            }
        }
    }
    catch {
        Write-Warning "Invalid JSON in build\vm\test-secrets.json; VirusTotal key could not be read. $($_.Exception.Message)"
    }

    return ''
}

function Get-VirusTotalApiKey {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    foreach ($scope in @('Process', 'User', 'Machine')) {
        $value = [Environment]::GetEnvironmentVariable('VIRUSTOTAL_API_KEY', $scope)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value
        }
    }

    return Get-SecretFromLocalTestSecrets -RepositoryRoot $RepositoryRoot
}

function ConvertTo-VirusTotalUrlId {
    param([Parameter(Mandatory = $true)][string]$Url)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Url)
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function ConvertTo-MarkdownTableValue {
    param($Value)

    if ($null -eq $Value) { return '' }
    return ([string]$Value).Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

function Invoke-VirusTotalRequest {
    # Use Body for application/x-www-form-urlencoded VirusTotal calls and JsonBody
    # for JSON endpoints such as public URL comments.
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$ApiKey,
        [hashtable]$Body,
        $JsonBody
    )

    $headers = @{
        accept = 'application/json'
        'x-apikey' = $ApiKey
    }

    if ($null -ne $Body -and $null -ne $JsonBody) {
        throw 'Invoke-VirusTotalRequest accepts either Body or JsonBody, not both.'
    }

    try {
        if ($null -ne $JsonBody) {
            $json = $JsonBody | ConvertTo-Json -Depth 8
            return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -Body $json -ContentType 'application/json' -TimeoutSec $RequestTimeoutSeconds
        }

        if ($null -ne $Body) {
            return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -Body $Body -ContentType 'application/x-www-form-urlencoded' -TimeoutSec $RequestTimeoutSeconds
        }

        return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -TimeoutSec $RequestTimeoutSeconds
    }
    catch {
        $detail = $_.Exception.Message
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $detail = "HTTP $([int]$_.Exception.Response.StatusCode) $($_.Exception.Response.StatusCode): $detail"
        }

        throw "VirusTotal request failed method=$Method uri=$Uri detail=$detail"
    }
}

function Test-VirusTotalRetryableFailure {
    param([AllowNull()][string]$Message)

    if ([string]::IsNullOrWhiteSpace($Message)) {
        return $false
    }

    return $Message -match 'HTTP 429\b' -or $Message -match 'HTTP 5\d\d\b'
}

function Limit-Text {
    param(
        [AllowNull()][string]$Text,
        [Parameter(Mandatory = $true)][int]$MaximumLength
    )

    if ([string]::IsNullOrEmpty($Text) -or [Text.Encoding]::UTF8.GetByteCount($Text) -le $MaximumLength) {
        return $Text
    }

    $suffix = "`n`n[Truncated for VirusTotal comment length. See the GitHub README and release notes for the full documentation.]"
    $suffixBytes = [Text.Encoding]::UTF8.GetByteCount($suffix)
    if ($MaximumLength -le $suffixBytes) {
        throw "MaximumLength must be greater than the UTF-8 byte length of the truncation suffix ($suffixBytes)."
    }

    $availablePrefixBytes = $MaximumLength - $suffixBytes
    $builder = [Text.StringBuilder]::new()
    $usedBytes = 0
    for ($index = 0; $index -lt $Text.Length; $index++) {
        $chunk = if ([char]::IsHighSurrogate($Text[$index]) -and $index + 1 -lt $Text.Length -and [char]::IsLowSurrogate($Text[$index + 1])) {
            $pair = $Text.Substring($index, 2)
            $index++
            $pair
        }
        else {
            [string]$Text[$index]
        }

        $chunkBytes = [Text.Encoding]::UTF8.GetByteCount($chunk)
        if ($usedBytes + $chunkBytes -gt $availablePrefixBytes) {
            break
        }

        [void]$builder.Append($chunk)
        $usedBytes += $chunkBytes
    }

    $result = $builder.ToString().TrimEnd() + $suffix
    if ([Text.Encoding]::UTF8.GetByteCount($result) -gt $MaximumLength) {
        throw 'Limit-Text produced a result longer than MaximumLength.'
    }

    return $result
}

function New-VirusTotalReleaseCommentText {
    param(
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$InstallerUrl,
        [Parameter(Mandatory = $true)][string]$ReleaseUrl,
        [Parameter(Mandatory = $true)][string]$InstallerHash,
        [Parameter(Mandatory = $true)][string]$VirusTotalReportUrl,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot
    )

    $readmePath = Join-Path $RepositoryRoot 'README.md'
    $readmeExcerpt = if (Test-Path -LiteralPath $readmePath -PathType Leaf) {
        Limit-Text -Text (Get-Content -Raw -LiteralPath $readmePath) -MaximumLength 1800
    }
    else {
        'README.md was not found in this checkout when the VirusTotal release comment was generated.'
    }

    $comment = @"
DO NOT PANIC PORTFOLIO VISUALIZER public installer advisory context

Download URL: $InstallerUrl
GitHub Release: $ReleaseUrl
Source Repository: https://github.com/$Repository
Release Tag: $Tag
Installer SHA-256: $InstallerHash
VirusTotal URL Report: $VirusTotalReportUrl

Summary:
DO NOT PANIC PORTFOLIO VISUALIZER is a cinematic Windows desktop financial visualizer by Supratim Sanyal of SANYALnet Labs. It displays delayed market data, ticker tapes, graph cards, world-market ribbons, configurable backgrounds, and optional AI-styled finance-news summaries. It is a visual/informational desktop application only. It must not be used as a financial planning, financial monitoring, trading, investment-advice, safety, or alerting tool.

License and distribution note:
This public beta is distributed under the repository LICENSE for strictly non-commercial personal, educational, or hobbyist use. This VirusTotal submission scans the already-public GitHub Release installer download URL and is advisory only; it is not a warranty, certification, or guarantee of safety.

README excerpt:
$readmeExcerpt
"@

    # VirusTotal comments are limited to 4096 bytes; keep a margin for UTF-8.
    return Limit-Text -Text $comment -MaximumLength 4000
}

function Publish-VirusTotalUrlComment {
    param(
        [Parameter(Mandatory = $true)][string]$UrlId,
        [Parameter(Mandatory = $true)][string]$CommentText,
        [Parameter(Mandatory = $true)][string]$ApiKey,
        [Parameter(Mandatory = $true)][string]$VirusTotalBaseUri,
        [ValidateRange(1, 5)]
        [int]$Attempts = 3,
        [ValidateRange(5, 300)]
        [int]$RetrySleepSeconds = 20
    )

    $body = @{
        data = @{
            type = 'comment'
            attributes = @{
                text = $CommentText
            }
        }
    }

    $lastError = $null
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            return Invoke-VirusTotalRequest -Method Post -Uri "$VirusTotalBaseUri/urls/$UrlId/comments" -ApiKey $ApiKey -JsonBody $body
        }
        catch {
            $lastError = $_
            if (-not (Test-VirusTotalRetryableFailure -Message $_.Exception.Message)) {
                throw
            }

            if ($attempt -lt $Attempts) {
                Start-Sleep -Seconds $RetrySleepSeconds
            }
        }
    }

    throw $lastError
}

function Get-ReleaseAsset {
    param(
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Pattern
    )

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'GitHub CLI gh was not found on PATH.'
    }

    $authOutput = & gh auth status 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI is not authenticated or cannot reach GitHub. gh auth status output: $($authOutput -join "`n")"
    }

    $errorPath = [IO.Path]::GetTempFileName()
    try {
        $releaseOutput = & gh release view $Tag --repo $Repository --json tagName,targetCommitish,url,assets 2> $errorPath
        $releaseError = Get-Content -Raw -LiteralPath $errorPath -ErrorAction SilentlyContinue
    }
    finally {
        Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue
    }

    if ($LASTEXITCODE -ne 0 -or $null -eq $releaseOutput -or [string]::IsNullOrWhiteSpace(($releaseOutput -join "`n"))) {
        throw "GitHub release could not be read: $Repository $Tag. gh stderr: $releaseError"
    }

    try {
        $release = ($releaseOutput -join "`n") | ConvertFrom-Json
    }
    catch {
        throw "GitHub release response was not valid JSON for $Repository $Tag. gh stdout: $($releaseOutput -join "`n") gh stderr: $releaseError"
    }

    $matchingAssets = @($release.assets | Where-Object { $_.name -like $Pattern } | Sort-Object name)
    if ($matchingAssets.Count -eq 0) {
        throw "No installer asset matching '$Pattern' was found on release $Tag."
    }

    if ($matchingAssets.Count -gt 1) {
        throw "Installer asset pattern '$Pattern' matched multiple assets on release ${Tag}: $(($matchingAssets | Select-Object -ExpandProperty name) -join ', '). Use a stricter pattern."
    }

    $asset = $matchingAssets[0]

    [pscustomobject]@{
        Release = $release
        Asset = $asset
    }
}

$repoRoot = Get-RepoRoot
$releaseRootPath = Join-Path $repoRoot 'releases'
New-Item -ItemType Directory -Force -Path $releaseRootPath | Out-Null
$releaseRoot = (Resolve-Path -LiteralPath $releaseRootPath -ErrorAction Stop).Path.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $releaseRoot $Tag
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$resolvedOutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory -ErrorAction Stop).Path.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$releaseRootWithSeparator = $releaseRoot + [IO.Path]::DirectorySeparatorChar
if ($resolvedOutputDirectory -ne $releaseRoot -and -not $resolvedOutputDirectory.StartsWith($releaseRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must resolve under the repository releases directory: $releaseRoot"
}

if ($PollIntervalSeconds * $MaxPollAttempts -gt 1800) {
    throw 'VirusTotal polling window must not exceed 30 minutes. Increase deliberately in a separate reviewed change if needed.'
}

$VirusTotalBaseUri = $VirusTotalBaseUri.TrimEnd('/')
if ($SkipComment -and $RequireComment) {
    throw 'Cannot combine -SkipComment with -RequireComment.'
}

$apiKey = Get-VirusTotalApiKey -RepositoryRoot $repoRoot
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    $message = 'VIRUSTOTAL_API_KEY was not found in Process/User/Machine environment or ignored build/vm/test-secrets.json.'
    if ($AllowMissingApiKey) {
        Write-Warning $message
        return
    }

    throw $message
}

$releaseAsset = Get-ReleaseAsset -Repository $Repository -Tag $Tag -Pattern $InstallerAssetPattern
$release = $releaseAsset.Release
$asset = $releaseAsset.Asset
$installerUrl = if ($asset.PSObject.Properties.Name -contains 'browser_download_url' -and -not [string]::IsNullOrWhiteSpace([string]$asset.browser_download_url)) {
    [string]$asset.browser_download_url
}
else {
    [string]$asset.url
}

if ($installerUrl -match '^https://api\.github\.com/' -or $installerUrl -notmatch '^https://github\.com/.+/releases/download/.+') {
    throw "Installer asset URL is not a public GitHub download URL: $installerUrl"
}

$digest = [string]$asset.digest
$digestMatch = [regex]::Match($digest, '^(?i)sha256:([a-f0-9]{64})$')
if (-not $digestMatch.Success) {
    throw "GitHub release asset digest is missing or is not a SHA-256 digest: '$digest'"
}

$installerHash = $digestMatch.Groups[1].Value.ToLowerInvariant()
$urlId = ConvertTo-VirusTotalUrlId -Url $installerUrl
$releaseBrowserUrl = "https://github.com/$Repository/releases/tag/$Tag"
$virusTotalUrlReport = "https://www.virustotal.com/gui/url/$urlId/detection"

Write-Information "Submitting public installer URL to VirusTotal: $installerUrl" -InformationAction Continue
$submission = Invoke-VirusTotalRequest -Method Post -Uri "$VirusTotalBaseUri/urls" -ApiKey $apiKey -Body @{ url = $installerUrl }
$analysisId = [string]$submission.data.id
if ([string]::IsNullOrWhiteSpace($analysisId)) {
    throw 'VirusTotal URL submission did not return an analysis id.'
}

$analysis = $null
for ($attempt = 1; $attempt -le $MaxPollAttempts; $attempt++) {
    if ($attempt -gt 1) {
        Start-Sleep -Seconds $PollIntervalSeconds
    }

    $analysis = Invoke-VirusTotalRequest -Method Get -Uri "$VirusTotalBaseUri/analyses/$analysisId" -ApiKey $apiKey
    $status = [string]$analysis.data.attributes.status
    Write-Information "VirusTotal analysis status attempt=$attempt status=$status" -InformationAction Continue
    if ($status -eq 'completed') {
        break
    }
}

if ($null -eq $analysis) {
    throw 'VirusTotal analysis polling did not return any response.'
}

$status = [string]$analysis.data.attributes.status
$analysisCompleted = $status -eq 'completed'
$stats = $analysis.data.attributes.stats
$analysisDate = Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
$reportPath = Join-Path $resolvedOutputDirectory 'virustotal-advisory-report.md'
$commentStatus = 'not attempted'
$commentId = ''
$commentNote = 'VirusTotal URL comment posting was not attempted.'

if (-not $SkipComment) {
    $commentText = New-VirusTotalReleaseCommentText -Repository $Repository -Tag $Tag -InstallerUrl $installerUrl -ReleaseUrl $releaseBrowserUrl -InstallerHash $installerHash -VirusTotalReportUrl $virusTotalUrlReport -RepositoryRoot $repoRoot
    try {
        $commentResult = Publish-VirusTotalUrlComment -UrlId $urlId -CommentText $commentText -ApiKey $apiKey -VirusTotalBaseUri $VirusTotalBaseUri
        $commentStatus = 'posted'
        $commentId = [string]$commentResult.data.id
        $commentNote = 'Release context comment posted to the VirusTotal URL object.'
        Write-Information "VirusTotal URL comment posted id=$commentId" -InformationAction Continue
    }
    catch {
        $commentStatus = 'failed'
        $commentNote = $_.Exception.Message
        Write-Warning "VirusTotal URL comment could not be posted: $commentNote"
        if ($RequireComment -and -not $AllowCommentFailure) {
            $commentNote = "Required comment failed; advisory report was still generated. $commentNote"
        }
    }
}

$statRows = @()
foreach ($name in @('malicious', 'suspicious', 'undetected', 'harmless', 'timeout')) {
    $value = if ($null -ne $stats -and $stats.PSObject.Properties.Name -contains $name) { $stats.$name } else { '' }
    $statRows += "| $name | $(ConvertTo-MarkdownTableValue $value) |"
}

$completionNote = if ($analysisCompleted) {
    'VirusTotal analysis completed before this report was generated.'
}
else {
    "VirusTotal analysis did not complete within $MaxPollAttempts polling attempts; this report captures the latest available status and should be refreshed later."
}

$report = @"
<!--
============================================================================
Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
Proprietary rights reserved except as expressly licensed herein.

DO NOT PANIC PORTFOLIO VISUALIZER
This file is governed by the SANYALnet Labs Non-Commercial License in the
root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
for AI/ML model training are prohibited unless separately authorized.

Attribution is required: "Based on original work by Supratim Sanyal of
SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
patent, trademark, and governing-law provisions.
============================================================================
-->

# VirusTotal Advisory Scan Report - $Tag

This advisory report records a VirusTotal URL scan for the public GitHub Release installer download URL. VirusTotal results are useful third-party signals, but they are not a warranty, certification, or guarantee that software is safe.

## Release Asset

| Field | Value |
| --- | --- |
| Release tag | $(ConvertTo-MarkdownTableValue $Tag) |
| GitHub release | $(ConvertTo-MarkdownTableValue $releaseBrowserUrl) |
| Installer asset | $(ConvertTo-MarkdownTableValue $asset.name) |
| Installer URL | $(ConvertTo-MarkdownTableValue $installerUrl) |
| GitHub asset SHA-256 | $(ConvertTo-MarkdownTableValue $installerHash) |
| GitHub asset size | $(ConvertTo-MarkdownTableValue $asset.size) bytes |
| Report generated | $(ConvertTo-MarkdownTableValue $analysisDate) |

## VirusTotal

| Field | Value |
| --- | --- |
| Submission type | Public URL scan |
| Analysis ID | $(ConvertTo-MarkdownTableValue $analysisId) |
| Analysis status | $(ConvertTo-MarkdownTableValue $status) |
| Completion note | $(ConvertTo-MarkdownTableValue $completionNote) |
| URL object ID | $(ConvertTo-MarkdownTableValue $urlId) |
| VirusTotal analysis API | $VirusTotalBaseUri/analyses/$analysisId |
| VirusTotal URL report | $virusTotalUrlReport |
| Release context comment status | $(ConvertTo-MarkdownTableValue $commentStatus) |
| Release context comment ID | $(ConvertTo-MarkdownTableValue $commentId) |
| Release context comment note | $(ConvertTo-MarkdownTableValue $commentNote) |

## Last Analysis Stats

| Category | Count |
| --- | --- |
$($statRows -join "`n")

## Operational Notes

- The release hook submits the already-public GitHub installer download URL rather than uploading the local installer binary.
- The release hook posts a bounded public VirusTotal URL comment containing the download URL, release metadata, app summary, and README excerpt so the VirusTotal report has provenance context. That adds one extra API call per release run, plus up to two quota-aware retries if VirusTotal has not accepted comments for the URL object yet. Pass -SkipComment for scan-only advisory runs.
- Comment-post failure is recorded in the advisory report by default so the scan evidence is not lost; pass -RequireComment if a release gate must fail after report generation when the comment cannot be posted.
- VirusTotal Public API limits are 500 requests/day and 4 requests/minute; this hook polls no more often than every $PollIntervalSeconds seconds.
- A clean or low-detection result is advisory only. Users should still apply normal software-installation judgment.
"@

if (Test-Path -LiteralPath $reportPath -PathType Leaf) {
    Write-Warning "Overwriting existing VirusTotal report: $reportPath"
}

Set-Content -LiteralPath $reportPath -Value $report -Encoding UTF8
Write-Information "VIRUSTOTAL_REPORT=$reportPath" -InformationAction Continue
if (-not $analysisCompleted -and -not $AllowIncompleteAnalysis) {
    throw "VirusTotal analysis did not complete within $MaxPollAttempts attempts. Partial report written to $reportPath. Rerun later or pass -AllowIncompleteAnalysis for advisory-only publication."
}

if ($RequireComment -and -not $AllowCommentFailure -and $commentStatus -ne 'posted') {
    throw "VirusTotal URL comment was required but was not posted. Advisory report written to $reportPath with comment status '$commentStatus'."
}
