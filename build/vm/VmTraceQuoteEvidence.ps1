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
$ReferenceComparisonSchemaVersion = 2
$ExternalReferenceDisabledWarning = 'Independent external quote comparison is disabled by canonical policy; this check verifies UI rendering against YFinance.NET trace evidence only, not independent market-data correctness.'

function Try-ParseInvariantDecimal {
    param([string]$Text)

    $value = [decimal]::Zero
    if ([decimal]::TryParse($Text, [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$value)) {
        return $value
    }

    return $null
}

function Read-AllBytesShared {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $buffer = New-Object byte[] $stream.Length
        $offset = 0
        while ($offset -lt $buffer.Length) {
            $read = $stream.Read($buffer, $offset, $buffer.Length - $offset)
            if ($read -le 0) {
                break
            }

            $offset += $read
        }

        if ($offset -eq $buffer.Length) {
            return $buffer
        }

        $trimmed = New-Object byte[] $offset
        [Array]::Copy($buffer, $trimmed, $offset)
        return $trimmed
    }
    finally {
        $stream.Dispose()
    }
}

function Get-TraceFieldValue {
    param(
        [Parameter(Mandatory = $true)][string]$Line,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $eventIndex = $Line.IndexOf('event=QuoteResponseObserved')
    if ($eventIndex -ge 0) {
        $Line = $Line.Substring($eventIndex)
    }

    foreach ($token in ($Line -split ' / ')) {
        $separator = $token.IndexOf('=')
        if ($separator -le 0) {
            continue
        }

        $key = $token.Substring(0, $separator).Trim()
        if ($key -ne $Name) {
            continue
        }

        return $token.Substring($separator + 1).Trim()
    }

    return $null
}

function Test-YFinanceQuoteEvidenceParser {
    $sample = '2026-06-04T16:37:25.0000000+00:00 | INFO | program=YFinance.NET.Server | source=YFinanceServer | event=QuoteResponseObserved / operation=get_quotes / symbol=SPY / price=600.12 / change=1.23 / change_percent=0.20'
    $parsed = @(Parse-YFinanceQuoteEvidence -TraceText $sample -Symbols @('SPY'))
    return ($parsed.Count -eq 1 -and $parsed[0].Symbol -eq 'SPY' -and [Math]::Abs([decimal]$parsed[0].Last - [decimal]600.12) -le [decimal]0.001)
}

function Parse-YFinanceQuoteEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$TraceText,
        [Parameter(Mandatory = $true)][string[]]$Symbols
    )

    $requested = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($symbol in @($Symbols)) {
        if (-not [string]::IsNullOrWhiteSpace($symbol)) {
            [void]$requested.Add($symbol)
        }
    }

    $latest = @{}
    foreach ($line in ($TraceText -split "`r?`n")) {
        if ($line -notmatch 'event=QuoteResponseObserved') {
            continue
        }

        $symbol = Get-TraceFieldValue -Line $line -Name 'symbol'
        if ([string]::IsNullOrWhiteSpace($symbol) -or -not $requested.Contains($symbol)) {
            continue
        }

        $price = Try-ParseInvariantDecimal -Text ([string](Get-TraceFieldValue -Line $line -Name 'price'))
        $latest[$symbol] = [pscustomobject]@{
            Symbol = $symbol
            Last = $price
            Source = 'YFinanceTrace'
            ObservedAt = ($line -split ' \| ')[0]
        }
    }

    return @($latest.Values)
}
