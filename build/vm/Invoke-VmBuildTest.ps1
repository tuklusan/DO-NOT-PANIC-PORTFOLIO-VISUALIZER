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
    [int]$BuildTimeoutSeconds = 3600,
    [int]$UxTimeoutSeconds = 2400
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'VmSshCommon.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostArtifactsRoot = Join-Path $repoRoot 'build\vm\artifacts\ssh-runs'
$bundle = $null
$localAgentCommandPath = $null
$uxResultName = 'ux-deep-ssh-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$vmCredParts = Get-VmSshCredentialPartsFromEnv
$effectiveCaptureIntervalSeconds = if ($GuestScreensaverDurationMinutes -ge 120 -and $CaptureIntervalSeconds -lt 30) { 30 } else { $CaptureIntervalSeconds }
$effectiveUxTimeoutSeconds = [Math]::Max($UxTimeoutSeconds, ($GuestScreensaverDurationMinutes * 60) + 1800)

function ConvertTo-RemoteSingleQuotedLiteral {
    param([Parameter(Mandatory = $true)][string]$Value)

    return $Value.Replace("'", "''")
}

function New-RemoteSharedJsonReadCommand {
    param([Parameter(Mandatory = $true)][string]$Path)

    $escapedPath = ConvertTo-RemoteSingleQuotedLiteral -Value $Path
    return @"
`$path = '$escapedPath'
if (Test-Path `$path) {
    for (`$attempt = 0; `$attempt -lt 12; `$attempt++) {
        try {
            `$stream = `$null
            `$reader = `$null
            `$stream = [System.IO.File]::Open(
                `$path,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read,
                [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
            try {
                `$reader = New-Object System.IO.StreamReader(`$stream)
                try {
                    `$reader.ReadToEnd()
                }
                finally {
                    if (`$null -ne `$reader) {
                        `$reader.Dispose()
                    }
                }
                break
            }
            finally {
                if (`$null -ne `$stream) {
                    `$stream.Dispose()
                }
            }
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }
}
"@
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
    Get-Process PortfolioSaver.VmAgent,PortfolioSaver.Config,PortfolioSaver.Desktop,PortfolioSaver.Screensaver -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    & dotnet restore .\PortfolioScreensaver.sln --disable-parallel --nologo
    if (`$LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    & dotnet build .\PortfolioScreensaver.sln -c Release --nologo --no-restore
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
                $pollAgentCommand = New-RemoteSharedJsonReadCommand -Path $remoteAgentStatus
                $agentPoll = Invoke-VmPwshCommand -Bundle $bundle -Command $pollAgentCommand -TimeOutSeconds 60
                $agentJson = ($agentPoll.Output -join [Environment]::NewLine).Trim()
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
            $pollCommandResult = New-RemoteSharedJsonReadCommand -Path $remoteAgentResult
            $resultPoll = Invoke-VmPwshCommand -Bundle $bundle -Command $pollCommandResult -TimeOutSeconds 60
            $resultJson = ($resultPoll.Output -join [Environment]::NewLine).Trim()
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
            $pollCommand = New-RemoteSharedJsonReadCommand -Path $remoteUxSummary
            $poll = Invoke-VmPwshCommand -Bundle $bundle -Command $pollCommand -TimeOutSeconds 120
            $json = ($poll.Output -join [Environment]::NewLine).Trim()
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
Get-Process PortfolioSaver.Config,PortfolioSaver.Desktop,PortfolioSaver.Screensaver,pwsh,powershell -ErrorAction SilentlyContinue |
    Select-Object ProcessName,Id,SessionId,StartTime |
    ConvertTo-Json -Compress
"@
            $taskInfo = Invoke-VmPwshCommand -Bundle $bundle -Command $statusCommand -TimeOutSeconds 120
            $taskInfoText = ($taskInfo.Output -join [Environment]::NewLine).Trim()
            throw "Timed out waiting for remote UX summary: $remoteUxSummary`nProcessInfo=$taskInfoText"
        }

        $pullOutput = & (Join-Path $PSScriptRoot 'Pull-VmResults.ps1') -VmHost $VmHost -VmPort $VmPort -RootPath $RootPath -RemotePath (Join-Path $RootPath ("results\$uxResultName"))
        $pullOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { Write-Host $_ }
        $localResultDirLine = [string[]]@($pullOutput | Where-Object { $_ -like 'LOCAL_RESULT_DIR=*' } | Select-Object -Last 1)
        if ($localResultDirLine.Length -gt 0) {
            $localResultDir = ([string]$localResultDirLine[0]).Substring('LOCAL_RESULT_DIR='.Length)
            if (-not [string]::IsNullOrWhiteSpace($localResultDir) -and (Test-Path $localResultDir)) {
                & (Join-Path $PSScriptRoot 'PostProcess-ReferenceSpotChecks.ps1') -ResultRoot $localResultDir
            }
        }
    }
}
finally {
    if ($null -ne $bundle) {
        Remove-VmSshSessionBundle -Bundle $bundle
    }
    if ($null -ne $localAgentCommandPath -and (Test-Path $localAgentCommandPath)) {
        Remove-Item -LiteralPath $localAgentCommandPath -Force -ErrorAction SilentlyContinue
    }
}
