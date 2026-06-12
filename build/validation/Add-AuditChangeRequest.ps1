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
    foreach ($request in @($AuditState.change_requests)) {
        $id = [string]$request.id
        if ($id -match '^CR-(\d+)$') {
            $number = [int]$Matches[1]
            if ($number -gt $max) { $max = $number }
        }
    }
    return 'CR-{0:D3}' -f ($max + 1)
}

if (-not (Test-Path -LiteralPath $AuditPath)) { throw "Audit state file not found: $AuditPath" }
$mutex = New-Object System.Threading.Mutex($false, 'Local\DoNotPanicPortfolioVisualizer.AuditStateWrite')
$lockTaken = $false
try {
    $lockTaken = $mutex.WaitOne([TimeSpan]::FromSeconds(30))
    if (-not $lockTaken) { throw 'Timed out waiting for audit-state write lock.' }

    $audit = Get-Content -Raw -LiteralPath $AuditPath | ConvertFrom-Json
    if ($null -eq $audit.PSObject.Properties['change_requests']) { throw "Audit state file does not contain change_requests: $AuditPath" }

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

    $requests = New-Object System.Collections.Generic.List[object]
    foreach ($request in @($audit.change_requests)) { [void]$requests.Add($request) }
    [void]$requests.Add($entry)
    $audit.change_requests = @($requests)

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

