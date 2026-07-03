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
    [string]$VmHost = '192.168.56.102',
    [int]$VmPort = 22,
    [string]$RootPath = 'C:\vmharness\portfolio-saver',
    [switch]$Bootstrap,
    [switch]$PushWorkspace,
    [switch]$RunUxDeep,
    [ValidateSet('Apply', 'Cancel')]
    [string]$ValidationCompletionMode = 'Apply',
    [ValidateRange(1, 10080)]
    [int]$GuestScreensaverDurationMinutes = 20,
    [ValidateRange(1, 3600)]
    [int]$CaptureIntervalSeconds = 5,
    [int]$DisplayWidth,
    [int]$DisplayHeight,
    [string]$DisplayProfile,
    [ValidateSet('none', 'offline-at-start', 'offline-during-config-validation', 'offline-during-runtime', 'offline-then-recover-runtime', 'high-latency-yfinance', 'upstream-throttled', 'timeout')]
    [string]$FaultProfile = 'none',
    [int]$BuildTimeoutSeconds = 3600,
    [int]$UxTimeoutSeconds = 2400
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'VmSshCommon.ps1')

# Machine-readable contract: when UX results are pulled successfully, this script
# emits exactly one LOCAL_RESULT_DIR=<path> line on stdout for parent automation.
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostArtifactsRoot = Join-Path $repoRoot 'build\vm\artifacts\ssh-runs'
$bundle = $null
$localAgentCommandPath = $null
$uxResultName = 'ux-deep-ssh-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$runCompleted = $false
$runFailureReason = $null
$vmCredParts = Get-VmSshCredentialPartsFromEnv
$effectiveCaptureIntervalSeconds = if ($GuestScreensaverDurationMinutes -ge 120 -and $CaptureIntervalSeconds -lt 30) { 30 } else { $CaptureIntervalSeconds }
$effectiveUxTimeoutSeconds = [Math]::Max($UxTimeoutSeconds, ($GuestScreensaverDurationMinutes * 60) + 1800)

function Read-VmSharedJsonViaSftp {
    param(
        [Parameter(Mandatory = $true)]$Bundle,
        [Parameter(Mandatory = $true)][string]$RemotePath,
        [ValidateRange(1, 120)]
        [int]$Attempts = 12,
        [ValidateRange(1, 60000)]
        [int]$RetryDelayMilliseconds = 250
    )

    # Returns raw JSON text only after a complete parse succeeds. Empty means
    # "not written or still mid-write"; exhausted SFTP errors are surfaced.
    if ($null -eq $Bundle -or
        $null -eq $Bundle.PSObject.Properties['SftpSession'] -or
        $null -eq $Bundle.SftpSession) {
        throw 'SFTP session is missing from the VM SSH session bundle.'
    }

    if ($null -eq (Get-Command ConvertTo-SftpRemotePath -ErrorAction SilentlyContinue)) {
        throw 'Required helper ConvertTo-SftpRemotePath is unavailable; VmSshCommon.ps1 must be loaded before polling remote JSON via SFTP.'
    }

    $sftpPath = ConvertTo-SftpRemotePath -Path $RemotePath
    $lastSftpError = $null
    $lastJsonError = $null
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            if (-not (Test-SFTPPath -SFTPSession $Bundle.SftpSession -Path $sftpPath -ErrorAction Stop)) {
                return ''
            }

            $content = Get-SFTPContent -SFTPSession $Bundle.SftpSession -Path $sftpPath -Encoding UTF8 -ErrorAction Stop
            $json = (($content | ForEach-Object { [string]$_ }) -join [Environment]::NewLine).Trim()
            if ([string]::IsNullOrWhiteSpace($json)) {
                return ''
            }
        }
        catch {
            $lastSftpError = $_.Exception
            if ($attempt -ge $Attempts) {
                throw "Failed to read remote JSON through SFTP after $Attempts attempts: $RemotePath - $($lastSftpError.Message)"
            }

            Start-Sleep -Milliseconds $RetryDelayMilliseconds
            continue
        }

        try {
            $null = $json | ConvertFrom-Json -ErrorAction Stop
            return $json
        }
        catch {
            $lastJsonError = $_.Exception
            if ($attempt -ge $Attempts) {
                throw "Remote JSON remained malformed after $Attempts SFTP read attempts: $RemotePath - $($lastJsonError.Message)"
            }

            Start-Sleep -Milliseconds $RetryDelayMilliseconds
        }
    }

    return ''
}

if ($PushWorkspace) {
    & (Join-Path $PSScriptRoot 'Push-VmWorkspace.ps1') -VmHost $VmHost -VmPort $VmPort -RootPath $RootPath -Bootstrap:$Bootstrap
}

try {
    New-Item -ItemType Directory -Force -Path $hostArtifactsRoot | Out-Null
    $bundle = New-VmSshSessionBundle -HostName $VmHost -Port $VmPort
    Ensure-VmFreeSpace -Bundle $bundle -RootPath $RootPath -MinimumFreeGb 8 | Out-Null

    if ($Bootstrap -and -not $PushWorkspace) {
        $bootstrapCommand = @"
& '$(Join-Path $RootPath 'scripts\Guest-BootstrapVmRemoteTools.ps1')' -RootPath '$RootPath'
"@
        Invoke-VmPwshCommand -Bundle $bundle -Command $bootstrapCommand -TimeOutSeconds 1800 | Out-Null
    }

    $buildStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $remoteBuildSummary = Join-Path $RootPath ("results\buildtest-$buildStamp.json")
    $buildCommand = @"
`$repoRoot = Join-Path '$RootPath' 'repo'
`$resultPath = '$remoteBuildSummary'
`$publishRoot = Join-Path `$repoRoot 'build\artifacts\publish-safe-temp'
`$stagedPublish = Join-Path '$RootPath' 'publish'
`$summary = [ordered]@{
    StartedAt = (Get-Date).ToString('o')
    RepoRoot = `$repoRoot
    Result = 'Pending'
}
Push-Location `$repoRoot
try {
    Get-Process PortfolioSaver.VmAgent,PortfolioSaver.Config,PortfolioSaver.Desktop -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    & dotnet restore .\DoNotPanicPortfolioVisualizer.sln --disable-parallel --nologo
    if (`$LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    & dotnet build .\DoNotPanicPortfolioVisualizer.sln -c Release --nologo --no-restore
    if (`$LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

    & dotnet test .\tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --nologo --no-build
    if (`$LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }

    & .\build\publish-safe-temp.ps1 -Configuration Release -TimeoutSeconds 900

    if (Test-Path `$stagedPublish) {
        Remove-Item -LiteralPath `$stagedPublish -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Force -Path `$stagedPublish | Out-Null
    foreach (`$item in Get-ChildItem -LiteralPath `$publishRoot -Force) {
        Copy-Item -LiteralPath `$item.FullName -Destination (Join-Path `$stagedPublish `$item.Name) -Recurse -Force
    }

    `$summary.Result = 'Passed'
}
catch {
    `$summary.Result = 'Failed'
    `$summary.Error = `$_.Exception.Message
    throw
}
finally {
    Pop-Location
    `$summary.FinishedAt = (Get-Date).ToString('o')
    `$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath `$resultPath -Encoding UTF8
}
Write-Output ('BUILD_SUMMARY=' + `$resultPath)
"@

    Write-VmSshStep "Running remote restore/build/test/publish"
    $buildOutput = Invoke-VmPwshCommand -Bundle $bundle -Command $buildCommand -TimeOutSeconds $BuildTimeoutSeconds
    $buildOutput.Output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { Write-Host $_ }

    if ($RunUxDeep) {
        $remoteApplyTestSecrets = Join-Path $RootPath 'repo\build\vm\Guest-ApplyTestSecrets.ps1'
        $applySecretsCommand = @"
& '$remoteApplyTestSecrets' -RootPath '$RootPath'
"@
        Write-VmSshStep "Applying remote VM test secrets overlay"
        Invoke-VmPwshCommand -Bundle $bundle -Command $applySecretsCommand -TimeOutSeconds 120 | Out-Null

        $remoteUxSummary = Join-Path $RootPath ("results\$uxResultName\ux-deep-summary.json")
        $remoteAgentStatus = Join-Path $RootPath 'agent\agent-status.json'
        $remoteAgentResult = Join-Path $RootPath ("agent\command-results\$uxResultName.result.json")
        $remoteAgentCommand = Join-Path $RootPath ("commands\$uxResultName.json")
        $remoteAutomationSetup = Join-Path $RootPath 'repo\build\vm\Guest-ConfigureDesktopAutomation.ps1'
        $remoteAutomationCredentialCleanup = Join-Path $RootPath 'repo\build\vm\Guest-ClearDesktopAutomationCredentials.ps1'
        $remoteAgentExe = Join-Path $RootPath 'publish\agent\PortfolioSaver.VmAgent.exe'
        $remoteUser = $vmCredParts.UserName.Replace("'", "''")
        $prepareAutomationCommand = @"
& '$remoteAutomationSetup' -RootPath '$RootPath'
"@
        try {
            Write-VmSshStep "Configuring remote desktop automation"
            Invoke-VmPwshCommand -Bundle $bundle -Command $prepareAutomationCommand -TimeOutSeconds 120 | Out-Null

            $remoteAgentStatusCmdPath = $remoteAgentStatus.Replace('/', '\')
            $stopExistingAgentCommand = "cmd /c taskkill /IM PortfolioSaver.VmAgent.exe /F >nul 2>&1 & del /F /Q `"$remoteAgentStatusCmdPath`" >nul 2>&1 & schtasks /Delete /TN `"PortfolioSaverVmAgent`" /F >nul 2>&1 & exit /b 0"
            Write-VmSshStep "Stopping any existing desktop-session agent"
            Invoke-VmRawCommand -Bundle $bundle -Command $stopExistingAgentCommand -TimeOutSeconds 60 -AllowedExitCodes @(0) | Out-Null

            $startAgentCommand = @"
if (-not (Test-Path "$remoteAgentExe")) {
    throw 'Missing desktop-session agent executable.'
}
`$taskName = 'PortfolioSaverVmAgent'
`$taskTime = (Get-Date).ToString('HH:mm')
`$taskAction = '"$remoteAgentExe" --root-path "$RootPath"'
& schtasks.exe /Create /TN `$taskName /TR `$taskAction /SC ONCE /ST `$taskTime /IT /RU '$remoteUser' /F
if (`$LASTEXITCODE -ne 0) { throw 'Failed to create interactive desktop-session agent scheduled task.' }
`$runOutput = & schtasks.exe /Run /TN `$taskName
if (`$LASTEXITCODE -ne 0) { throw 'Failed to run interactive desktop-session agent scheduled task.' }
"@
            Write-VmSshStep "Starting desktop-session agent"
            $startEncoded = ConvertTo-VmPwshEncodedCommand -Command $startAgentCommand
            $startSucceeded = $false
            for ($startAttempt = 1; $startAttempt -le 2 -and -not $startSucceeded; $startAttempt++) {
                try {
                    Invoke-VmRawCommand -Bundle $bundle -Command ('pwsh -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ' + $startEncoded) -TimeOutSeconds 120 | Out-Null
                    $startSucceeded = $true
                }
                catch {
                    if ($startAttempt -ge 2) { throw }
                    Write-VmSshStep "Desktop-session agent start attempt failed once; retrying."
                    Start-Sleep -Seconds 3
                }
            }

            $agentDeadline = (Get-Date).AddSeconds(120)
            do {
                Start-Sleep -Seconds 5
                $agentJson = Read-VmSharedJsonViaSftp -Bundle $bundle -RemotePath $remoteAgentStatus
                if (-not [string]::IsNullOrWhiteSpace($agentJson)) {
                    $agentStatus = $agentJson | ConvertFrom-Json
                    $heartbeatUtc = [datetime]$agentStatus.LastHeartbeatUtc
                    if ($agentStatus.UserInteractive -and $agentStatus.SessionId -eq 1 -and ((Get-Date).ToUniversalTime() - $heartbeatUtc).TotalSeconds -lt 30) {
                        break
                    }
                }
            } while ((Get-Date) -lt $agentDeadline)

            if ((Get-Date) -ge $agentDeadline) {
                throw "Timed out waiting for remote desktop-session agent heartbeat: $remoteAgentStatus"
            }
        }
        finally {
            $cleanupAutomationCredentialsCommand = @"
schtasks /Delete /TN "PortfolioSaverVmAgent" /F >`$null 2>&1
& '$remoteAutomationCredentialCleanup'
"@
            Write-VmSshStep "Clearing remote desktop automation autologon credential"
            $cleanupOutput = Invoke-VmPwshCommand -Bundle $bundle -Command $cleanupAutomationCredentialsCommand -TimeOutSeconds 120
            $cleanupJson = ($cleanupOutput.Output -join [Environment]::NewLine).Trim()
            if ([string]::IsNullOrWhiteSpace($cleanupJson)) {
                throw "Remote desktop automation credential cleanup produced no verification output."
            }

            $cleanupState = $cleanupJson | ConvertFrom-Json
            if ($null -eq $cleanupState.PSObject.Properties['DefaultPasswordPresent'] -or $null -eq $cleanupState.PSObject.Properties['AutoAdminLogon']) {
                throw "Remote desktop automation credential cleanup returned malformed verification output."
            }

            if ($cleanupState.DefaultPasswordPresent -or $cleanupState.AutoAdminLogon -ne '0') {
                throw "Remote desktop automation credential cleanup verification failed."
            }
        }

        $commandPayload = [ordered]@{
            Id = $uxResultName
            Type = 'run-ux-deep'
            Payload = [ordered]@{
                ResultName = $uxResultName
                ResultRootPath = (Join-Path $RootPath 'results')
                ScreensaverDurationMinutes = $GuestScreensaverDurationMinutes
                CaptureIntervalSeconds = $effectiveCaptureIntervalSeconds
                ValidationCompletionMode = $ValidationCompletionMode
                DisplayWidth = if ($DisplayWidth -gt 0) { $DisplayWidth } else { $null }
                DisplayHeight = if ($DisplayHeight -gt 0) { $DisplayHeight } else { $null }
                DisplayProfile = if (-not [string]::IsNullOrWhiteSpace($DisplayProfile)) { $DisplayProfile } else { $null }
                FaultProfile = $FaultProfile
            }
        } | ConvertTo-Json -Depth 5
        Write-VmSshStep "Queuing UX run through desktop-session agent"
        $localAgentCommandPath = Join-Path ([System.IO.Path]::GetTempPath()) ($uxResultName + '.json')
        $commandPayload | Set-Content -LiteralPath $localAgentCommandPath -Encoding UTF8
        Ensure-VmDirectory -Bundle $bundle -RemotePath (Split-Path -Path $remoteAgentCommand -Parent)
        Send-VmItem -Bundle $bundle -LocalPath $localAgentCommandPath -RemoteDestination (Split-Path -Path $remoteAgentCommand -Parent)

        $commandDeadline = (Get-Date).AddSeconds(120)
        do {
            Start-Sleep -Seconds 5
            $resultJson = Read-VmSharedJsonViaSftp -Bundle $bundle -RemotePath $remoteAgentResult
            if (-not [string]::IsNullOrWhiteSpace($resultJson)) {
                break
            }
        } while ((Get-Date) -lt $commandDeadline)

        if ((Get-Date) -ge $commandDeadline) {
            throw "Timed out waiting for agent command acknowledgement: $remoteAgentResult"
        }

        Write-VmSshStep "Using UX timeout budget of $effectiveUxTimeoutSeconds seconds with capture interval $effectiveCaptureIntervalSeconds seconds"
        $deadline = (Get-Date).AddSeconds($effectiveUxTimeoutSeconds)
        do {
            Start-Sleep -Seconds 15
            $json = Read-VmSharedJsonViaSftp -Bundle $bundle -RemotePath $remoteUxSummary
            if (-not [string]::IsNullOrWhiteSpace($json)) {
                $summary = $json | ConvertFrom-Json
                $hasFinishedAt = $summary.PSObject.Properties.Name -contains 'FinishedAt'
                if ($hasFinishedAt -and -not [string]::IsNullOrWhiteSpace([string]$summary.FinishedAt)) {
                    Write-VmSshStep "Remote UX run finished"
                    break
                }
            }
        } while ((Get-Date) -lt $deadline)

        if ((Get-Date) -ge $deadline) {
            $statusCommand = @"
Get-Process PortfolioSaver.Config,PortfolioSaver.Desktop,pwsh,powershell -ErrorAction SilentlyContinue |
    Select-Object ProcessName,Id,SessionId,StartTime |
    ConvertTo-Json -Compress
"@
            $taskInfo = Invoke-VmPwshCommand -Bundle $bundle -Command $statusCommand -TimeOutSeconds 120
            $taskInfoText = ($taskInfo.Output -join [Environment]::NewLine).Trim()
            throw "Timed out waiting for remote UX summary: $remoteUxSummary`nProcessInfo=$taskInfoText"
        }

        $pullOutput = & (Join-Path $PSScriptRoot 'Pull-VmResults.ps1') -VmHost $VmHost -VmPort $VmPort -RootPath $RootPath -RemotePath (Join-Path $RootPath ("results\$uxResultName"))
        $localResultDirLine = [string[]]@($pullOutput | Where-Object { $_ -like 'LOCAL_RESULT_DIR=*' } | Select-Object -Last 1)
        $pullOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -notlike 'LOCAL_RESULT_DIR=*' } | ForEach-Object { Write-Host $_ }
        if ($localResultDirLine.Length -gt 0) {
            $localResultDir = ([string]$localResultDirLine[0]).Substring('LOCAL_RESULT_DIR='.Length)
            if (-not [string]::IsNullOrWhiteSpace($localResultDir) -and (Test-Path $localResultDir)) {
                Write-Output $localResultDirLine[0]
                & (Join-Path $PSScriptRoot 'PostProcess-ReferenceSpotChecks.ps1') -ResultRoot $localResultDir
            }
        }
    }

    $runCompleted = $true
}
catch {
    $runFailureReason = $_.Exception.Message
    throw
}
finally {
    if (-not $runCompleted -and $null -ne $bundle) {
        Write-VmSshStep "Run did not complete; requesting remote harness abort cleanup"
        $abortReason = if ([string]::IsNullOrWhiteSpace($runFailureReason)) { 'Invoke-VmBuildTest exited before completion without an exception message.' } else { $runFailureReason }
        Invoke-VmHarnessAbortCleanup -Bundle $bundle -RootPath $RootPath -Reason $abortReason -ResultName $uxResultName
    }
    if ($null -ne $bundle) {
        Remove-VmSshSessionBundle -Bundle $bundle
    }
    if ($null -ne $localAgentCommandPath -and (Test-Path $localAgentCommandPath)) {
        Remove-Item -LiteralPath $localAgentCommandPath -Force -ErrorAction SilentlyContinue
    }
}

