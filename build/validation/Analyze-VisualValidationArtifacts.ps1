param(
    [Parameter(Mandatory = $true)][string]$ResultRoot,
    [string]$OutputPath,
    [switch]$CreateChangeRequests,
    [int]$MinimumScreenshots = 3,
    [double]$BlankBrightnessThreshold = 7.0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -and $PSVersionTable.Platform -ne 'Win32NT') { throw 'Visual artifact image analysis requires Windows/System.Drawing support.' }
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

function New-Finding {
    param([string]$Code,[string]$Title,[string]$Area,[string]$Severity,[string[]]$Evidence,[string[]]$Notes = @())
    [pscustomobject]@{ code=$Code; title=$Title; area=$Area; severity=$Severity; evidence=@($Evidence); notes=@($Notes) }
}

function Get-ValidationRunDirectories {
    param([string]$Root)
    $rootItem = Get-Item -LiteralPath (Resolve-Path -LiteralPath $Root).Path
    if (Test-Path -LiteralPath (Join-Path $rootItem.FullName 'ux-deep-summary.json')) { return @($rootItem) }
    return @(Get-ChildItem -LiteralPath $rootItem.FullName -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'ux-deep-summary.json') } | Sort-Object LastWriteTime)
}

function Test-TraceLineAllowed {
    param([string]$Line)
    $allowPath = Join-Path $PSScriptRoot 'allowed-trace-patterns.txt'
    $patterns = if (Test-Path -LiteralPath $allowPath) { @(Get-Content -LiteralPath $allowPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.TrimStart().StartsWith('#') }) } else { @() }
    foreach ($pattern in $patterns) {
        if ($Line -match [regex]::Escape($pattern)) { return $true }
    }
    return $false
}

function Measure-ImageBrightness {
    param([string]$Path)
    Add-Type -AssemblyName System.Drawing
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $stream = New-Object System.IO.MemoryStream(, $bytes)
    $bitmap = [System.Drawing.Bitmap]::FromStream($stream)
    try {
        $stepX = [Math]::Max(1, [int]($bitmap.Width / 32))
        $stepY = [Math]::Max(1, [int]($bitmap.Height / 32))
        $total = 0.0; $count = 0
        for ($y = 0; $y -lt $bitmap.Height; $y += $stepY) {
            for ($x = 0; $x -lt $bitmap.Width; $x += $stepX) {
                $p = $bitmap.GetPixel($x, $y)
                $total += (($p.R + $p.G + $p.B) / 3.0); $count++
            }
        }
        if ($count -eq 0) { return 0.0 }
        return $total / $count
    }
    finally { if ($null -ne $bitmap) { $bitmap.Dispose() }; $stream.Dispose() }
}

$runs = Get-ValidationRunDirectories -Root $ResultRoot
if ($runs.Count -eq 0) { throw "No UX validation run directories with ux-deep-summary.json were found under $ResultRoot" }
$findings = New-Object System.Collections.Generic.List[object]
$runSummaries = New-Object System.Collections.Generic.List[object]

foreach ($run in $runs) {
    $summaryPath = Join-Path $run.FullName 'ux-deep-summary.json'
    $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
    $runId = if ($summary.PSObject.Properties.Name -contains 'ResultName') { [string]$summary.ResultName } else { $run.Name }
    [void]$runSummaries.Add([pscustomobject]@{ resultName=$runId; path=$run.FullName; configPhaseStatus=[string]$summary.ConfigPhaseStatus; desktopPhaseStatus=[string]$summary.DesktopPhaseStatus; fullScreenToggleStatus=[string]$summary.FullScreenToggleStatus; notes=@($summary.Notes) })

    foreach ($statusName in @('ConfigPhaseStatus','DesktopPhaseStatus','FullScreenToggleStatus')) {
        $status = [string]$summary.$statusName
        if ($status -ne 'Completed') {
            [void]$findings.Add((New-Finding -Code "harness-$($statusName.ToLowerInvariant())" -Title "VM validation phase did not complete: $statusName" -Area 'VM validation harness' -Severity 'High' -Evidence @("Run ${runId} reported ${statusName}=$status.", "Summary: $summaryPath") -Notes @('Incomplete harness phases are release-candidate blockers.')))
        }
    }

    $screenshots = @(Get-ChildItem -LiteralPath $run.FullName -File -Include '*.png' -Recurse -ErrorAction SilentlyContinue)
    if ($screenshots.Count -lt $MinimumScreenshots) {
        [void]$findings.Add((New-Finding -Code 'capture-count-low' -Title 'VM validation produced too few screenshots for visual proof' -Area 'VM validation harness' -Severity 'Medium' -Evidence @("Run ${runId} produced $($screenshots.Count) screenshot(s); minimum expected is $MinimumScreenshots.", "Run directory: $($run.FullName)") -Notes @('Visual validation needs enough captures to verify background, graph-card, news, and ribbon behavior.')))
    }

    $dark = New-Object System.Collections.Generic.List[string]
    foreach ($shot in @($screenshots)) {
        try {
            $brightness = Measure-ImageBrightness -Path $shot.FullName
            if ($brightness -lt $BlankBrightnessThreshold) { [void]$dark.Add(("{0} brightness={1:N2}" -f $shot.Name, $brightness)) }
        } catch {
            [void]$findings.Add((New-Finding -Code 'capture-unreadable' -Title 'VM validation screenshot could not be inspected' -Area 'VM validation harness' -Severity 'Medium' -Evidence @("Screenshot read failed in run ${runId}: $($shot.FullName)", $_.Exception.Message) -Notes @('Unreadable proof artifacts weaken autonomous validation confidence.')))
        }
    }
    if ($dark.Count -gt 0) {
        [void]$findings.Add((New-Finding -Code 'blank-or-dark-background' -Title 'Possible blank or fully dark screen captured during VM validation' -Area 'visual_background' -Severity 'High' -Evidence @("Run ${runId} contains very dark capture(s): $($dark -join '; ')", "Run directory: $($run.FullName)") -Notes @('Triage against the actual screenshots before closure.')))
    }

    $traceFiles = @(Get-ChildItem -LiteralPath $run.FullName -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '(?i)(trace|circular|events|log)' })
    $traceLines = New-Object System.Collections.Generic.List[string]
    foreach ($trace in $traceFiles) {
        $hits = @(Select-String -LiteralPath $trace.FullName -Pattern '(?i)\b(error|fatal|exception|failed|timeout|warning|warn|missing|blank|source-missing|jitter|burst|unhandled)\b' -ErrorAction SilentlyContinue | Select-Object -First 40)
        foreach ($hit in $hits) {
            $line = "{0}:{1}: {2}" -f $trace.Name, $hit.LineNumber, $hit.Line.Trim()
            if (-not (Test-TraceLineAllowed -Line $line)) { [void]$traceLines.Add($line) }
        }
    }
    if ($traceLines.Count -gt 0) {
        [void]$findings.Add((New-Finding -Code 'trace-anomalies' -Title 'Trace scan found anomaly lines during VM validation' -Area 'runtime_tracing' -Severity 'High' -Evidence @("Run ${runId} trace anomaly sample:", @($traceLines | Select-Object -First 12)) -Notes @('Unexpected trace anomalies are escalated so they are not lost after long autonomous runs.')))
    }

    $configTrace = Join-Path $run.FullName 'config-window-events.log'
    if (Test-Path -LiteralPath $configTrace) {
        $closeHits = @(Select-String -LiteralPath $configTrace -Pattern 'ValidateCloseFailed|ValidateOkNotReached|ConfigClosedVerificationWaitTimeout|forced close|Validate did not close' -ErrorAction SilentlyContinue)
        if ($closeHits.Count -gt 0) {
            $closeEvidence = @($closeHits | Select-Object -First 8 | ForEach-Object { "config-window-events.log:{0}: {1}" -f $_.LineNumber, $_.Line.Trim() })
            [void]$findings.Add((New-Finding -Code 'config-close-regression' -Title 'Config validation close workflow showed close-regression evidence' -Area 'config_harness' -Severity 'High' -Evidence @("Run ${runId} config trace contains close-regression evidence.", $closeEvidence) -Notes @('Successful validation must reach OK/Cancel and close promptly when Apply/OK is selected.')))
        }
    }
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $repoRoot ('build\validation\artifacts\visual-validation-analysis-{0}.json' -f (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force -Path (Split-Path -Path $OutputPath -Parent) | Out-Null
$report = [ordered]@{ generatedAt=(Get-Date).ToString('o'); resultRoot=(Resolve-Path -LiteralPath $ResultRoot).Path; runCount=$runs.Count; clean=($findings.Count -eq 0); findings=@($findings); runs=@($runSummaries) }
$report | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

if ($CreateChangeRequests -and $findings.Count -gt 0) {
    foreach ($finding in $findings) {
        & (Join-Path $PSScriptRoot 'Add-AuditChangeRequest.ps1') -Title $finding.title -Area $finding.area -Severity $finding.severity -Priority 1 -Source 'autonomous_visual_validation' -Evidence @($finding.evidence) -Notes @($finding.notes) | Out-Host
    }
}
Write-Output ("ANALYSIS_REPORT=" + $OutputPath)
Write-Output ("ANALYSIS_CLEAN=" + [string]($findings.Count -eq 0))
Write-Output ("ANALYSIS_FINDINGS=" + $findings.Count)




