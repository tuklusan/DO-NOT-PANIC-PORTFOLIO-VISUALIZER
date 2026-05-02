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

        if (Test-Path $ArchivePath) {
            Remove-Item -LiteralPath $ArchivePath -Force -ErrorAction SilentlyContinue
        }

        Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $ArchivePath -CompressionLevel Optimal -Force
    }
    finally {
        if (Test-Path $stageRoot) {
            Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
