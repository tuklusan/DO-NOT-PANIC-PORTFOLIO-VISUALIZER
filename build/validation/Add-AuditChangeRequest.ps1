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
    [Parameter(Mandatory = $true)][ValidateScript({ -not [string]::IsNullOrWhiteSpace($_) })][string]$Title,
    [Parameter(Mandatory = $true)][string]$Area,
    [Parameter(Mandatory = $true)][string]$Severity,
    [Parameter(Mandatory = $true)][string[]]$Evidence,
    [string[]]$Notes = @(),
    [int]$Priority = 2,
    [string]$Source = 'autonomous_visual_validation',
    [string]$AuditPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'docs\BETA6_AUDIT_STATE.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-NextChangeRequestId {
    param([Parameter(Mandatory = $true)]$AuditState)
    $max = 0

    $property = $AuditState.PSObject.Properties['change_requests']
    if ($null -eq $property) {
        return 'CR-001'
    }

    foreach ($request in @($property.Value)) {
        if ($null -eq $request) { continue }
        foreach ($idPropertyName in @('id', 'tracking_number')) {
            $idProperty = $request.PSObject.Properties[$idPropertyName]
            if ($null -eq $idProperty) { continue }

            $id = [string]$idProperty.Value
            if ($id -match '^CR-(\d+)$') {
                $number = [int]$Matches[1]
                if ($number -gt $max) { $max = $number }
            }
        }
    }

    return 'CR-{0:D3}' -f ($max + 1)
}

function Ensure-ArrayProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object.PSObject.Properties[$Name]) {
        $Object | Add-Member -MemberType NoteProperty -Name $Name -Value @()
        return
    }

    if ($null -eq $Object.$Name) {
        $Object.PSObject.Properties[$Name].Value = @()
    }
}

function Add-ObjectToArrayProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$ItemToAppend
    )

    Ensure-ArrayProperty -Object $Object -Name $Name
    $items = New-Object System.Collections.Generic.List[object]
    foreach ($item in @($Object.$Name)) {
        if ($null -ne $item) {
            [void]$items.Add($item)
        }
        else {
            Write-Warning ("Null entry encountered while appending to audit-state array '{0}'; dropping the corrupt entry." -f $Name)
        }
    }

    [void]$items.Add($ItemToAppend)
    $Object.PSObject.Properties[$Name].Value = $items.ToArray()
}

function Ensure-ChangeRequestEntryShape {
    param([Parameter(Mandatory = $true)]$Entry)

    $id = if ($null -ne $Entry.PSObject.Properties['id']) { [string]$Entry.id } else { $null }
    $trackingNumber = if ($null -ne $Entry.PSObject.Properties['tracking_number']) { [string]$Entry.tracking_number } else { $null }
    if ([string]::IsNullOrWhiteSpace($id) -and -not [string]::IsNullOrWhiteSpace($trackingNumber)) {
        $Entry | Add-Member -MemberType NoteProperty -Name id -Value $trackingNumber
    }
    elseif ($null -eq $Entry.PSObject.Properties['id']) {
        $Entry | Add-Member -MemberType NoteProperty -Name id -Value $null
    }

    if ([string]::IsNullOrWhiteSpace($trackingNumber) -and -not [string]::IsNullOrWhiteSpace($id)) {
        $Entry | Add-Member -MemberType NoteProperty -Name tracking_number -Value $id
    }
    elseif ($null -eq $Entry.PSObject.Properties['tracking_number']) {
        $Entry | Add-Member -MemberType NoteProperty -Name tracking_number -Value $null
    }

    foreach ($scalarName in @('priority', 'area', 'title', 'status', 'source', 'severity')) {
        if ($null -eq $Entry.PSObject.Properties[$scalarName]) {
            $Entry | Add-Member -MemberType NoteProperty -Name $scalarName -Value $null
        }

        if ($null -eq $Entry.$scalarName -or ([string]$Entry.$scalarName).Trim().Length -eq 0) {
            $defaultTitle = if (-not [string]::IsNullOrWhiteSpace([string]$Entry.id)) {
                [string]$Entry.id
            }
            elseif (-not [string]::IsNullOrWhiteSpace([string]$Entry.tracking_number)) {
                [string]$Entry.tracking_number
            }
            else {
                'Untitled change request'
            }

            $defaultValue = switch ($scalarName) {
                'priority' { 3 }
                'area' { 'unspecified' }
                'title' { $defaultTitle }
                'status' { 'unknown' }
                'source' { 'schema_migration' }
                'severity' { 'unspecified' }
            }

            $Entry.PSObject.Properties[$scalarName].Value = $defaultValue
        }
    }

    foreach ($arrayName in @('evidence', 'notes', 'acceptance', 'resolution', 'validation')) {
        if ($null -eq $Entry.PSObject.Properties[$arrayName]) {
            $Entry | Add-Member -MemberType NoteProperty -Name $arrayName -Value @()
        }
        elseif ($null -eq $Entry.$arrayName) {
            $Entry.PSObject.Properties[$arrayName].Value = @()
        }
        elseif ($Entry.$arrayName -isnot [System.Array]) {
            $Entry.PSObject.Properties[$arrayName].Value = @($Entry.$arrayName)
        }
    }
}

function Normalize-AuditChangeRequestSchema {
    param([Parameter(Mandatory = $true)]$AuditState)

    $canonicalName = 'change_requests'
    $legacyNames = @('pending_next_build_issues', 'current_priority_backlog')
    $hasCanonical = $null -ne $AuditState.PSObject.Properties[$canonicalName]
    $hasLegacy = $false
    foreach ($legacyName in $legacyNames) {
        if ($null -ne $AuditState.PSObject.Properties[$legacyName]) {
            $hasLegacy = $true
            break
        }
    }

    if (-not $hasCanonical -and -not $hasLegacy) {
        throw 'Audit state file does not contain change_requests.'
    }

    if ($hasCanonical -and -not $hasLegacy) {
        Ensure-ArrayProperty -Object $AuditState -Name $canonicalName
        if ($AuditState.$canonicalName -isnot [System.Array]) {
            throw 'Audit state change_requests property must be a JSON array.'
        }

        return
    }

    $merged = New-Object System.Collections.Generic.List[object]
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($propertyName in @($canonicalName) + $legacyNames) {
        $property = $AuditState.PSObject.Properties[$propertyName]
        if ($null -eq $property) { continue }

        foreach ($item in @($property.Value)) {
            if ($null -eq $item) { continue }
            if ($item -is [string] -or $item -is [ValueType]) {
                throw ("Non-object ticket entry encountered in audit-state property '{0}'. Manual repair is required before adding change requests." -f $propertyName)
            }

            $identity = $null
            foreach ($identityPropertyName in @('id', 'tracking_number')) {
                $identityProperty = $item.PSObject.Properties[$identityPropertyName]
                if ($null -ne $identityProperty -and -not [string]::IsNullOrWhiteSpace([string]$identityProperty.Value)) {
                    $identity = [string]$identityProperty.Value
                    break
                }
            }

            if ([string]::IsNullOrWhiteSpace($identity)) {
                $identity = '__anonymous_{0}' -f $merged.Count
            }

            Ensure-ChangeRequestEntryShape -Entry $item
            if ($seen.Add($identity)) {
                [void]$merged.Add($item)
            }
            else {
                $existing = $merged | Where-Object {
                    $existingId = if ($null -ne $_.PSObject.Properties['id']) { [string]$_.id } else { $null }
                    $existingTracking = if ($null -ne $_.PSObject.Properties['tracking_number']) { [string]$_.tracking_number } else { $null }
                    $existingId -ieq $identity -or $existingTracking -ieq $identity
                } | Select-Object -First 1
                $existingJson = $existing | ConvertTo-Json -Depth 30 -Compress
                $duplicateJson = $item | ConvertTo-Json -Depth 30 -Compress
                if ($existingJson -ne $duplicateJson) {
                    throw ("Conflicting duplicate ticket identity '{0}' encountered while normalizing audit state. Manual repair is required." -f $identity)
                }

                Write-Warning ("Duplicate ticket identity '{0}' encountered while normalizing audit state; identical duplicate skipped." -f $identity)
            }
        }
    }

    Ensure-ArrayProperty -Object $AuditState -Name $canonicalName
    $AuditState.PSObject.Properties[$canonicalName].Value = $merged.ToArray()
    foreach ($legacyName in $legacyNames) {
        if ($null -ne $AuditState.PSObject.Properties[$legacyName]) {
            $AuditState.PSObject.Properties.Remove($legacyName)
        }
    }
}

function Get-AuditChangeRequestTargetProperty {
    param([Parameter(Mandatory = $true)]$AuditState)

    if ($null -ne $AuditState.PSObject.Properties['change_requests']) { return 'change_requests' }

    throw 'Audit state file does not contain change_requests.'
}

if (-not (Test-Path -LiteralPath $AuditPath)) { throw "Audit state file not found: $AuditPath" }
$mutex = New-Object System.Threading.Mutex($false, 'Local\DoNotPanicPortfolioVisualizer.AuditStateWrite')
$lockTaken = $false
$mutexWasAbandoned = $false
try {
    try {
        $lockTaken = $mutex.WaitOne([TimeSpan]::FromSeconds(30))
    }
    catch [System.Threading.AbandonedMutexException] {
        Write-Warning 'Audit-state write mutex was abandoned by a prior process; continuing with the recovered lock.'
        $lockTaken = $true
        $mutexWasAbandoned = $true
    }

    if (-not $lockTaken) { throw 'Timed out waiting for audit-state write lock.' }

    try {
        $audit = Get-Content -Raw -LiteralPath $AuditPath | ConvertFrom-Json
    }
    catch {
        $context = if ($mutexWasAbandoned) { ' after recovering an abandoned audit-state write mutex' } else { '' }
        throw "Audit state JSON could not be parsed$context`: $($_.Exception.Message)"
    }

    if ($mutexWasAbandoned) {
        Write-Warning 'Audit state JSON parsed successfully after abandoned mutex recovery; proceeding with the recovered exclusive lock.'
    }

    Normalize-AuditChangeRequestSchema -AuditState $audit

    $nextId = Get-NextChangeRequestId -AuditState $audit
    $entry = [pscustomobject][ordered]@{
        id = $nextId
        tracking_number = $nextId
        priority = $Priority
        area = $Area
        title = $Title
        status = 'open'
        source = $Source
        severity = $Severity
        evidence = @($Evidence)
        notes = @($Notes)
        acceptance = @(
            'Root cause is identified from traces, screenshots, or code inspection.',
            'A minimal product or harness fix is implemented without destabilizing the current runtime flow.',
            'DeepSeek code review gate reports no blocking findings for code changes.',
            'Local build/test validation passes when the change affects code.',
            'VM validation passes when the change is UI/runtime/harness-visible.',
            'Ticket is marked resolved/closed in docs/BETA6_AUDIT_STATE.json with evidence.'
        )
    }

    $targetProperty = Get-AuditChangeRequestTargetProperty -AuditState $audit
    Add-ObjectToArrayProperty -Object $audit -Name $targetProperty -ItemToAppend $entry

    $tempPath = $AuditPath + ('.{0}.tmp' -f [guid]::NewGuid().ToString('N'))
    try {
        $audit | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $tempPath -Encoding UTF8
        Move-Item -LiteralPath $tempPath -Destination $AuditPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue }
    }

    Write-Output ("CHANGE_REQUEST_ID=" + $nextId)
}
finally {
    if ($lockTaken) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
