param(
    [Parameter(Mandatory = $true)][string]$Title,
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

    foreach ($propertyName in @('pending_next_build_issues', 'change_requests')) {
        $property = $AuditState.PSObject.Properties[$propertyName]
        if ($null -eq $property) { continue }

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
        [Parameter(Mandatory = $true)]$Value
    )

    Ensure-ArrayProperty -Object $Object -Name $Name
    $items = New-Object System.Collections.Generic.List[object]
    foreach ($item in @($Object.$Name)) {
        if ($null -ne $item) {
            [void]$items.Add($item)
        }
    }

    [void]$items.Add($Value)
    $Object.PSObject.Properties[$Name].Value = @($items)
}

function Get-AuditChangeRequestTargetProperty {
    param([Parameter(Mandatory = $true)]$AuditState)

    if ($null -ne $AuditState.PSObject.Properties['pending_next_build_issues']) {
        return 'pending_next_build_issues'
    }

    if ($null -ne $AuditState.PSObject.Properties['change_requests']) {
        return 'change_requests'
    }

    throw 'Audit state file does not contain pending_next_build_issues or change_requests.'
}

if (-not (Test-Path -LiteralPath $AuditPath)) { throw "Audit state file not found: $AuditPath" }
$mutex = New-Object System.Threading.Mutex($false, 'Local\DoNotPanicPortfolioVisualizer.AuditStateWrite')
$lockTaken = $false
try {
    $lockTaken = $mutex.WaitOne([TimeSpan]::FromSeconds(30))
    if (-not $lockTaken) { throw 'Timed out waiting for audit-state write lock.' }

    $audit = Get-Content -Raw -LiteralPath $AuditPath | ConvertFrom-Json

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
    Add-ObjectToArrayProperty -Object $audit -Name $targetProperty -Value $entry

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

