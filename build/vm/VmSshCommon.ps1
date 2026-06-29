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
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-VmSshStep {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host ("[{0}] {1}" -f (Get-Date).ToString('HH:mm:ss'), $Message)
}

function Ensure-PoshSshModule {
    if (-not (Get-Module -ListAvailable -Name Posh-SSH)) {
        Write-VmSshStep "Installing Posh-SSH for the current user"
        Install-Module -Name Posh-SSH -Scope CurrentUser -Force -AllowClobber -ErrorAction Stop
    }

    Import-Module Posh-SSH -ErrorAction Stop
}

function Get-VmSshCredentialPartsFromEnv {
    param([string]$EnvironmentVariable = 'PORTFOLIOSAVER_TESTVM_WIN10_SSH_CREDS')

    $raw = [Environment]::GetEnvironmentVariable($EnvironmentVariable, 'Process')
    if ([string]::IsNullOrWhiteSpace($raw)) {
        $raw = [Environment]::GetEnvironmentVariable($EnvironmentVariable, 'User')
    }
    if ([string]::IsNullOrWhiteSpace($raw)) {
        $raw = [Environment]::GetEnvironmentVariable($EnvironmentVariable, 'Machine')
    }
    if ([string]::IsNullOrWhiteSpace($raw)) {
        throw "Missing environment variable '$EnvironmentVariable'."
    }

    $userMatch = [regex]::Match($raw, 'username=(?<value>\S+)', 'IgnoreCase')
    $passwordMatch = [regex]::Match($raw, 'password=(?<value>.+)$', 'IgnoreCase')
    if (-not $userMatch.Success -or -not $passwordMatch.Success) {
        throw "Environment variable '$EnvironmentVariable' is not in the expected format 'username=... password=...'."
    }

    $user = $userMatch.Groups['value'].Value.Trim()
    $password = $passwordMatch.Groups['value'].Value.Trim()
    if ([string]::IsNullOrWhiteSpace($user) -or [string]::IsNullOrWhiteSpace($password)) {
        throw "Environment variable '$EnvironmentVariable' does not contain both a username and password."
    }

    return [pscustomobject]@{
        UserName = $user
        Password = $password
    }
}

function Get-VmSshCredentialFromEnv {
    param([string]$EnvironmentVariable = 'PORTFOLIOSAVER_TESTVM_WIN10_SSH_CREDS')

    $parts = Get-VmSshCredentialPartsFromEnv -EnvironmentVariable $EnvironmentVariable

    $securePassword = ConvertTo-SecureString -String $parts.Password -AsPlainText -Force
    return [pscredential]::new($parts.UserName, $securePassword)
}

function New-VmSshSessionBundle {
    param(
        [string]$HostName = '192.168.56.102',
        [int]$Port = 22,
        [pscredential]$Credential
    )

    Ensure-PoshSshModule
    if ($null -eq $Credential) {
        $Credential = Get-VmSshCredentialFromEnv
    }

    $ssh = New-SSHSession -ComputerName $HostName -Port $Port -Credential $Credential -AcceptKey -ConnectionTimeout 20 -ErrorAction Stop
    try {
        $sftp = New-SFTPSession -ComputerName $HostName -Port $Port -Credential $Credential -AcceptKey -ConnectionTimeout 20 -ErrorAction Stop
    }
    catch {
        Remove-SSHSession -SessionId $ssh.SessionId | Out-Null
        throw
    }

    return [pscustomobject]@{
        HostName = $HostName
        Port = $Port
        Credential = $Credential
        SshSession = $ssh
        SftpSession = $sftp
    }
}

function Remove-VmSshSessionBundle {
    param([Parameter(Mandatory = $true)]$Bundle)

    if ($null -ne $Bundle.SftpSession) {
        Remove-SFTPSession -SFTPSession $Bundle.SftpSession -ErrorAction SilentlyContinue | Out-Null
    }
    if ($null -ne $Bundle.SshSession) {
        Remove-SSHSession -SSHSession $Bundle.SshSession -ErrorAction SilentlyContinue | Out-Null
    }
}

function ConvertTo-VmPwshEncodedCommand {
    param([Parameter(Mandatory = $true)][string]$Command)

    return [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command))
}

function Test-IsIgnorableVmPwshFailure {
    param([Parameter(Mandatory = $true)]$Result)

    if ($Result.ExitStatus -eq 0) {
        return $false
    }

    $errorText = ($Result.Error) -join [Environment]::NewLine
    if ($errorText -notmatch 'InitializeDefaultDrives operation on the .+FileSystem.+ provider failed') {
        return $false
    }

    $normalized = $errorText `
        -replace '#< CLIXML', '' `
        -replace '_x[0-9A-Fa-f]{4}_', ' ' `
        -replace '<[^>]+>', ' ' `
        -replace '\x1B\[[0-9;]*m', ' '
    $normalized = [regex]::Replace($normalized, '\s+', ' ').Trim()
    $normalized = [regex]::Replace(
        $normalized,
        'Attempting to perform the InitializeDefaultDrives operation on the .+?FileSystem.+? provider failed\.?',
        '',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $normalized = [regex]::Replace($normalized, '\s+', ' ').Trim()

    return [string]::IsNullOrWhiteSpace($normalized)
}

function Invoke-VmPwshCommand {
    param(
        [Parameter(Mandatory = $true)]$Bundle,
        [Parameter(Mandatory = $true)][string]$Command,
        [int]$TimeOutSeconds = 600
    )

    $encoded = ConvertTo-VmPwshEncodedCommand -Command $Command
    $remoteCommand = "pwsh -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encoded"
    $result = Invoke-SSHCommand -SSHSession $Bundle.SshSession -Command $remoteCommand -TimeOut $TimeOutSeconds -ErrorAction Stop
    if ($result.ExitStatus -ne 0) {
        if (Test-IsIgnorableVmPwshFailure -Result $result) {
            return $result
        }

        $joined = ($result.Output + $result.Error) -join [Environment]::NewLine
        throw "Remote command failed with exit code $($result.ExitStatus): $joined"
    }

    return $result
}

function Invoke-VmRawCommand {
    param(
        [Parameter(Mandatory = $true)]$Bundle,
        [Parameter(Mandatory = $true)][string]$Command,
        [int]$TimeOutSeconds = 600,
        [int[]]$AllowedExitCodes = @(0),
        [string]$SuccessOutputPattern
    )

    $result = Invoke-SSHCommand -SSHSession $Bundle.SshSession -Command $Command -TimeOut $TimeOutSeconds -ErrorAction Stop
    $joined = ($result.Output + $result.Error) -join [Environment]::NewLine
    if ($AllowedExitCodes -notcontains $result.ExitStatus) {
        if (-not [string]::IsNullOrWhiteSpace($SuccessOutputPattern) -and $joined -match $SuccessOutputPattern) {
            return $result
        }

        throw "Remote command failed with exit code $($result.ExitStatus): $joined"
    }

    return $result
}

function Invoke-VmHarnessAbortCleanup {
    param(
        [Parameter(Mandatory = $true)]$Bundle,
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$Reason,
        [string]$ResultName
    )

    if ([string]::IsNullOrWhiteSpace($RootPath) -or $RootPath.Length -lt 8 -or $RootPath.TrimEnd('\') -notmatch '\\') {
        Write-Warning "Skipping remote harness abort cleanup because RootPath is not specific enough: '$RootPath'"
        return
    }

    $escapedRoot = $RootPath.Replace("'", "''")
    $escapedReason = $Reason.Replace("'", "''")
    $escapedResultName = if ([string]::IsNullOrWhiteSpace($ResultName)) { '' } else { $ResultName.Replace("'", "''") }
    $command = @"
`$root = '$escapedRoot'
`$reason = '$escapedReason'
`$resultName = '$escapedResultName'
`$cleanupFailures = [System.Collections.Generic.List[string]]::new()
`$processNames = @(
    'PortfolioSaver.VmAgent',
    'PortfolioSaver.Config',
    'PortfolioSaver.Desktop',
    'PortfolioSaver.Screensaver',
    'WinAppDriver'
)
Get-Process -Name `$processNames -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        `$_.CommandLine -and
        `$_.CommandLine.Contains(`$root) -and
        `$_.ProcessId -ne `$PID
    } |
    ForEach-Object {
        `$process = `$_
        try {
            Stop-Process -Id `$process.ProcessId -Force -ErrorAction Stop
        }
        catch {
            `$failure = "Failed to stop PID `$(`$process.ProcessId): `$(`$_.Exception.Message)"
            `$cleanupFailures.Add(`$failure)
            Write-Warning `$failure
        }
    }

`$abortRoot = Join-Path `$root 'results'
New-Item -ItemType Directory -Force -Path `$abortRoot | Out-Null
`$abortMarker = Join-Path `$abortRoot 'harness-aborted.json'
`$marker = [ordered]@{
    Result = 'Aborted'
    Reason = `$reason
    ResultName = `$resultName
    WrittenAt = (Get-Date).ToString('o')
    CleanupFailures = @(`$cleanupFailures)
}
`$marker | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath `$abortMarker -Encoding UTF8
"@

    try {
        Invoke-VmPwshCommand -Bundle $Bundle -Command $command -TimeOutSeconds 120 | Out-Null
    }
    catch {
        Write-Warning ("Remote harness abort cleanup failed: {0}" -f $_.Exception.Message)
    }
}

function Ensure-VmDirectory {
    param(
        [Parameter(Mandatory = $true)]$Bundle,
        [Parameter(Mandatory = $true)][string]$RemotePath
    )

    $command = @"
New-Item -ItemType Directory -Force -Path '$RemotePath' | Out-Null
"@
    Invoke-VmPwshCommand -Bundle $Bundle -Command $command -TimeOutSeconds 60 | Out-Null
}

function ConvertTo-SftpRemotePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $normalized = $Path -replace '\\', '/'
    if ($normalized -match '^[A-Za-z]:/') {
        return '/' + $normalized
    }

    return $normalized
}

function Send-VmItem {
    param(
        [Parameter(Mandatory = $true)]$Bundle,
        [Parameter(Mandatory = $true)][string]$LocalPath,
        [Parameter(Mandatory = $true)][string]$RemoteDestination
    )

    $destination = ConvertTo-SftpRemotePath -Path $RemoteDestination
    Set-SFTPItem -SFTPSession $Bundle.SftpSession -Path $LocalPath -Destination $destination -Force -ErrorAction Stop
}

function Receive-VmItem {
    param(
        [Parameter(Mandatory = $true)]$Bundle,
        [Parameter(Mandatory = $true)][string]$RemotePath,
        [Parameter(Mandatory = $true)][string]$LocalDestination
    )

    $path = ConvertTo-SftpRemotePath -Path $RemotePath
    Get-SFTPItem -SFTPSession $Bundle.SftpSession -Path $path -Destination $LocalDestination -Force -ErrorAction Stop
}

function Get-VmSystemDriveSpaceSnapshot {
    param([Parameter(Mandatory = $true)]$Bundle)

    $command = @"
`$drive = Get-CimInstance -ClassName Win32_LogicalDisk -Filter "DeviceID='C:'"
[pscustomobject]@{
    Drive = `$drive.DeviceID
    SizeBytes = [int64]`$drive.Size
    FreeBytes = [int64]`$drive.FreeSpace
    UsedBytes = [int64](`$drive.Size - `$drive.FreeSpace)
    FreeGB = [math]::Round(([double]`$drive.FreeSpace / 1GB), 2)
    UsedGB = [math]::Round(([double](`$drive.Size - `$drive.FreeSpace) / 1GB), 2)
} | ConvertTo-Json -Compress
"@

    $result = Invoke-VmPwshCommand -Bundle $Bundle -Command $command -TimeOutSeconds 120
    $json = ($result.Output -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($json)) {
        throw 'Unable to read guest drive-space snapshot.'
    }

    return $json | ConvertFrom-Json
}

function Invoke-VmWorkspaceCleanup {
    param(
        [Parameter(Mandatory = $true)]$Bundle,
        [Parameter(Mandatory = $true)][string]$RootPath
    )

    $command = @"
Get-Process PortfolioSaver.VmAgent,PortfolioSaver.Config,PortfolioSaver.Desktop,PortfolioSaver.Screensaver,WinAppDriver -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

`$targets = @(
    (Join-Path '$RootPath' 'artifacts'),
    (Join-Path '$RootPath' 'logs'),
    (Join-Path '$RootPath' 'results'),
    (Join-Path '$RootPath' 'publish'),
    (Join-Path '$RootPath' 'repo\build\artifacts'),
    (Join-Path '$RootPath' 'repo\build\vm\artifacts'),
    (Join-Path '$RootPath' 'repo\TestResults')
)

foreach (`$target in `$targets) {
    if (Test-Path `$target) {
        Get-ChildItem -LiteralPath `$target -Force -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
}

`$repoRoot = Join-Path '$RootPath' 'repo'
if (Test-Path `$repoRoot) {
    Get-ChildItem -LiteralPath `$repoRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { `$_.Name -in @('bin', 'obj') } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

`$traceRoot = Join-Path `$env:LOCALAPPDATA 'PortfolioSaver\Trace'
if (Test-Path `$traceRoot) {
    Remove-Item -LiteralPath `$traceRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Get-ChildItem -LiteralPath `$env:TEMP -Directory -Force -ErrorAction SilentlyContinue |
    Where-Object { `$_.Name -like 'PortfolioSaverVm*' -or `$_.Name -like 'PortfolioSaver*' } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
"@

    Invoke-VmPwshCommand -Bundle $Bundle -Command $command -TimeOutSeconds 1800 | Out-Null
}

function Ensure-VmFreeSpace {
    param(
        [Parameter(Mandatory = $true)]$Bundle,
        [Parameter(Mandatory = $true)][string]$RootPath,
        [double]$MinimumFreeGb = 8
    )

    $before = Get-VmSystemDriveSpaceSnapshot -Bundle $Bundle
    if ([double]$before.FreeGB -ge $MinimumFreeGb) {
        return $before
    }

    Write-VmSshStep ("Guest free space is low ({0} GB free). Purging stale workspace/test artifacts." -f $before.FreeGB)
    Invoke-VmWorkspaceCleanup -Bundle $Bundle -RootPath $RootPath
    $after = Get-VmSystemDriveSpaceSnapshot -Bundle $Bundle
    if ([double]$after.FreeGB -lt $MinimumFreeGb) {
        throw ("Guest free space remains below {0} GB after cleanup (free {1} GB)." -f $MinimumFreeGb, $after.FreeGB)
    }

    Write-VmSshStep ("Guest free space recovered to {0} GB free." -f $after.FreeGB)
    return $after
}

function New-VmWorkspaceArchive {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ArchivePath
    )

    $stageRoot = Join-Path $env:TEMP ('PortfolioSaverVmStage-' + [guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
        $null = robocopy $RepoRoot $stageRoot /MIR /XD .git .vs bin obj build\artifacts /XF *.user *.suo
        $exitCode = $LASTEXITCODE
        if ($exitCode -gt 7) {
            throw "robocopy failed while staging the VM archive (exit code $exitCode)."
        }

        $staleStageTargets = @(
            (Join-Path $stageRoot 'build\artifacts'),
            (Join-Path $stageRoot 'build\vm\artifacts'),
            (Join-Path $stageRoot 'TestResults')
        )
        foreach ($target in $staleStageTargets) {
            if (Test-Path $target) {
                Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        Get-ChildItem -LiteralPath $stageRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -in @('bin', 'obj') } |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

        if (Test-Path $ArchivePath) {
            Remove-Item -LiteralPath $ArchivePath -Force -ErrorAction SilentlyContinue
        }

        $archiveDirectory = Split-Path -Path $ArchivePath -Parent
        $archiveName = Split-Path -Path $ArchivePath -Leaf
        if (-not [string]::IsNullOrWhiteSpace($archiveDirectory)) {
            New-Item -ItemType Directory -Force -Path $archiveDirectory | Out-Null
        }

        $arguments = @(
            '-cf',
            $archiveName,
            '-C',
            $stageRoot,
            '.'
        )

        Push-Location $archiveDirectory
        try {
            & tar @arguments | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "tar failed while building the VM archive (exit code $LASTEXITCODE)."
            }
        }
        finally {
            Pop-Location
        }

        if (-not (Test-Path $ArchivePath)) {
            throw "tar did not produce the expected VM archive: $ArchivePath"
        }
    }
    finally {
        if (Test-Path $stageRoot) {
            Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
