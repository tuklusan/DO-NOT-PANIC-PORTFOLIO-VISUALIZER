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
