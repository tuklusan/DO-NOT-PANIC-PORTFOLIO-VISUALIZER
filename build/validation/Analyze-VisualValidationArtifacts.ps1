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
    [string]$OutputPath,
    [switch]$CreateChangeRequests,
    [int]$MinimumScreenshots = 3,
    [int]$MinimumStaticPairs = 3,
    [double]$BlankBrightnessThreshold = 7.0,
    [switch]$SkipDeepSeekArtifactReview
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -and $PSVersionTable.Platform -ne 'Win32NT') { throw 'Visual artifact image analysis requires Windows/System.Drawing support.' }
Add-Type -AssemblyName System.Drawing
$script:MaxScreenshotDifferenceBytes = 100MB
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$allowedTracePath = Join-Path $PSScriptRoot 'allowed-trace-patterns.txt'
$allowedFaultTracePath = Join-Path $PSScriptRoot 'allowed-fault-injection-trace-patterns.txt'
$script:allowedTracePatterns = if (Test-Path -LiteralPath $allowedTracePath) { @(Get-Content -LiteralPath $allowedTracePath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.TrimStart().StartsWith('#') }) } else { @() }
$script:allowedFaultInjectionTracePatterns = if (Test-Path -LiteralPath $allowedFaultTracePath) { @(Get-Content -LiteralPath $allowedFaultTracePath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.TrimStart().StartsWith('#') }) } else { @() }

function New-Finding {
    param([string]$Code,[string]$Title,[string]$Area,[string]$Severity,[string[]]$Evidence,[string[]]$Notes = @())
    [pscustomobject]@{ code=$Code; title=$Title; area=$Area; severity=$Severity; evidence=@($Evidence); notes=@($Notes) }
}

function Get-JsonPropertyValue {
    param($Object,[string]$Name,$Default = $null)
    if ($null -ne $Object -and $Object.PSObject.Properties.Name -contains $Name) { return $Object.$Name }
    return $Default
}

function Get-JsonIntPropertyValue {
    param($Object,[string]$Name)
    if ($null -eq $Object -or -not ($Object.PSObject.Properties.Name -contains $Name)) { return $null }
    $value = Get-JsonPropertyValue -Object $Object -Name $Name
    $parsed = 0
    if ([int]::TryParse([string]$value, [ref]$parsed)) { return $parsed }
    return $null
}

function Get-ValidationRunDirectories {
    param([string]$Root)
    $rootItem = Get-Item -LiteralPath (Resolve-Path -LiteralPath $Root).Path
    if (Test-Path -LiteralPath (Join-Path $rootItem.FullName 'ux-deep-summary.json')) { return @($rootItem) }
    return @(Get-ChildItem -LiteralPath $rootItem.FullName -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'ux-deep-summary.json') } | Sort-Object LastWriteTime)
}

function Test-TraceLineAllowed {
    param([string]$Line)
    foreach ($pattern in $script:allowedTracePatterns) {
        if ($Line -match [regex]::Escape($pattern)) { return $true }
    }
    return $false
}

function Test-FaultInjectionTraceLineAllowed {
    param([string]$Line,[string]$FaultProfile)
    if ([string]::IsNullOrWhiteSpace($FaultProfile) -or $FaultProfile -eq 'none') { return $false }
    if ($Line -match 'YFinanceClientProtocol' -and
        $Line -match 'ClientResponseReceive' -and
        $Line -match 'status=error') {
        return $true
    }

    foreach ($pattern in $script:allowedFaultInjectionTracePatterns) {
        if ($Line -match [regex]::Escape($pattern)) { return $true }
    }
    return $false
}

function Test-TraceAgeFieldFresh {
    param(
        [Parameter(Mandatory = $true)][string]$Line,
        [Parameter(Mandatory = $true)][string]$FieldName,
        [double]$MaximumAgeSeconds = 180.0
    )

    $pattern = [regex]::Escape($FieldName) + '=([0-9]+(?:\.[0-9]+)?)'
    $match = [regex]::Match($Line, $pattern)
    if (-not $match.Success) {
        return $false
    }

    $age = 0.0
    if (-not [double]::TryParse(
            $match.Groups[1].Value,
            [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$age)) {
        return $false
    }

    return $age -le $MaximumAgeSeconds
}

function Get-TraceLineTimestampUtc {
    param([Parameter(Mandatory = $true)][string]$Line)

    $match = [regex]::Match($Line, '\btimestamp=(?<timestamp>\d{4}-\d{2}-\d{2}T[^\s|\]]+)')
    if (-not $match.Success) {
        $match = [regex]::Match($Line, '^\s*(?<timestamp>\d{4}-\d{2}-\d{2}T[^\s|\]]+)')
    }
    if (-not $match.Success) {
        return $null
    }

    $timestamp = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse(
            $match.Groups['timestamp'].Value,
            [System.Globalization.CultureInfo]::InvariantCulture,
            ([System.Globalization.DateTimeStyles]::AssumeUniversal -bor [System.Globalization.DateTimeStyles]::AdjustToUniversal),
            [ref]$timestamp)) {
        return $timestamp.ToUniversalTime()
    }

    return $null
}

function Measure-ImageBrightness {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $stream = [System.IO.MemoryStream]::new($bytes)
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

function Read-ScreenCaptureManifest {
    param([Parameter(Mandatory = $true)][string]$RunDirectory)

    $manifestPath = Join-Path $RunDirectory 'screen-captures.jsonl'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return @()
    }

    $records = New-Object System.Collections.Generic.List[object]
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $manifestPath) {
        $lineNumber++
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $record = $line | ConvertFrom-Json
            if ($null -ne $record) {
                [void]$records.Add($record)
            }
        }
        catch {
            Write-Warning ("Ignoring malformed screen capture manifest line {0} in {1}: {2}" -f $lineNumber, $manifestPath, $_.Exception.Message)
        }
    }

    return @($records.ToArray())
}

function Get-CaptureImagePath {
    param(
        [Parameter(Mandatory = $true)]$Record,
        [Parameter(Mandatory = $true)][string]$RunDirectory
    )

    $fileName = [string](Get-JsonPropertyValue -Object $Record -Name 'FileName' -Default '')
    if ([string]::IsNullOrWhiteSpace($fileName)) {
        return $null
    }
    if ([IO.Path]::GetFileName($fileName) -ne $fileName) {
        return $null
    }

    $candidate = Join-Path $RunDirectory $fileName
    $resolvedRunDirectory = [IO.Path]::GetFullPath($RunDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedCandidate = [IO.Path]::GetFullPath($candidate)
    if (-not $resolvedCandidate.StartsWith($resolvedRunDirectory, [StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }
    if (Test-Path -LiteralPath $resolvedCandidate) {
        return $resolvedCandidate
    }

    return $null
}

function ConvertTo-ValidationBool {
    param($Value)

    if ($Value -is [bool]) { return $Value }
    if ($null -eq $Value) { return $false }

    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return $false }
    if ($text -ieq 'true') { return $true }
    if ($text -ieq 'false') { return $false }

    $number = 0.0
    if ([double]::TryParse(
            $text,
            [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$number)) {
        return $number -ne 0.0
    }

    return $false
}

function Get-CaptureSequenceNumber {
    param([Parameter(Mandatory = $true)]$Record)

    $fileName = [string](Get-JsonPropertyValue -Object $Record -Name 'FileName' -Default '')
    $match = [regex]::Match($fileName, '^desktop-(\d+)\.png$')
    if (-not $match.Success) {
        return [int]::MaxValue
    }

    return [int]::Parse($match.Groups[1].Value, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-CaptureRectangleNumber {
    param(
        $Object,
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    if ($null -eq $Object) {
        return $null
    }

    foreach ($name in $Names) {
        if ($Object.PSObject.Properties.Name -contains $name) {
            $number = 0.0
            if ([double]::TryParse(
                    [string]$Object.$name,
                    [System.Globalization.NumberStyles]::Float,
                    [System.Globalization.CultureInfo]::InvariantCulture,
                    [ref]$number)) {
                return $number
            }
        }
    }

    return $null
}

function Get-CaptureRectangle {
    param(
        [Parameter(Mandatory = $true)]$Record,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $rect = Get-JsonPropertyValue -Object $Record -Name $Name
    if ($null -eq $rect) {
        return $null
    }

    $x = Get-CaptureRectangleNumber -Object $rect -Names @('X', 'Left')
    $y = Get-CaptureRectangleNumber -Object $rect -Names @('Y', 'Top')
    $width = Get-CaptureRectangleNumber -Object $rect -Names @('Width')
    $height = Get-CaptureRectangleNumber -Object $rect -Names @('Height')
    if ($null -eq $x -or $null -eq $y -or $null -eq $width -or $null -eq $height) {
        return $null
    }
    if ($width -le 0 -or $height -le 0) {
        return $null
    }

    return [pscustomobject]@{
        X = $x
        Y = $y
        Width = $width
        Height = $height
    }
}

function Test-CaptureRectanglesEquivalent {
    param(
        $Left,
        $Right,
        [double]$TolerancePixels = 2.0
    )

    if ($null -eq $Left -or $null -eq $Right) {
        return $false
    }

    return [Math]::Abs($Left.X - $Right.X) -le $TolerancePixels -and
           [Math]::Abs($Left.Y - $Right.Y) -le $TolerancePixels -and
           [Math]::Abs($Left.Width - $Right.Width) -le $TolerancePixels -and
           [Math]::Abs($Left.Height - $Right.Height) -le $TolerancePixels
}

function Get-CaptureImageCrop {
    param([Parameter(Mandatory = $true)]$Record)

    $virtualScreen = Get-CaptureRectangle -Record $Record -Name 'VirtualScreen'
    $windowBounds = Get-CaptureRectangle -Record $Record -Name 'DesktopWindowBounds'
    if ($null -eq $virtualScreen -or $null -eq $windowBounds) {
        return $null
    }

    return [pscustomobject]@{
        X = [int][Math]::Floor($windowBounds.X - $virtualScreen.X)
        Y = [int][Math]::Floor($windowBounds.Y - $virtualScreen.Y)
        Width = [int][Math]::Ceiling($windowBounds.Width)
        Height = [int][Math]::Ceiling($windowBounds.Height)
    }
}

function Measure-ImageSampleDifference {
    param(
        [Parameter(Mandatory = $true)][string]$LeftPath,
        [Parameter(Mandatory = $true)][string]$RightPath,
        [Parameter(Mandatory = $true)]$Crop
    )

    $leftStream = $null
    $rightStream = $null
    $leftBitmap = $null
    $rightBitmap = $null
    try {
        foreach ($imagePath in @($LeftPath, $RightPath)) {
            $imageInfo = Get-Item -LiteralPath $imagePath -ErrorAction Stop
            if ($imageInfo.Length -gt $script:MaxScreenshotDifferenceBytes) {
                throw "Screenshot '$imagePath' is too large for safe stasis comparison: $($imageInfo.Length) bytes."
            }
        }

        $leftBytes = [System.IO.File]::ReadAllBytes($LeftPath)
        $rightBytes = [System.IO.File]::ReadAllBytes($RightPath)
        $leftStream = [System.IO.MemoryStream]::new($leftBytes)
        $rightStream = [System.IO.MemoryStream]::new($rightBytes)
        $leftBitmap = [System.Drawing.Bitmap]::FromStream($leftStream)
        $rightBitmap = [System.Drawing.Bitmap]::FromStream($rightStream)

        $x = [Math]::Max(0, [int]$Crop.X)
        $y = [Math]::Max(0, [int]$Crop.Y)
        $width = [Math]::Min([int]$Crop.Width, [Math]::Min($leftBitmap.Width, $rightBitmap.Width) - $x)
        $height = [Math]::Min([int]$Crop.Height, [Math]::Min($leftBitmap.Height, $rightBitmap.Height) - $y)
        if ($width -le 2 -or $height -le 2) {
            return $null
        }

        $sampleColumns = 48
        $sampleRows = 27
        $total = 0.0
        $changed = 0
        $count = 0
        for ($row = 0; $row -lt $sampleRows; $row++) {
            $sampleY = $y + [int][Math]::Round((($height - 1) * $row) / [Math]::Max(1, $sampleRows - 1))
            for ($column = 0; $column -lt $sampleColumns; $column++) {
                $sampleX = $x + [int][Math]::Round((($width - 1) * $column) / [Math]::Max(1, $sampleColumns - 1))
                $leftPixel = $leftBitmap.GetPixel($sampleX, $sampleY)
                $rightPixel = $rightBitmap.GetPixel($sampleX, $sampleY)
                $difference = ([Math]::Abs($leftPixel.R - $rightPixel.R) + [Math]::Abs($leftPixel.G - $rightPixel.G) + [Math]::Abs($leftPixel.B - $rightPixel.B)) / 3.0
                $total += $difference
                if ($difference -gt 0.0) {
                    $changed++
                }

                $count++
            }
        }

        if ($count -eq 0) {
            return $null
        }

        return [pscustomobject]@{
            MeanAbsDiff = $total / $count
            ChangedSampleFraction = $changed / [double]$count
            SampleCount = $count
        }
    }
    finally {
        if ($null -ne $leftBitmap) { $leftBitmap.Dispose() }
        if ($null -ne $rightBitmap) { $rightBitmap.Dispose() }
        if ($null -ne $leftStream) { $leftStream.Dispose() }
        if ($null -ne $rightStream) { $rightStream.Dispose() }
    }
}

function Find-RenderedSurfaceStasis {
    param([Parameter(Mandatory = $true)][string]$RunDirectory)

    $records = @(Read-ScreenCaptureManifest -RunDirectory $RunDirectory |
        Where-Object { [string](Get-JsonPropertyValue -Object $_ -Name 'FileName' -Default '') -match '^desktop-\d+\.png$' } |
        Sort-Object { Get-CaptureSequenceNumber -Record $_ })
    if ($records.Count -lt ($MinimumStaticPairs + 1)) {
        return @()
    }

    $segments = New-Object System.Collections.Generic.List[object]
    $currentSegment = $null

    for ($index = 1; $index -lt $records.Count; $index++) {
        $previous = $records[$index - 1]
        $current = $records[$index]
        $previousBounds = Get-CaptureRectangle -Record $previous -Name 'DesktopWindowBounds'
        $currentBounds = Get-CaptureRectangle -Record $current -Name 'DesktopWindowBounds'
        $previousVirtualScreen = Get-CaptureRectangle -Record $previous -Name 'VirtualScreen'
        $currentVirtualScreen = Get-CaptureRectangle -Record $current -Name 'VirtualScreen'

        if (-not (Test-CaptureRectanglesEquivalent -Left $previousBounds -Right $currentBounds) -or
            -not (Test-CaptureRectanglesEquivalent -Left $previousVirtualScreen -Right $currentVirtualScreen)) {
            if ($null -ne $currentSegment -and $currentSegment.StaticPairs -ge $MinimumStaticPairs) {
                [void]$segments.Add($currentSegment)
            }
            $currentSegment = $null
            continue
        }

        $previousPath = Get-CaptureImagePath -Record $previous -RunDirectory $RunDirectory
        $currentPath = Get-CaptureImagePath -Record $current -RunDirectory $RunDirectory
        $crop = Get-CaptureImageCrop -Record $previous
        if ([string]::IsNullOrWhiteSpace($previousPath) -or [string]::IsNullOrWhiteSpace($currentPath) -or $null -eq $crop) {
            if ($null -ne $currentSegment -and $currentSegment.StaticPairs -ge $MinimumStaticPairs) {
                [void]$segments.Add($currentSegment)
            }
            $currentSegment = $null
            continue
        }

        $difference = Measure-ImageSampleDifference -LeftPath $previousPath -RightPath $currentPath -Crop $crop
        $isStaticPair = $null -ne $difference -and
            $difference.MeanAbsDiff -le 0.01 -and
            $difference.ChangedSampleFraction -le 0.001
        if ($isStaticPair) {
            if ($null -eq $currentSegment) {
                $currentSegment = [pscustomobject]@{
                    StartFile = [string](Get-JsonPropertyValue -Object $previous -Name 'FileName' -Default '')
                    EndFile = [string](Get-JsonPropertyValue -Object $current -Name 'FileName' -Default '')
                    StartCapturedAt = [string](Get-JsonPropertyValue -Object $previous -Name 'CapturedAt' -Default '')
                    EndCapturedAt = [string](Get-JsonPropertyValue -Object $current -Name 'CapturedAt' -Default '')
                    StaticPairs = 1
                    MaxMeanAbsDiff = [double]$difference.MeanAbsDiff
                    MaxChangedSampleFraction = [double]$difference.ChangedSampleFraction
                    Crop = $crop
                }
            }
            else {
                $currentSegment.EndFile = [string](Get-JsonPropertyValue -Object $current -Name 'FileName' -Default '')
                $currentSegment.EndCapturedAt = [string](Get-JsonPropertyValue -Object $current -Name 'CapturedAt' -Default '')
                $currentSegment.StaticPairs = [int]$currentSegment.StaticPairs + 1
                $currentSegment.MaxMeanAbsDiff = [Math]::Max([double]$currentSegment.MaxMeanAbsDiff, [double]$difference.MeanAbsDiff)
                $currentSegment.MaxChangedSampleFraction = [Math]::Max([double]$currentSegment.MaxChangedSampleFraction, [double]$difference.ChangedSampleFraction)
            }
        }
        else {
            if ($null -ne $currentSegment -and $currentSegment.StaticPairs -ge $MinimumStaticPairs) {
                [void]$segments.Add($currentSegment)
            }
            $currentSegment = $null
        }
    }

    if ($null -ne $currentSegment -and $currentSegment.StaticPairs -ge $MinimumStaticPairs) {
        [void]$segments.Add($currentSegment)
    }

    return @($segments.ToArray())
}

$runs = @(Get-ValidationRunDirectories -Root $ResultRoot)
if ($runs.Count -eq 0) { throw "No UX validation run directories with ux-deep-summary.json were found under $ResultRoot" }
$findings = New-Object System.Collections.Generic.List[object]
$runSummaries = New-Object System.Collections.Generic.List[object]

foreach ($run in $runs) {
    $summaryPath = Join-Path $run.FullName 'ux-deep-summary.json'
    $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
    $runId = [string](Get-JsonPropertyValue -Object $summary -Name 'ResultName' -Default $run.Name)
    $faultProfile = [string](Get-JsonPropertyValue -Object $summary -Name 'FaultProfile' -Default 'none')
    $isLongRunSoak = ConvertTo-ValidationBool (Get-JsonPropertyValue -Object $summary -Name 'IsLongRunSoak' -Default $false)
    [void]$runSummaries.Add([pscustomobject]@{ resultName=$runId; path=$run.FullName; isLongRunSoak=$isLongRunSoak; configPhaseStatus=[string](Get-JsonPropertyValue -Object $summary -Name 'ConfigPhaseStatus'); desktopPhaseStatus=[string](Get-JsonPropertyValue -Object $summary -Name 'DesktopPhaseStatus'); fullScreenToggleStatus=[string](Get-JsonPropertyValue -Object $summary -Name 'FullScreenToggleStatus'); notes=@(Get-JsonPropertyValue -Object $summary -Name 'Notes' -Default @()) })

    foreach ($statusName in @('ConfigPhaseStatus','DesktopPhaseStatus','FullScreenToggleStatus')) {
        $status = [string](Get-JsonPropertyValue -Object $summary -Name $statusName)
        $isExpectedLongRunDesktopHost = $isLongRunSoak -and
                                      $statusName -eq 'DesktopPhaseStatus' -and
                                      $status -eq 'Running'
        if ($status -ne 'Completed' -and -not $isExpectedLongRunDesktopHost) {
            [void]$findings.Add((New-Finding -Code "harness-$($statusName.ToLowerInvariant())" -Title "VM validation phase did not complete: $statusName" -Area 'VM validation harness' -Severity 'High' -Evidence @("Run ${runId} reported ${statusName}=$status.", "Summary: $summaryPath") -Notes @('Incomplete harness phases are release-candidate blockers.')))
        }
    }

    $screenshots = @(Get-ChildItem -LiteralPath $run.FullName -File -Include '*.png' -Recurse -ErrorAction SilentlyContinue)
    if ($screenshots.Count -lt $MinimumScreenshots) {
        [void]$findings.Add((New-Finding -Code 'capture-count-low' -Title 'VM validation produced too few screenshots for visual proof' -Area 'VM validation harness' -Severity 'Medium' -Evidence @("Run ${runId} produced $($screenshots.Count) screenshot(s); minimum expected is $MinimumScreenshots.", "Run directory: $($run.FullName)") -Notes @('Visual validation needs enough captures to verify background, graph-card, news, and ribbon behavior.')))
    }

    $targetCaptureFrames = Get-JsonIntPropertyValue -Object $summary -Name 'TargetCaptureFrames'
    $desktopShots = Get-JsonIntPropertyValue -Object $summary -Name 'DesktopShots'
    if ($null -ne $targetCaptureFrames -and
        $null -ne $desktopShots -and
        $targetCaptureFrames -ge 10 -and
        $desktopShots -lt [Math]::Floor($targetCaptureFrames * 0.5)) {
        [void]$findings.Add((New-Finding -Code 'capture-loop-starved' -Title 'VM validation capture loop yielded too few runtime frames' -Area 'VM validation harness' -Severity 'High' -Evidence @(
            "Run ${runId} targeted $targetCaptureFrames capture frame(s) but recorded DesktopShots=$desktopShots.",
            "Minimum blocking completion ratio is 50 percent; 50-80 percent is retained as a VM summary coverage note because runtime freshness telemetry still spans the run.",
            "Run directory: $($run.FullName)"
        ) -Notes @('A clean visual validation report requires enough runtime frames to verify UI fluidity, background rotation, news scroller, graph cards, and ticker ribbons.')))
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

    $captureManifestRecords = @(Read-ScreenCaptureManifest -RunDirectory $run.FullName)
    $desktopCaptureManifestRecords = @($captureManifestRecords |
        Where-Object { [string](Get-JsonPropertyValue -Object $_ -Name 'FileName' -Default '') -match '^desktop-\d+\.png$' })
    $missingWindowBoundsRecords = @($desktopCaptureManifestRecords |
        Where-Object { $null -eq (Get-CaptureRectangle -Record $_ -Name 'DesktopWindowBounds') })
    if ($desktopCaptureManifestRecords.Count -gt 0 -and $missingWindowBoundsRecords.Count -gt 0) {
        $missingWindowBoundsSeverity = if ($missingWindowBoundsRecords.Count -eq $desktopCaptureManifestRecords.Count) { 'High' } else { 'Medium' }
        $missingWindowBoundsSample = @($missingWindowBoundsRecords |
            Select-Object -First 8 |
            ForEach-Object { [string](Get-JsonPropertyValue -Object $_ -Name 'FileName' -Default '<unknown>') })
        [void]$findings.Add((New-Finding -Code 'screen-capture-window-bounds-missing' -Title 'Screen capture manifest lacks app-window bounds for stasis analysis' -Area 'desktop_rendering_reliability' -Severity $missingWindowBoundsSeverity -Evidence @(
            "Run ${runId} has $($missingWindowBoundsRecords.Count) desktop capture manifest record(s) without DesktopWindowBounds out of $($desktopCaptureManifestRecords.Count).",
            "Sample missing-bound capture(s): $($missingWindowBoundsSample -join ', ').",
            "Run directory: $($run.FullName)"
        ) -Notes @('Rendered-surface stasis analysis needs app-window bounds; missing bounds make affected capture pairs unprovable rather than clean.')))
    }

    $stasisSegments = @(Find-RenderedSurfaceStasis -RunDirectory $run.FullName)
    if ($stasisSegments.Count -gt 0) {
        $runtimeFreshnessPath = Join-Path $run.FullName 'runtime-freshness-events.log'
        $runtimeFreshnessLineCount = if (Test-Path -LiteralPath $runtimeFreshnessPath) {
            @(Get-Content -LiteralPath $runtimeFreshnessPath).Count
        } else {
            0
        }
        foreach ($segment in $stasisSegments) {
            [void]$findings.Add((New-Finding -Code 'rendered-surface-stasis' -Title 'Desktop rendered surface stayed pixel-static across runtime captures' -Area 'desktop_rendering_reliability' -Severity 'High' -Evidence @(
                "Run ${runId} app-window crop remained unchanged from $($segment.StartFile) to $($segment.EndFile).",
                "CapturedAt range: $($segment.StartCapturedAt) to $($segment.EndCapturedAt).",
                "Static adjacent pairs: $($segment.StaticPairs); max mean absolute RGB diff=$([Math]::Round([double]$segment.MaxMeanAbsDiff, 4)); max changed-sample fraction=$([Math]::Round([double]$segment.MaxChangedSampleFraction, 4)).",
                "runtime-freshness-events.log line count during run: $runtimeFreshnessLineCount.",
                "Run directory: $($run.FullName)"
            ) -Notes @('The stasis detector compares only stable app-window crops from screen-captures.jsonl, so this indicates the presented WPF surface stopped changing while harness/runtime evidence may still be live.')))
        }
    }

    $traceFiles = @(Get-ChildItem -LiteralPath $run.FullName -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '(?i)(trace|circular|events|log)' })
    $traceLines = New-Object System.Collections.Generic.List[string]
    foreach ($trace in $traceFiles) {
        $hits = @(Select-String -LiteralPath $trace.FullName -Pattern '(?i)\b(error|fatal|exception|failed|timeout|warning|warn|missing|blank|source-missing|jitter|burst|unhandled)\b' -ErrorAction SilentlyContinue | Select-Object -First 40)
        foreach ($hit in $hits) {
            $line = "{0}:{1}: {2}" -f $trace.Name, $hit.LineNumber, $hit.Line.Trim()
            if (-not (Test-TraceLineAllowed -Line $line) -and
                -not (Test-FaultInjectionTraceLineAllowed -Line $line -FaultProfile $faultProfile)) {
                [void]$traceLines.Add($line)
            }
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

    $offlineFaultProfile = $faultProfile -match '(?i)offline'
    # Keep this list in sync with runtime-scoped offline profiles in Guest-UxDeepExercise.ps1.
    # Config-only offline profiles prove fault activation, then clear before runtime UX validation.
    $runtimeOfflineFaultProfile = $faultProfile -in @('offline-at-start', 'offline-during-runtime', 'offline-then-recover-runtime')
    if ($offlineFaultProfile) {
        $faultTrace = Join-Path $run.FullName 'fault-injection-events.log'
        $combinedTrace = Join-Path $run.FullName 'combined-trace-tail.txt'
        $freshnessTrace = Join-Path $run.FullName 'runtime-freshness-events.log'
        # These trace strings are a validation contract. If runtime trace field
        # names or freshness text changes, update these patterns and fixtures.
        $faultActivationMatches = @(if (Test-Path -LiteralPath $faultTrace) {
            Select-String -LiteralPath $faultTrace -Pattern 'profile=offline' -ErrorAction SilentlyContinue |
                Where-Object { $null -ne $_ }
        })
        $faultActivated = $faultActivationMatches.Count -gt 0

        if (-not $faultActivated) {
            [void]$findings.Add((New-Finding -Code 'offline-fault-injection-unverified' -Title 'Offline fault run did not prove fault activation' -Area 'degraded_mode_harness' -Severity 'High' -Evidence @("Run ${runId} used FaultProfile=$faultProfile.", "Expected fault-injection-events.log to contain profile=offline.") -Notes @('Every offline fault profile must prove the harness actually injected the offline condition.')))
            continue
        }

        if (-not $runtimeOfflineFaultProfile) {
            continue
        }

        $offlineFreshnessHits = @()
        # The product trace and harness snapshot can report the same state; for
        # degraded-mode proof, corroborating hits are benign because validation
        # only requires presence and uses source-scoped line ordering below.
        if (Test-Path -LiteralPath $combinedTrace) {
            $offlineFreshnessHits += @(Select-String -LiteralPath $combinedTrace -Pattern 'data_freshness_text=OFFLINE' -ErrorAction SilentlyContinue |
                Where-Object { $null -ne $_ })
        }
        if (Test-Path -LiteralPath $freshnessTrace) {
            $offlineFreshnessHits += @(Select-String -LiteralPath $freshnessTrace -Pattern 'latest_freshness=OFFLINE' -ErrorAction SilentlyContinue |
                Where-Object { $null -ne $_ })
        }
        $offlineFreshnessSample = @($offlineFreshnessHits | Select-Object -First 8)

        if (-not $faultActivated -or $offlineFreshnessHits.Count -eq 0) {
            $offlineEvidence = @(
                "Run ${runId} used FaultProfile=$faultProfile.",
                "Fault activation observed: $faultActivated.",
                "Offline freshness trace hits: $($offlineFreshnessHits.Count).",
                "Expected a trace line containing data_freshness_text=OFFLINE after offline fault injection."
            )
            [void]$findings.Add((New-Finding -Code 'offline-ux-state-unverified' -Title 'Offline fault run did not prove a user-visible offline data-freshness state' -Area 'degraded_mode_ux' -Severity 'High' -Evidence $offlineEvidence -Notes @('Offline/degraded validation must prove that stale/cached data is clearly surfaced to the user.')))
        }

        $faultActivatedAtUtc = $null
        if ($faultActivationMatches.Count -gt 0) {
            $faultActivatedAtUtc = Get-TraceLineTimestampUtc -Line (@($faultActivationMatches | Sort-Object LineNumber | Select-Object -First 1)[0].Line)
        }

        $offlineTransitionHits = @()
        if (Test-Path -LiteralPath $combinedTrace) {
            $offlineTransitionHits = @(Select-String -LiteralPath $combinedTrace -Pattern 'event=RuntimeDataFreshnessChanged.*data_freshness_text=OFFLINE' -ErrorAction SilentlyContinue |
                Where-Object { $null -ne $_ })
        }
        if (Test-Path -LiteralPath $freshnessTrace) {
            $offlineTransitionHits += @(Select-String -LiteralPath $freshnessTrace -Pattern 'latest_freshness=OFFLINE' -ErrorAction SilentlyContinue |
                Where-Object { $null -ne $_ })
        }

        $timelyOfflineTransitionHit = $null
        $offlineTransitionDelaySeconds = $null
        if ($null -ne $faultActivatedAtUtc) {
            foreach ($hit in @($offlineTransitionHits | Sort-Object @{ Expression = {
                            $timestampUtc = Get-TraceLineTimestampUtc -Line $_.Line
                            if ($null -eq $timestampUtc) { [DateTimeOffset]::MaxValue } else { $timestampUtc }
                        } })) {
                $hitTimestampUtc = Get-TraceLineTimestampUtc -Line $hit.Line
                if ($null -eq $hitTimestampUtc -or $hitTimestampUtc -lt $faultActivatedAtUtc) {
                    continue
                }

                $offlineTransitionDelaySeconds = [Math]::Round(($hitTimestampUtc - $faultActivatedAtUtc).TotalSeconds, 3)
                if ($offlineTransitionDelaySeconds -le 2.5) {
                    $timelyOfflineTransitionHit = $hit
                }
                break
            }
        }

        if ($null -eq $timelyOfflineTransitionHit) {
            $timingEvidence = @(
                "Run ${runId} used FaultProfile=$faultProfile.",
                "Fault activation timestamp: $(if ($null -eq $faultActivatedAtUtc) { 'unavailable' } else { $faultActivatedAtUtc.ToString('o') }).",
                "Offline freshness transition hits: $($offlineTransitionHits.Count).",
                "First offline transition delay seconds: $(if ($null -eq $offlineTransitionDelaySeconds) { 'unavailable' } else { $offlineTransitionDelaySeconds }).",
                "Expected RuntimeDataFreshnessChanged to OFFLINE within 2.5 seconds of runtime offline fault activation."
            )
            foreach ($hit in @($offlineTransitionHits | Select-Object -First 5)) {
                $timingEvidence += ("{0}:{1}: {2}" -f (Split-Path -Leaf $hit.Path), $hit.LineNumber, $hit.Line.Trim())
            }
            [void]$findings.Add((New-Finding -Code 'offline-ux-state-delay' -Title 'Offline fault run did not prove prompt visible offline feedback' -Area 'degraded_mode_ux' -Severity 'High' -Evidence $timingEvidence -Notes @('CR-086 requires degraded-state feedback to reach the user promptly; runtime offline status should transition within roughly two quote-cadence ticks.')))
        }

        if ($faultProfile -eq 'offline-then-recover-runtime') {
            $targetCaptureFrames = [int](Get-JsonPropertyValue -Object $summary -Name 'TargetCaptureFrames' -Default 0)
            if ($targetCaptureFrames -lt 2) {
                [void]$findings.Add((New-Finding -Code 'offline-recovery-insufficient-captures' -Title 'Recovery fault run did not capture enough frames to prove offline before recovery' -Area 'degraded_mode_ux' -Severity 'High' -Evidence @("Run ${runId} used FaultProfile=$faultProfile with TargetCaptureFrames=$targetCaptureFrames.", 'Recovery validation requires at least two frames: one to prove the offline state and one after clearing the fault.') -Notes @('Increase run duration or decrease capture interval for recovery validation.')))
            }

            # Startup writes an initial profile=none line. For recovery proof, only
            # clears and LIVE status lines after the injected offline phase count.
            $lastOfflineProfileLine = if ($faultActivationMatches.Count -gt 0) {
                @($faultActivationMatches | Sort-Object LineNumber | Select-Object -Last 1)[0].LineNumber
            } else {
                0
            }
            $faultClearMatches = @(if (Test-Path -LiteralPath $faultTrace) {
                Select-String -LiteralPath $faultTrace -Pattern 'profile=none' -ErrorAction SilentlyContinue |
                    Where-Object { $null -ne $_ -and $_.LineNumber -gt $lastOfflineProfileLine }
            })
            $freshnessScopedOfflineHits = @($offlineFreshnessHits | Where-Object { $_.Path -eq $freshnessTrace })
            $recoveredFreshnessHits = @()
            # Recovery proof is intentionally source-local: an OFFLINE marker and
            # later LIVE marker must be ordered within the same evidence file.
            # Harness freshness snapshots also carry the effective fault profile;
            # for those, order recovery against the last OFFLINE snapshot captured
            # while the fault was active. Later trace-only stale OFFLINE tail reads
            # after the fault clears should not erase a UI-visible LIVE recovery.
            if ($freshnessScopedOfflineHits.Count -gt 0 -and (Test-Path -LiteralPath $freshnessTrace)) {
                $freshnessOfflineRecoveryBasisHits = @($freshnessScopedOfflineHits | Where-Object { $_.Line -match 'effective_fault_profile=offline' })
                if ($freshnessOfflineRecoveryBasisHits.Count -eq 0) {
                    $freshnessOfflineRecoveryBasisHits = $freshnessScopedOfflineHits
                }
                $lastFreshnessOfflineLine = @($freshnessOfflineRecoveryBasisHits | Sort-Object LineNumber | Select-Object -Last 1)[0].LineNumber
                $recoveredFreshnessHits += @(Select-String -LiteralPath $freshnessTrace -Pattern 'latest_freshness=LIVE quote feed' -ErrorAction SilentlyContinue |
                    Where-Object {
                        if ($null -eq $_ -or $_.LineNumber -le $lastFreshnessOfflineLine -or $_.Line -notmatch 'effective_fault_profile=none') { return $false }

                        # The harness marks source=ui when direct UI text agrees with moving trace evidence.
                        $hasDirectUiFreshness = $_.Line -match 'latest_freshness_source=ui(\s|$)' -and $_.Line -match 'ui_freshness=LIVE quote feed'
                        $hasFreshTraceAge = Test-TraceAgeFieldFresh -Line $_.Line -FieldName 'trace_age_seconds'

                        return $hasDirectUiFreshness -and $hasFreshTraceAge
                    })
            }
            $recoveredFreshnessEvidenceHits = @($recoveredFreshnessHits | Select-Object -First 8)

            if (-not $faultActivated -or $faultClearMatches.Count -eq 0 -or $recoveredFreshnessHits.Count -eq 0) {
                $recoveryEvidence = New-Object System.Collections.Generic.List[string]
                $recoveryEvidence.Add("Run ${runId} used FaultProfile=$faultProfile.")
                $recoveryEvidence.Add("Fault activation observed: $faultActivated.")
                $recoveryEvidence.Add("Fault clear after offline observed: $($faultClearMatches.Count -gt 0).")
                $recoveryEvidence.Add("Recovered LIVE freshness trace hits: $($recoveredFreshnessHits.Count).")
                $recoveryEvidence.Add("Expected profile=none after the last profile=offline line in fault-injection-events.log and latest_freshness=LIVE quote feed in runtime-freshness-events.log after the last OFFLINE freshness line, backed by direct UI freshness and trace_age_seconds <= 180.")
                foreach ($hit in $offlineFreshnessSample) {
                    $recoveryEvidence.Add(("{0}:{1}: {2}" -f (Split-Path -Leaf $hit.Path), $hit.LineNumber, $hit.Line.Trim()))
                }
                foreach ($hit in $recoveredFreshnessEvidenceHits) {
                    $recoveryEvidence.Add(("{0}:{1}: {2}" -f (Split-Path -Leaf $hit.Path), $hit.LineNumber, $hit.Line.Trim()))
                }
                [void]$findings.Add((New-Finding -Code 'offline-recovery-ux-state-unverified' -Title 'Recovery fault run did not prove return to live data-freshness state' -Area 'degraded_mode_ux' -Severity 'High' -Evidence @($recoveryEvidence.ToArray()) -Notes @('Recovery validation must prove that user-visible stale/offline state returns to live after the injected fault clears.')))
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $repoRoot ('build\validation\artifacts\visual-validation-analysis-{0}.json' -f (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force -Path (Split-Path -Path $OutputPath -Parent) | Out-Null
$findingArray = @($findings.ToArray())
$runSummaryArray = @($runSummaries.ToArray())
$report = [ordered]@{ generatedAt=(Get-Date).ToString('o'); resultRoot=(Resolve-Path -LiteralPath $ResultRoot).Path; runCount=$runs.Count; clean=($findingArray.Count -eq 0); findings=$findingArray; runs=$runSummaryArray }
$report | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

$deepSeekReviewLine = $null
if (-not $SkipDeepSeekArtifactReview) {
    $deepSeekReviewPath = Join-Path (Split-Path -Path $OutputPath -Parent) ('deepseek-artifact-review-{0}.md' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    try {
        # This is intentionally terminal for normal workflows: the project requires
        # a DeepSeek advisory second opinion before final artifact interpretation.
        $deepSeekOutput = & (Join-Path $PSScriptRoot 'Invoke-DeepSeekArtifactReview.ps1') -ResultRoot $ResultRoot -AnalysisReportPath $OutputPath -OutputPath $deepSeekReviewPath
        $reviewLine = @($deepSeekOutput | Where-Object { $_ -like 'DEEPSEEK_ARTIFACT_REVIEW=*' } | Select-Object -Last 1)
        if ($reviewLine.Count -eq 0) { throw 'DeepSeek artifact second-opinion review did not report DEEPSEEK_ARTIFACT_REVIEW.' }
        $deepSeekReviewLine = $reviewLine[0]
    }
    catch {
        Write-Output ("DEEPSEEK_ARTIFACT_REVIEW_FAILED=" + $_.Exception.Message)
        throw
    }
}

if ($CreateChangeRequests -and $findings.Count -gt 0) {
    foreach ($finding in $findings) {
        & (Join-Path $PSScriptRoot 'Add-AuditChangeRequest.ps1') -Title $finding.title -Area $finding.area -Severity $finding.severity -Priority 1 -Source 'autonomous_visual_validation' -Evidence @($finding.evidence) -Notes @($finding.notes) | Out-Host
    }
}
Write-Output ("ANALYSIS_REPORT=" + $OutputPath)
Write-Output ("ANALYSIS_CLEAN=" + [string]($findings.Count -eq 0))
Write-Output ("ANALYSIS_FINDINGS=" + $findings.Count)
if (-not [string]::IsNullOrWhiteSpace($deepSeekReviewLine)) { Write-Output $deepSeekReviewLine }
