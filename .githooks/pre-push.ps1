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
$ErrorActionPreference = "Stop"

function Is-PlaceholderValue {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return $true }
    $v = $Value.Trim()
    if ($v -match '^(?i:(REPLACE_WITH_[A-Z0-9_]+|YOUR_[A-Z0-9_]+|CHANGEME(?:_[A-Z0-9_]+)?|REDACTED(?:_[A-Z0-9_]+)?|NONE|null|nil))$') { return $true }
    if ($v -match '^<.*>$') { return $true }
    if ($v -match '^\*+$') { return $true }
    return $false
}

function Add-Hit {
    param(
        [System.Collections.Generic.List[string]]$Hits,
        [string]$Commit,
        [string]$Reason,
        [string]$Line
    )

    $preview = $Line
    if ($preview.Length -gt 180) { $preview = $preview.Substring(0, 180) + '...' }
    $Hits.Add("$Commit [$Reason] $preview") | Out-Null
}

$refs = @()
while (($line = [Console]::In.ReadLine()) -ne $null) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $parts = ($line.Trim() -split '\s+') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($parts.Count -lt 4) { continue }
    $refs += [pscustomobject]@{ LocalSha = $parts[1]; RemoteSha = $parts[3] }
}

if ($refs.Count -eq 0) { exit 0 }

$outgoing = New-Object System.Collections.Generic.HashSet[string]
foreach ($r in $refs) {
    if ($r.LocalSha -match '^[0]+$') { continue }

    if ($r.RemoteSha -match '^[0]+$') {
        [void]$outgoing.Add($r.LocalSha)
        continue
    }

    $rangeCommits = @(git rev-list "$($r.RemoteSha)..$($r.LocalSha)" 2>$null)
    if ($LASTEXITCODE -ne 0 -or $rangeCommits.Count -eq 0) {
        [void]$outgoing.Add($r.LocalSha)
    } else {
        foreach ($c in $rangeCommits) { [void]$outgoing.Add($c.Trim()) }
    }
}

$hits = New-Object System.Collections.Generic.List[string]
$forbiddenPathPatterns = @(
    '(?i)(^|/|\\)codex\.secrets\.json$',
    '(?i)(^|/|\\)TUKLUS-NOTES-WITH-API-KEYS\.txt$',
    '(?i)(^|/|\\)Issues and Development Scratchpad.*\.(txt|md)$',
    '(?i)(^|/|\\).*scratchpad.*\.(txt|md)$',
    '(?i)(^|/|\\)build/vm-settings\.json$',
    '(?i)(^|/|\\)build/vm/vm-settings\.json$',
    '(?i)(^|/|\\)build/vm/vm-credentials\.local\.json$',
    '(?i)(^|/|\\)build/.*/test-secrets\.json$'
)

foreach ($commit in $outgoing) {
    if ([string]::IsNullOrWhiteSpace($commit)) { continue }

    $changedPaths = @(git diff-tree --no-commit-id --name-only -r $commit 2>$null)
    foreach ($path in $changedPaths) {
        foreach ($pattern in $forbiddenPathPatterns) {
            if ($path -match $pattern) {
                Add-Hit -Hits $hits -Commit $commit -Reason 'Forbidden secret/local-only filename' -Line $path
                break
            }
        }
    }

    $patch = git show --pretty=format: --unified=0 $commit -- . ':(exclude).githooks/*' 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $patch) { continue }

    foreach ($raw in $patch) {
        if (-not $raw.StartsWith('+') -or $raw.StartsWith('+++')) { continue }
        $line = $raw.Substring(1)

        # VM credential guards (keep these protections active)
        if ($line -match '(?i)\bschtasks\b.*\s/RP\s+(\S+)') {
            $rp = $Matches[1].Trim('"')
            if ($rp -notmatch '^(?i:%USERNAME%|<pass>|<password>|REDACTED_VM_PASSWORD)$') {
                Add-Hit -Hits $hits -Commit $commit -Reason 'VM password in schtasks /RP' -Line $line
            }
        }
        if ($line -match '(?i)\bRegister-ScheduledTask\b.*-Password\s+"([^"]+)"') {
            $pwd = $Matches[1]
            if (-not (Is-PlaceholderValue $pwd)) {
                Add-Hit -Hits $hits -Commit $commit -Reason 'VM password in Register-ScheduledTask' -Line $line
            }
        }
        if ($line -match '(?i)\bschtasks\b.*\s/RU\s+(\S+)') {
            $ru = $Matches[1].Trim('"')
            if ($ru -notmatch '^(?i:%USERNAME%|<user>|REDACTED_VM_USER)$') {
                Add-Hit -Hits $hits -Commit $commit -Reason 'Hardcoded VM user in schtasks /RU' -Line $line
            }
        }
        if ($line -match '(?i)\bNew-ScheduledTaskPrincipal\b.*-UserId\s+"([^"]+)"') {
            $uid = $Matches[1]
            if ($uid -notmatch '^(?i:%USERNAME%|\$env:USERNAME|<user>|REDACTED_VM_USER)$') {
                Add-Hit -Hits $hits -Commit $commit -Reason 'Hardcoded VM user in New-ScheduledTaskPrincipal' -Line $line
            }
        }

        # API key leak guards (JSON/assignment style with non-placeholder values)
        if ($line -match '(?i)"[A-Za-z0-9_-]*api[_-]?key"\s*:\s*"([^"]+)"') {
            $val = $Matches[1]
            if ($val.Length -ge 12 -and -not (Is-PlaceholderValue $val)) {
                Add-Hit -Hits $hits -Commit $commit -Reason 'API key value in JSON assignment' -Line $line
            }
        }

        if ($line -match '(?i)\bPORTFOLIOSAVER_[A-Z0-9_]*API_KEY\b\s*=\s*["\'']?([^"\''\s]+)') {
            $envVal = $Matches[1]
            if ($envVal.Length -ge 12 -and -not (Is-PlaceholderValue $envVal)) {
                Add-Hit -Hits $hits -Commit $commit -Reason 'API key value in env assignment' -Line $line
            }
        }
    }
}

if ($hits.Count -gt 0) {
    $report = ($hits | Select-Object -Unique | Out-String)
    Write-Error "Push blocked: credential/API-key leak detected in outgoing commits.`n$report"
    exit 1
}

exit 0
