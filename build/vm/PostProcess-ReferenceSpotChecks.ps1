param(
    [Parameter(Mandatory = $true)][string]$ResultRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-EnvValue {
    param([Parameter(Mandatory = $true)][string]$Name)

    foreach ($scope in 'Process', 'User', 'Machine') {
        $value = [Environment]::GetEnvironmentVariable($Name, $scope)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }

    return ''
}

function Read-CircularTraceText {
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$IndexPath
    )

    if (-not (Test-Path $LogPath)) {
        return ''
    }

    $bytes = [System.IO.File]::ReadAllBytes($LogPath)
    if ($bytes.Length -eq 0) {
        return ''
    }

    $position = 0
    if (Test-Path $IndexPath) {
        $positionText = Get-Content -LiteralPath $IndexPath -Raw -ErrorAction SilentlyContinue
        $positionRaw = ''
        if ($null -ne $positionText) {
            $positionRaw = [string]$positionText
        }
        $null = [int]::TryParse($positionRaw.Trim(), [ref]$position)
        $position = [Math]::Max(0, [Math]::Min($position, $bytes.Length))
    }

    $orderedBytes = if ($position -eq 0) {
        $bytes
    }
    else {
        $suffixLength = $bytes.Length - $position
        $ordered = New-Object byte[] $bytes.Length
        [Array]::Copy($bytes, $position, $ordered, 0, $suffixLength)
        [Array]::Copy($bytes, 0, $ordered, $suffixLength, $position)
        $ordered
    }

    return ([System.Text.Encoding]::UTF8.GetString($orderedBytes)).Replace("`0", '')
}

function Try-ParseInvariantDecimal {
    param([string]$Text)

    $value = [decimal]::Zero
    if ([decimal]::TryParse($Text, [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$value)) {
        return $value
    }

    return $null
}

function Parse-DisplayedTapeSamples {
    param([Parameter(Mandatory = $true)][string]$TraceText)

    $lines = ($TraceText -split "`r?`n") |
        Where-Object { $_ -like '*event=DisplayedTapeSample*' }

    $records = @()
    $index = 0
    foreach ($line in $lines) {
        $index++
        $timestamp = ($line -split '\s+\|\s+', 2)[0]
        $match = [regex]::Match($line, 'sample=\[(.*)\]\s*$')
        if (-not $match.Success) {
            continue
        }

        $items = @()
        foreach ($entry in ($match.Groups[1].Value -split ', ')) {
            $parts = $entry -split '~', 4
            if ($parts.Count -lt 4) { continue }
            $items += [pscustomobject]@{
                Symbol = [string]$parts[0]
                LastText = [string]$parts[1]
                ChangeText = [string]$parts[2]
                State = [string]$parts[3]
            }
        }

        if ($items.Count -gt 0) {
            $records += [pscustomobject]@{
                CapturedAt = $timestamp
                CaptureIndex = $index
                DisplayedSample = $items
            }
        }
    }

    return @($records)
}

function Test-IsDisplayedSampleFullyLive {
    param([Parameter(Mandatory = $true)]$SampleRecord)

    $items = @($SampleRecord.DisplayedSample)
    if ($items.Count -eq 0) {
        return $false
    }

    return -not ($items | Where-Object { [string]$_.State -ne 'live' } | Select-Object -First 1)
}

function Get-PreferredDisplayedTapeSample {
    param([Parameter(Mandatory = $true)][object[]]$Samples)

    $fullyLive = @($Samples | Where-Object { Test-IsDisplayedSampleFullyLive -SampleRecord $_ })
    if ($fullyLive.Count -gt 0) {
        return $fullyLive[-1]
    }

    return $Samples[-1]
}

function Get-ReferenceResults {
    param([Parameter(Mandatory = $true)][string[]]$Symbols)

    return Get-YahooReferenceResults -Symbols $Symbols
}

function Get-YahooReferenceResults {
    param([Parameter(Mandatory = $true)][string[]]$Symbols)

    $results = @()
    $error = $null
    try {
        $encodedSymbols = [Uri]::EscapeDataString(($Symbols -join ','))
        $response = Invoke-RestMethod -Uri ("https://query1.finance.yahoo.com/v7/finance/quote?symbols=$encodedSymbols") -TimeoutSec 20 -Headers @{ 'User-Agent' = 'PortfolioSaverVmHarness/1.0' }
        foreach ($quote in ($response.quoteResponse.result | Where-Object { $_ -ne $null })) {
            $results += [pscustomobject]@{
                Symbol = [string]$quote.symbol
                Last = Try-ParseInvariantDecimal -Text ([string]$quote.regularMarketPrice)
                ChangePercent = Try-ParseInvariantDecimal -Text ([string]$quote.regularMarketChangePercent)
                MarketTime = if ($quote.regularMarketTime) { [DateTimeOffset]::FromUnixTimeSeconds([long]$quote.regularMarketTime).ToString('o') } else { $null }
                Currency = [string]$quote.currency
            }
        }
    }
    catch {
        $error = $_.Exception.Message
    }

    return [pscustomobject]@{
        Source = 'YahooFinanceQuote'
        Results = @($results)
        Error = $error
    }
}

function Build-ComparisonEntries {
    param(
        [Parameter(Mandatory = $true)]$DisplayedSample,
        [Parameter(Mandatory = $true)]$ReferenceResults
    )

    $resultMap = @{}
    foreach ($result in @($ReferenceResults)) {
        if ($null -ne $result.Symbol) {
            $resultMap[[string]$result.Symbol] = $result
        }
    }

    $comparisons = @()
    foreach ($displayed in @($DisplayedSample)) {
        $symbol = [string]$displayed.Symbol
        $state = [string]$displayed.State
        if (-not $resultMap.ContainsKey($symbol)) {
            $comparisons += [pscustomobject]@{
                Symbol = $symbol
                State = $state
                Status = 'reference-missing'
            }
            continue
        }

        $reference = $resultMap[$symbol]
        $entry = [ordered]@{
            Symbol = $symbol
            State = $state
            DisplayedLast = [string]$displayed.LastText
            ReferenceLast = $reference.Last
        }

        if ($state -ne 'live') {
            $entry.Status = 'waiting'
            $comparisons += [pscustomobject]$entry
            continue
        }

        $displayedValue = Try-ParseInvariantDecimal -Text ([string]$displayed.LastText)
        $referenceValue = $reference.Last
        if ($null -eq $displayedValue -or $null -eq $referenceValue) {
            $entry.Status = 'unparsable'
            $comparisons += [pscustomobject]$entry
            continue
        }

        $absDiff = [Math]::Abs([decimal]$displayedValue - [decimal]$referenceValue)
        $pctDiff = if ([decimal]$referenceValue -ne 0) {
            [Math]::Abs(([double](([decimal]$displayedValue - [decimal]$referenceValue) / [decimal]$referenceValue)))
        }
        else {
            0.0
        }

        $entry.AbsoluteDifference = [decimal]::Round($absDiff, 4)
        $entry.PercentDifference = [Math]::Round($pctDiff * 100.0, 4)
        $entry.Status = if ($absDiff -le ([decimal]0.05) -or $pctDiff -le 0.0035) { 'close' } else { 'drift' }
        $comparisons += [pscustomobject]$entry
    }

    return @($comparisons)
}

$traceRoot = Join-Path $ResultRoot 'trace'
$tracePath = Join-Path $traceRoot 'trace.circular.log'
$indexPath = Join-Path $traceRoot 'trace.circular.idx'
$yfinanceTracePath = Join-Path $traceRoot 'yfinance.circular.log'
$yfinanceIndexPath = Join-Path $traceRoot 'yfinance.circular.idx'
$spotCheckPath = Join-Path $ResultRoot 'reference-spot-checks.jsonl'
$comparisonPath = Join-Path $ResultRoot 'reference-spot-check-comparisons.jsonl'
$combinedTracePath = Join-Path $ResultRoot 'combined-trace-tail.txt'

$traceText = Read-CircularTraceText -LogPath $tracePath -IndexPath $indexPath
$yfinanceTraceText = Read-CircularTraceText -LogPath $yfinanceTracePath -IndexPath $yfinanceIndexPath
$samples = Parse-DisplayedTapeSamples -TraceText $traceText

$sampleRecords = @()
$comparisonRecords = @()
$preferredSample = if ($samples.Count -gt 0) { Get-PreferredDisplayedTapeSample -Samples $samples } else { $null }
foreach ($sample in @($preferredSample)) {
    $symbols = @($sample.DisplayedSample | Select-Object -ExpandProperty Symbol -Unique)
    $reference = Get-ReferenceResults -Symbols $symbols
    $sampleRecords += [pscustomobject]@{
        CapturedAt = $sample.CapturedAt
        CaptureIndex = $sample.CaptureIndex
        Source = [string]$reference.Source
        SampleSelection = if (Test-IsDisplayedSampleFullyLive -SampleRecord $sample) { 'latest-fully-live' } else { 'latest-available' }
        Symbols = $symbols
        DisplayedSample = @($sample.DisplayedSample)
        Results = @($reference.Results)
        Error = $reference.Error
    }

    $comparisonRecords += [pscustomobject]@{
        CapturedAt = $sample.CapturedAt
        CaptureIndex = $sample.CaptureIndex
        Source = 'DisplayedVsReferenceFeed'
        ReferenceSource = [string]$reference.Source
        SampleSelection = if (Test-IsDisplayedSampleFullyLive -SampleRecord $sample) { 'latest-fully-live' } else { 'latest-available' }
        Comparisons = @(Build-ComparisonEntries -DisplayedSample $sample.DisplayedSample -ReferenceResults $reference.Results)
    }
}

$sampleRecords | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 6 } | Set-Content -LiteralPath $spotCheckPath -Encoding UTF8
$comparisonRecords | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 6 } | Set-Content -LiteralPath $comparisonPath -Encoding UTF8
@(
    "===== UI TRACE =====",
    $traceText,
    "",
    "===== YFINANCE TRACE =====",
    $yfinanceTraceText
) | Set-Content -LiteralPath $combinedTracePath -Encoding UTF8
