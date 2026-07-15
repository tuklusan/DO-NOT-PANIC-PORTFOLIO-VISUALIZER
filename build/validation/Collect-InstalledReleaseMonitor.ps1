# ============================================================================
# Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
# Proprietary rights reserved except as expressly licensed herein.
# ============================================================================
param(
    [string]$VmHost,
    [string]$RemoteLaptopHost,
    [pscredential]$RemoteLaptopCredential,
    [string]$ArtifactRoot,
    [switch]$SkipVm,
    [switch]$SkipRemoteLaptop,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactBase = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'build\validation\artifacts'))
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$artifactRoot = if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    Join-Path $artifactBase ("installed-monitor-{0}" -f $stamp)
}
else {
    [System.IO.Path]::GetFullPath($ArtifactRoot)
}
if (-not $artifactRoot.StartsWith($artifactBase + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "ArtifactRoot must remain under $artifactBase"
}
$previousBundle = Get-ChildItem -Path (Join-Path $repoRoot 'build\validation\artifacts') -Directory -Filter 'installed-monitor-*' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

$notes = [System.Collections.Generic.List[string]]::new()

function Ensure-Dir {
    param([Parameter(Mandatory = $true)][string]$Path)
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowNull()]$Object
    )

    Ensure-Dir (Split-Path -Parent $Path)
    # -InputObject preserves an empty array as [] instead of enumerating it into
    # an empty pipeline and producing a zero-byte file.
    $json = ConvertTo-Json -InputObject $Object -Depth 20
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
}

if ($SelfTest) {
    $selfTestRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('dnppv-monitor-collector-' + [guid]::NewGuid().ToString('N'))
    try {
        $emptyPath = Join-Path $selfTestRoot 'empty.json'
        $nonEmptyPath = Join-Path $selfTestRoot 'non-empty.json'
        $nullPath = Join-Path $selfTestRoot 'null.json'
        Write-JsonFile -Path $emptyPath -Object @()
        Write-JsonFile -Path $nonEmptyPath -Object @([ordered]@{ Id = 1002; ProviderName = 'Application Hang' })
        Write-JsonFile -Path $nullPath -Object $null
        if ((Get-Content -Raw -LiteralPath $emptyPath).Trim() -ne '[]') {
            throw 'Empty arrays must serialize as valid JSON [].'
        }
        $parsed = @(Get-Content -Raw -LiteralPath $nonEmptyPath | ConvertFrom-Json)
        if ($parsed.Count -ne 1 -or $parsed[0].Id -ne 1002) {
            throw 'Non-empty event arrays did not round-trip.'
        }
        if ((Get-Content -Raw -LiteralPath $nullPath).Trim() -ne 'null') {
            throw 'Null values must serialize as valid JSON null.'
        }
        Write-Output 'Collect-InstalledReleaseMonitor self-test passed.'
        return
    }
    finally {
        Remove-Item -LiteralPath $selfTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

. (Join-Path (Split-Path -Parent $PSScriptRoot) 'vm\VmSshCommon.ps1')

function Convert-ToSftpPath {
    param([Parameter(Mandatory = $true)][string]$WindowsPath)

    $normalized = $WindowsPath -replace '\\', '/'
    if ($normalized -match '^(?<drive>[A-Za-z]):(?<rest>/.*)$') {
        return '/{0}:{1}' -f $Matches.drive.ToUpper(), $Matches.rest
    }

    throw "Unsupported Windows path for SFTP conversion: $WindowsPath"
}

function Get-ProcessSnapshot {
    param([Parameter(Mandatory = $true)][string[]]$Names)

    return @(
        Get-Process -Name $Names -ErrorAction SilentlyContinue |
            Sort-Object ProcessName |
            ForEach-Object {
                [ordered]@{
                    ProcessName = $_.ProcessName
                    Id = $_.Id
                    SessionId = $_.SessionId
                    MainWindowHandle = [int64]$_.MainWindowHandle
                    MainWindowTitle = $_.MainWindowTitle
                    StartTime = $_.StartTime
                    Responding = $_.Responding
                    CPU = $_.CPU
                    WorkingSet64 = $_.WorkingSet64
                    PrivateMemorySize64 = $_.PrivateMemorySize64
                }
            }
    )
}

function Get-RecentApplicationErrors {
    param([Parameter(Mandatory = $true)][datetime]$StartTime)

    try {
        return @(
            Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $StartTime; Level = 2 } -ErrorAction Stop |
                Where-Object {
                    $_.ProviderName -in @('.NET Runtime', 'Application Error', 'Windows Error Reporting') -or
                    $_.Message -match 'PortfolioSaver|YFinance\.NET\.Server|DoNotPanicPortfolioVisualizer'
                } |
                Select-Object -First 50 TimeCreated, Id, ProviderName, LevelDisplayName, MachineName, Message
        )
    }
    catch {
        if ($_.Exception.Message -match 'No events were found that match the specified selection criteria') {
            return @()
        }
        $script:notes.Add("Local Application log query failed: $($_.Exception.Message)")
        return @()
    }
}

function Get-ResourceSummary {
    try {
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        $cpu = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1 LoadPercentage, Name
        $disk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'" -ErrorAction SilentlyContinue | Select-Object -First 1 DeviceID, FreeSpace, Size
        return [ordered]@{
            ComputerName = $env:COMPUTERNAME
            UserName = $env:USERNAME
            CapturedAt = (Get-Date).ToString('o')
            TotalVisibleMemoryBytes = if ($os.TotalVisibleMemorySize) { [int64]$os.TotalVisibleMemorySize * 1KB } else { $null }
            FreePhysicalMemoryBytes = if ($os.FreePhysicalMemory) { [int64]$os.FreePhysicalMemory * 1KB } else { $null }
            LastBootUpTime = $os.LastBootUpTime
            CpuLoadPercentage = $cpu.LoadPercentage
            CpuName = $cpu.Name
            SystemDrive = $disk
            TopProcessesByPrivateBytes = @(
                Get-Process -ErrorAction SilentlyContinue |
                    Sort-Object PrivateMemorySize64 -Descending |
                    Select-Object -First 10 ProcessName, Id, PrivateMemorySize64, WorkingSet64, CPU
            )
        }
    }
    catch {
        $script:notes.Add("Local resource snapshot failed: $($_.Exception.Message)")
        return [ordered]@{
            ComputerName = $env:COMPUTERNAME
            UserName = $env:USERNAME
            CapturedAt = (Get-Date).ToString('o')
            Error = $_.Exception.Message
        }
    }
}

function Copy-TraceFilesLocal {
    param(
        [Parameter(Mandatory = $true)][string]$SourceTraceRoot,
        [Parameter(Mandatory = $true)][string]$DestinationTraceRoot
    )

    Ensure-Dir $DestinationTraceRoot
    foreach ($fileName in @('trace.circular.log', 'trace.circular.idx', 'yfinance.circular.log', 'yfinance.circular.idx')) {
        $source = Join-Path $SourceTraceRoot $fileName
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $DestinationTraceRoot $fileName) -Force
        }
        else {
            $script:notes.Add("Missing local trace file: $source")
        }
    }
}

function Invoke-RemotePwshJson {
    param(
        [Parameter(Mandatory = $true)]$SshSession,
        [Parameter(Mandatory = $true)][string]$Command,
        [string]$ShellExecutable = 'pwsh',
        [int]$TimeoutSeconds = 120
    )

    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command))
    $remoteCommand = "$ShellExecutable -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encoded"
    $result = Invoke-SSHCommand -SSHSession $SshSession -Command $remoteCommand -TimeOut $TimeoutSeconds -ErrorAction Stop
    if ($result.ExitStatus -ne 0) {
        $joined = ($result.Output + $result.Error) -join [Environment]::NewLine
        throw "Remote command failed with exit code $($result.ExitStatus): $joined"
    }

    $text = ($result.Output -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    return ($text | ConvertFrom-Json -Depth 10)
}

function Copy-TraceFilesRemote {
    param(
        [Parameter(Mandatory = $true)]$SftpSession,
        [Parameter(Mandatory = $true)][string]$RemoteTraceRoot,
        [Parameter(Mandatory = $true)][string]$DestinationTraceRoot,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Ensure-Dir $DestinationTraceRoot
    $sftpTraceRoot = Convert-ToSftpPath $RemoteTraceRoot
    foreach ($fileName in @('trace.circular.log', 'trace.circular.idx', 'yfinance.circular.log', 'yfinance.circular.idx')) {
        $remoteFile = "$sftpTraceRoot/$fileName"
        if (Test-SFTPPath -SFTPSession $SftpSession -Path $remoteFile) {
            Get-SFTPItem -SFTPSession $SftpSession -Path $remoteFile -Destination $DestinationTraceRoot -Force | Out-Null
        }
        else {
            $script:notes.Add("Missing $Label trace file: $remoteFile")
        }
    }
}

$cutoff = (Get-Date).AddHours(-1)
$processNames = @('PortfolioSaver.Desktop', 'YFinance.NET.Server')
Ensure-Dir $artifactRoot

$remotePayload = @'
$cutoff = (Get-Date).AddHours(-1)
$procNames = @('PortfolioSaver.Desktop', 'YFinance.NET.Server')
$procs = @(
    Get-Process -Name $procNames -ErrorAction SilentlyContinue |
        Sort-Object ProcessName |
        ForEach-Object {
            [ordered]@{
                ProcessName = $_.ProcessName
                Id = $_.Id
                SessionId = $_.SessionId
                MainWindowHandle = [int64]$_.MainWindowHandle
                MainWindowTitle = $_.MainWindowTitle
                StartTime = $_.StartTime
                Responding = $_.Responding
                CPU = $_.CPU
                WorkingSet64 = $_.WorkingSet64
                PrivateMemorySize64 = $_.PrivateMemorySize64
            }
        }
)
$events = @()
try {
    $events = @(
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $cutoff; Level = 2 } -ErrorAction Stop |
            Where-Object {
                $_.ProviderName -in @('.NET Runtime', 'Application Error', 'Windows Error Reporting') -or
                $_.Message -match 'PortfolioSaver|YFinance\.NET\.Server|DoNotPanicPortfolioVisualizer'
            } |
            Select-Object -First 50 TimeCreated, Id, ProviderName, LevelDisplayName, MachineName, Message
    )
}
catch {
    if ($_.Exception.Message -match 'No events were found that match the specified selection criteria') {
        $events = @()
    }
    else {
        $events = @([ordered]@{ QueryError = $_.Exception.Message })
    }
}
try {
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    $cpu = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1 LoadPercentage, Name
    $disk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'" -ErrorAction SilentlyContinue | Select-Object -First 1 DeviceID, FreeSpace, Size
    $resourceSummary = [ordered]@{
        ComputerName = $env:COMPUTERNAME
        UserName = $env:USERNAME
        CapturedAt = (Get-Date).ToString('o')
        TotalVisibleMemoryBytes = if ($os.TotalVisibleMemorySize) { [int64]$os.TotalVisibleMemorySize * 1KB } else { $null }
        FreePhysicalMemoryBytes = if ($os.FreePhysicalMemory) { [int64]$os.FreePhysicalMemory * 1KB } else { $null }
        LastBootUpTime = $os.LastBootUpTime
        CpuLoadPercentage = $cpu.LoadPercentage
        CpuName = $cpu.Name
        SystemDrive = $disk
        TopProcessesByPrivateBytes = @(
            Get-Process -ErrorAction SilentlyContinue |
                Sort-Object PrivateMemorySize64 -Descending |
                Select-Object -First 10 ProcessName, Id, PrivateMemorySize64, WorkingSet64, CPU
        )
    }
}
catch {
    $resourceSummary = [ordered]@{
        ComputerName = $env:COMPUTERNAME
        UserName = $env:USERNAME
        CapturedAt = (Get-Date).ToString('o')
        Error = $_.Exception.Message
    }
}
[ordered]@{
    computerName = $env:COMPUTERNAME
    userName = $env:USERNAME
    capturedAt = (Get-Date).ToString('o')
    traceRoot = (Join-Path $env:LOCALAPPDATA 'DoNotPanicPortfolioVisualizer\Trace')
    processes = $procs
    applicationErrors = $events
    resourceSummary = $resourceSummary
} | ConvertTo-Json -Depth 10 -Compress
'@

$localRoot = Join-Path $artifactRoot 'local'
$localTraceRoot = Join-Path $env:LOCALAPPDATA 'DoNotPanicPortfolioVisualizer\Trace'
$localProcesses = @(Get-ProcessSnapshot -Names $processNames)
$localEvents = @(Get-RecentApplicationErrors -StartTime $cutoff)
$localResources = Get-ResourceSummary
Write-JsonFile -Path (Join-Path $localRoot 'processes.json') -Object @($localProcesses)
Write-JsonFile -Path (Join-Path $localRoot 'application-events-error.json') -Object @($localEvents)
Write-JsonFile -Path (Join-Path $localRoot 'resource-summary.json') -Object $localResources
Copy-TraceFilesLocal -SourceTraceRoot $localTraceRoot -DestinationTraceRoot (Join-Path $localRoot 'Trace')

$vmData = $null
if (-not $SkipVm) {
    if ([string]::IsNullOrWhiteSpace($VmHost)) {
        throw 'VmHost is required unless -SkipVm is used.'
    }
    $vmBundle = New-VmSshSessionBundle -HostName $VmHost
    try {
        $vmData = Invoke-RemotePwshJson -SshSession $vmBundle.SshSession -Command $remotePayload
        $notes.Add("VM remote command completed: target=$VmHost session_id=$($vmBundle.SshSession.SessionId) exit_status=0")
        if ($null -eq $vmData) {
            $notes.Add('VM remote command returned no JSON data.')
        }
        else {
            $vmRoot = Join-Path $artifactRoot 'vm'
            Write-JsonFile -Path (Join-Path $vmRoot 'processes.json') -Object @($vmData.processes)
            Write-JsonFile -Path (Join-Path $vmRoot 'application-events-error.json') -Object @($vmData.applicationErrors)
            Write-JsonFile -Path (Join-Path $vmRoot 'resource-summary.json') -Object $vmData.resourceSummary
            Copy-TraceFilesRemote -SftpSession $vmBundle.SftpSession -RemoteTraceRoot $vmData.traceRoot -DestinationTraceRoot (Join-Path $vmRoot 'Trace') -Label 'VM'
        }
    }
    finally {
        Remove-VmSshSessionBundle -Bundle $vmBundle
    }
}

$remoteData = $null
if (-not $SkipRemoteLaptop) {
    if ([string]::IsNullOrWhiteSpace($RemoteLaptopHost)) {
        throw 'RemoteLaptopHost is required unless -SkipRemoteLaptop is used.'
    }
    if ($null -eq $RemoteLaptopCredential) {
        throw 'RemoteLaptopCredential is required unless -SkipRemoteLaptop is used.'
    }
    Ensure-PoshSshModule
    # Validation targets live on a trusted private lab network. -AcceptKey
    # matches the existing VM harness policy for first-contact lab machines.
    $remoteSsh = New-SSHSession -ComputerName $RemoteLaptopHost -Credential $RemoteLaptopCredential -AcceptKey -ConnectionTimeout 20 -ErrorAction Stop
    $remoteSftp = $null
    try {
        $remoteSftp = New-SFTPSession -ComputerName $RemoteLaptopHost -Credential $RemoteLaptopCredential -AcceptKey -ConnectionTimeout 20 -ErrorAction Stop
        $remoteData = Invoke-RemotePwshJson -SshSession $remoteSsh -Command $remotePayload -ShellExecutable 'powershell'
        $notes.Add("Remote-laptop command completed: target=$RemoteLaptopHost session_id=$($remoteSsh.SessionId) exit_status=0")
        if ($null -eq $remoteData) {
            $notes.Add('Remote-laptop command returned no JSON data.')
        }
        else {
            $remoteRoot = Join-Path $artifactRoot 'remote-laptop'
            Write-JsonFile -Path (Join-Path $remoteRoot 'processes.json') -Object @($remoteData.processes)
            Write-JsonFile -Path (Join-Path $remoteRoot 'application-events-error.json') -Object @($remoteData.applicationErrors)
            Write-JsonFile -Path (Join-Path $remoteRoot 'resource-summary.json') -Object $remoteData.resourceSummary
            Copy-TraceFilesRemote -SftpSession $remoteSftp -RemoteTraceRoot $remoteData.traceRoot -DestinationTraceRoot (Join-Path $remoteRoot 'Trace') -Label 'remote-laptop'
        }
    }
    finally {
        if ($remoteSftp) {
            Remove-SFTPSession -SFTPSession $remoteSftp -ErrorAction SilentlyContinue | Out-Null
        }
        if ($remoteSsh) {
            Remove-SSHSession -SSHSession $remoteSsh -ErrorAction SilentlyContinue | Out-Null
        }
    }
}

$alive = [ordered]@{
    localDesktop = @($localProcesses | Where-Object ProcessName -eq 'PortfolioSaver.Desktop').Count -gt 0
    localServer = @($localProcesses | Where-Object ProcessName -eq 'YFinance.NET.Server').Count -gt 0
    vmDesktop = if ($SkipVm) { $null } elseif ($null -eq $vmData) { $false } else { @($vmData.processes | Where-Object ProcessName -eq 'PortfolioSaver.Desktop').Count -gt 0 }
    vmServer = if ($SkipVm) { $null } elseif ($null -eq $vmData) { $false } else { @($vmData.processes | Where-Object ProcessName -eq 'YFinance.NET.Server').Count -gt 0 }
    remoteLaptopDesktop = if ($SkipRemoteLaptop) { $null } elseif ($null -eq $remoteData) { $false } else { @($remoteData.processes | Where-Object ProcessName -eq 'PortfolioSaver.Desktop').Count -gt 0 }
    remoteLaptopServer = if ($SkipRemoteLaptop) { $null } elseif ($null -eq $remoteData) { $false } else { @($remoteData.processes | Where-Object ProcessName -eq 'YFinance.NET.Server').Count -gt 0 }
}

$summary = [ordered]@{
    capturedAt = (Get-Date).ToUniversalTime().ToString('o')
    artifactRoot = $artifactRoot
    previousArtifactRoot = if ($null -eq $previousBundle) { $null } else { $previousBundle.FullName }
    alive = $alive
    collectionNotes = @($notes)
}

Write-JsonFile -Path (Join-Path $artifactRoot 'collection-summary.json') -Object $summary
Write-Output $artifactRoot
