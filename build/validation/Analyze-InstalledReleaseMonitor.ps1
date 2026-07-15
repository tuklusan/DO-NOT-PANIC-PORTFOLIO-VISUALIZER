# ============================================================================
# Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
# Proprietary rights reserved except as expressly licensed herein.
# ============================================================================
param(
    [string]$ArtifactRoot,
    [long]$LowFreeMemoryBytes = 2147483648,
    [int]$HighCpuPercentage = 90,
    [long]$LowDiskFreeBytes = 10737418240,
    [double]$RapidDiskLossGbPerHour = 2.0,
    [double]$RapidPrivateGrowthMbPerHour = 256.0,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-JsonFile {
    param([string]$Path, $Value)
    $json = ConvertTo-Json -InputObject $Value -Depth 12
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
}

function Read-JsonEvidence {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [pscustomobject]@{ State = 'missing'; Values = @(); ParsedValue = $null; Error = 'File does not exist.'; ByteLength = $null }
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.Length -eq 0) {
        return [pscustomobject]@{ State = 'zero-byte'; Values = @(); ParsedValue = $null; Error = 'File is empty.'; ByteLength = 0 }
    }

    try {
        $raw = [System.IO.File]::ReadAllText($item.FullName)
        $parsed = $raw | ConvertFrom-Json
        $values = @($parsed)
        return [pscustomobject]@{
            State = if ($values.Count -eq 0) { 'valid-empty' } else { 'valid-nonempty' }
            Values = $values
            ParsedValue = $parsed
            Error = $null
            ByteLength = $item.Length
        }
    }
    catch {
        return [pscustomobject]@{ State = 'malformed'; Values = @(); ParsedValue = $null; Error = $_.Exception.Message; ByteLength = $item.Length }
    }
}

function Get-JsonValue {
    param([string]$Path, $Default = $null)
    $evidence = Read-JsonEvidence -Path $Path
    if ($evidence.State -notin @('valid-empty', 'valid-nonempty')) { return $Default }
    if ($evidence.State -eq 'valid-empty') { return @() }
    return $evidence.ParsedValue
}

function Get-NumericProperty {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value -or [string]::IsNullOrWhiteSpace([string]$property.Value)) { return $null }
    return [double]$property.Value
}

function Get-DesktopProcess {
    param($Processes)
    $matches = @($Processes | Where-Object ProcessName -eq 'PortfolioSaver.Desktop' | Select-Object -First 1)
    if ($matches.Count -eq 0) { return $null }
    return $matches[0]
}

function Get-ResourceHealth {
    param(
        [string]$Platform,
        $CurrentResource,
        $PreviousResource,
        $CurrentProcesses,
        $PreviousProcesses,
        [double]$ElapsedHours,
        [bool]$HasCorrelatedFailure
    )

    $signals = [System.Collections.Generic.List[string]]::new()
    $missing = [System.Collections.Generic.List[string]]::new()
    $freeMemory = Get-NumericProperty $CurrentResource 'FreePhysicalMemoryBytes'
    $cpu = Get-NumericProperty $CurrentResource 'CpuLoadPercentage'
    $disk = if ($null -eq $CurrentResource) { $null } else { $CurrentResource.PSObject.Properties['SystemDrive'].Value }
    $diskFree = Get-NumericProperty $disk 'FreeSpace'
    foreach ($entry in @(
        [pscustomobject]@{ Name = 'FreePhysicalMemoryBytes'; Value = $freeMemory },
        [pscustomobject]@{ Name = 'CpuLoadPercentage'; Value = $cpu },
        [pscustomobject]@{ Name = 'SystemDrive.FreeSpace'; Value = $diskFree }
    )) {
        if ($null -eq $entry.Value) { $missing.Add($entry.Name) }
    }

    if ($null -ne $freeMemory -and $freeMemory -lt $LowFreeMemoryBytes) { $signals.Add('low-free-memory') }
    if ($null -ne $cpu -and $cpu -ge $HighCpuPercentage) { $signals.Add('high-cpu') }
    if ($null -ne $diskFree -and $diskFree -lt $LowDiskFreeBytes) { $signals.Add('low-disk-free') }

    $previousDisk = if ($null -eq $PreviousResource) { $null } else { $PreviousResource.PSObject.Properties['SystemDrive'].Value }
    $previousDiskFree = Get-NumericProperty $previousDisk 'FreeSpace'
    $trendWindowQualified = $ElapsedHours -ge (5.0 / 60.0)
    $diskDeltaBytes = if ($null -ne $diskFree -and $null -ne $previousDiskFree) { $diskFree - $previousDiskFree } else { $null }
    $diskRateGbPerHour = if ($trendWindowQualified -and $null -ne $diskDeltaBytes) { ($diskDeltaBytes / 1GB) / $ElapsedHours } else { $null }
    if ($null -ne $diskRateGbPerHour -and $diskRateGbPerHour -le -$RapidDiskLossGbPerHour) { $signals.Add('rapid-disk-loss') }

    $desktop = Get-DesktopProcess $CurrentProcesses
    $previousDesktop = Get-DesktopProcess $PreviousProcesses
    $privateDeltaBytes = if ($null -ne $desktop -and $null -ne $previousDesktop) {
        [double]$desktop.PrivateMemorySize64 - [double]$previousDesktop.PrivateMemorySize64
    }
    else { $null }
    $privateRateMbPerHour = if ($trendWindowQualified -and $null -ne $privateDeltaBytes) { ($privateDeltaBytes / 1MB) / $ElapsedHours } else { $null }
    if ($null -ne $privateRateMbPerHour -and $privateRateMbPerHour -ge $RapidPrivateGrowthMbPerHour) { $signals.Add('rapid-product-private-growth') }

    $previousFreeMemory = Get-NumericProperty $PreviousResource 'FreePhysicalMemoryBytes'
    $previousCpu = Get-NumericProperty $PreviousResource 'CpuLoadPercentage'
    $sustained = ($freeMemory -lt $LowFreeMemoryBytes -and $previousFreeMemory -lt $LowFreeMemoryBytes) -or
        ($cpu -ge $HighCpuPercentage -and $previousCpu -ge $HighCpuPercentage)
    $recovered = ($freeMemory -ge $LowFreeMemoryBytes -and $previousFreeMemory -lt $LowFreeMemoryBytes) -or
        ($cpu -lt $HighCpuPercentage -and $previousCpu -ge $HighCpuPercentage)

    $topProcesses = @()
    if ($null -ne $CurrentResource) {
        $topProperty = $CurrentResource.PSObject.Properties['TopProcessesByPrivateBytes']
        if ($null -ne $topProperty) { $topProcesses = @($topProperty.Value) }
    }
    $topConsumer = @($topProcesses | Select-Object -First 1)
    $productIsTopConsumer = $topConsumer.Count -gt 0 -and [string]$topConsumer[0].ProcessName -eq 'PortfolioSaver.Desktop'

    $classification = if ($missing.Count -gt 0) { 'evidence-failure' }
    elseif ($signals.Count -eq 0 -and $recovered) { 'recovered' }
    elseif ($signals.Count -eq 0) { 'healthy' }
    elseif ($HasCorrelatedFailure) { 'product-correlated-anomaly' }
    elseif ($signals -contains 'rapid-product-private-growth' -and $productIsTopConsumer) { 'product-growth-warning' }
    elseif ($Platform -eq 'vm' -and $signals.Count -eq 1 -and $signals[0] -eq 'high-cpu') { 'accepted-constrained-vm-baseline' }
    elseif ($sustained) { 'sustained-environmental-pressure' }
    else { 'isolated-environmental-warning' }

    return [ordered]@{
        platform = $Platform
        classification = $classification
        signals = $signals.ToArray()
        missingSamples = $missing.ToArray()
        correlatedFailure = $HasCorrelatedFailure
        sustained = [bool]$sustained
        recovered = [bool]$recovered
        trendWindowQualified = $trendWindowQualified
        freePhysicalMemoryBytes = $freeMemory
        cpuLoadPercentage = $cpu
        diskFreeBytes = $diskFree
        diskDeltaBytes = $diskDeltaBytes
        diskRateGbPerHour = $diskRateGbPerHour
        productPrivateMemoryBytes = if ($null -eq $desktop) { $null } else { $desktop.PrivateMemorySize64 }
        productPrivateDeltaBytes = $privateDeltaBytes
        productPrivateRateMbPerHour = $privateRateMbPerHour
        productResponding = if ($null -eq $desktop) { $null } else { [bool]$desktop.Responding }
        productIsTopPrivateConsumer = $productIsTopConsumer
        topProcessesByPrivateBytes = $topProcesses
    }
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('dnppv-monitor-analyzer-' + [guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path $root | Out-Null
        $validEmpty = Join-Path $root 'empty.json'
        $zero = Join-Path $root 'zero.json'
        $bad = Join-Path $root 'bad.json'
        [System.IO.File]::WriteAllText($validEmpty, '[]')
        [System.IO.File]::WriteAllText($zero, '')
        [System.IO.File]::WriteAllText($bad, '{')
        if ((Read-JsonEvidence $validEmpty).State -ne 'valid-empty') { throw 'Valid empty event evidence was not recognized.' }
        if ((Read-JsonEvidence $zero).State -ne 'zero-byte') { throw 'Zero-byte event evidence was not rejected.' }
        if ((Read-JsonEvidence $bad).State -ne 'malformed') { throw 'Malformed event evidence was not rejected.' }

        $process = @([pscustomobject]@{ ProcessName = 'PortfolioSaver.Desktop'; PrivateMemorySize64 = 400MB; Responding = $true })
        $previousProcess = @([pscustomobject]@{ ProcessName = 'PortfolioSaver.Desktop'; PrivateMemorySize64 = 390MB; Responding = $true })
        $otherTop = @([pscustomobject]@{ ProcessName = 'browser'; PrivateMemorySize64 = 2GB })
        $low = [pscustomobject]@{ FreePhysicalMemoryBytes = 1GB; CpuLoadPercentage = 20; SystemDrive = [pscustomobject]@{ FreeSpace = 20GB }; TopProcessesByPrivateBytes = $otherTop }
        $healthy = [pscustomobject]@{ FreePhysicalMemoryBytes = 4GB; CpuLoadPercentage = 20; SystemDrive = [pscustomobject]@{ FreeSpace = 20GB }; TopProcessesByPrivateBytes = $otherTop }
        $missing = [pscustomobject]@{ FreePhysicalMemoryBytes = 4GB; CpuLoadPercentage = $null; SystemDrive = [pscustomobject]@{ FreeSpace = 20GB } }
        $crossing = Get-ResourceHealth local $low $healthy $process $previousProcess 0.5 $false
        $recovery = Get-ResourceHealth local $healthy $low $process $previousProcess 0.5 $false
        $sustained = Get-ResourceHealth local $low $low $process $previousProcess 0.5 $false
        $missingResult = Get-ResourceHealth local $missing $healthy $process $previousProcess 0.5 $false
        $noProcessResult = Get-ResourceHealth local $healthy $healthy @() @() 0.5 $false
        $vmCpu = [pscustomobject]@{ FreePhysicalMemoryBytes = 4GB; CpuLoadPercentage = 100; SystemDrive = [pscustomobject]@{ FreeSpace = 20GB }; TopProcessesByPrivateBytes = $otherTop }
        $vmBaseline = Get-ResourceHealth vm $vmCpu $healthy $process $previousProcess 0.5 $false
        $shortWindow = Get-ResourceHealth local $low $healthy $process $previousProcess (1.0 / 3600.0) $false
        if ($crossing.classification -ne 'isolated-environmental-warning') { throw 'Threshold crossing classification failed.' }
        if ($recovery.classification -ne 'recovered') { throw 'Recovery classification failed.' }
        if ($sustained.classification -ne 'sustained-environmental-pressure') { throw 'Sustained pressure classification failed.' }
        if ($missingResult.classification -ne 'evidence-failure') { throw 'Missing sample classification failed.' }
        if ($noProcessResult.productPrivateMemoryBytes -ne $null -or $noProcessResult.productResponding -ne $null) { throw 'Missing product process classification failed.' }
        if ($vmBaseline.classification -ne 'accepted-constrained-vm-baseline') { throw 'Constrained VM baseline classification failed.' }
        if ($shortWindow.trendWindowQualified -or $null -ne $shortWindow.diskRateGbPerHour) { throw 'Short trend windows must not produce rates.' }
        if ($crossing.productIsTopPrivateConsumer) { throw 'Product-versus-host attribution failed.' }
        Write-Output 'Analyze-InstalledReleaseMonitor self-test passed.'
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) { throw 'ArtifactRoot is required unless -SelfTest is used.' }
$artifactRootPath = (Resolve-Path -LiteralPath $ArtifactRoot).Path
$summaryPath = Join-Path $artifactRootPath 'collection-summary.json'
$summary = Get-JsonValue $summaryPath
if ($null -eq $summary) { throw "Missing or invalid collection summary: $summaryPath" }

$previousRoot = [string]$summary.previousArtifactRoot
$previousSummary = if ([string]::IsNullOrWhiteSpace($previousRoot)) { $null } else { Get-JsonValue (Join-Path $previousRoot 'collection-summary.json') }
$capturedAt = [datetimeoffset]$summary.capturedAt
$previousCapturedAt = if ($null -eq $previousSummary) { $null } else { [datetimeoffset]$previousSummary.capturedAt }
$elapsedHours = if ($null -eq $previousCapturedAt) { 0.5 } else { [Math]::Max(1.0 / 3600.0, ($capturedAt - $previousCapturedAt).TotalHours) }

$platforms = @('local', 'vm', 'remote-laptop') | Where-Object { Test-Path -LiteralPath (Join-Path $artifactRootPath $_) }
$aliveProperties = if ($null -eq $summary.alive) { @() } else { @($summary.alive.PSObject.Properties) }
$eventEvidence = [System.Collections.Generic.List[object]]::new()
$resourceHealth = [System.Collections.Generic.List[object]]::new()
$findings = [System.Collections.Generic.List[object]]::new()

foreach ($platform in $platforms) {
    $platformRoot = Join-Path $artifactRootPath $platform
    $eventsPath = Join-Path $platformRoot 'application-events-error.json'
    $events = Read-JsonEvidence $eventsPath
    $eventRecord = [ordered]@{ platform = $platform; state = $events.State; byteLength = $events.ByteLength; eventCount = $events.Values.Count; error = $events.Error }
    $eventEvidence.Add($eventRecord)
    if ($events.State -notin @('valid-empty', 'valid-nonempty')) {
        $findings.Add([ordered]@{ key = 'application-event-evidence-failure'; severity = 'high'; platform = $platform; classification = $events.State; evidence = $eventsPath; summary = 'Application event evidence is missing, empty, malformed, or unreadable.' })
    }
    elseif ($events.State -eq 'valid-nonempty') {
        $findings.Add([ordered]@{ key = 'application-error-event'; severity = 'high'; platform = $platform; classification = 'event-recorded'; evidence = $eventsPath; summary = "$($events.Values.Count) Application/.NET error event(s) were collected." })
    }

    $currentResource = Get-JsonValue (Join-Path $platformRoot 'resource-summary.json')
    $currentProcesses = @(Get-JsonValue (Join-Path $platformRoot 'processes.json') @())
    $previousResource = if ([string]::IsNullOrWhiteSpace($previousRoot)) { $null } else { Get-JsonValue (Join-Path $previousRoot "$platform\resource-summary.json") }
    $previousProcesses = if ([string]::IsNullOrWhiteSpace($previousRoot)) { @() } else { @(Get-JsonValue (Join-Path $previousRoot "$platform\processes.json") @()) }
    $desktop = Get-DesktopProcess $currentProcesses
    $hasCorrelatedFailure = $null -ne $desktop -and ((-not [bool]$desktop.Responding) -or @($aliveProperties | Where-Object { $_.Name -like "$($platform -replace '-laptop','Laptop')*" -and $_.Value -eq $false }).Count -gt 0)
    $health = Get-ResourceHealth $platform $currentResource $previousResource $currentProcesses $previousProcesses $elapsedHours $hasCorrelatedFailure
    $resourceHealth.Add($health)
    if ($health.classification -notin @('healthy', 'recovered', 'accepted-constrained-vm-baseline')) {
        $findings.Add([ordered]@{ key = 'resource-health'; severity = if ($health.classification -in @('evidence-failure', 'product-correlated-anomaly')) { 'high' } else { 'medium' }; platform = $platform; classification = $health.classification; signals = $health.signals; summary = 'Resource threshold, trend, attribution, or evidence condition requires classification.' })
    }
}

$analysis = [ordered]@{
    analyzedAt = (Get-Date).ToUniversalTime().ToString('o')
    artifactRoot = $artifactRootPath
    capturedAt = $capturedAt.ToString('o')
    previousCapturedAt = if ($null -eq $previousCapturedAt) { $null } else { $previousCapturedAt.ToString('o') }
    elapsedHours = $elapsedHours
    alive = $summary.alive
    verdict = if (@($findings | Where-Object severity -eq 'high').Count -gt 0) { 'evidence-or-product-failure' } elseif ($findings.Count -gt 0) { 'resource-warning' } else { 'clean' }
    findings = $findings.ToArray()
    eventEvidence = $eventEvidence.ToArray()
    resourceHealth = $resourceHealth.ToArray()
    notes = @(
        'PREPRE and POSTPOST are legitimate upstream market states and are not anomalies.',
        'MainWindowHandle=0 from an SSH session is instrumentation evidence, not proof that an interactive-session window is absent.',
        'Screenshot collection is excluded from this trace-focused monitor by owner direction.'
    )
}

Write-JsonFile (Join-Path $artifactRootPath 'event-evidence.json') $eventEvidence.ToArray()
Write-JsonFile (Join-Path $artifactRootPath 'resource-health.json') $resourceHealth.ToArray()
$analysisPath = Join-Path $artifactRootPath 'deterministic-analysis.json'
Write-JsonFile $analysisPath $analysis
Write-Output $analysisPath
