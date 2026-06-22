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
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$workspaceRoot = "C:\Users\WDAGUtilityAccount\Desktop\PortfolioSaverWorkspace"
$resultsRoot = Join-Path $workspaceRoot "build\sandbox\results"
$logPath = Join-Path $resultsRoot "sandbox-smoke-test.log"
$resultPath = Join-Path $resultsRoot "sandbox-smoke-test.json"

New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null
Remove-Item -LiteralPath $logPath,$resultPath -Force -ErrorAction SilentlyContinue

function Write-Log {
    param([string]$Message)

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "[$timestamp] $Message"
    Add-Content -LiteralPath $logPath -Value $line -Encoding ASCII
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-InstalledState {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("Installed", "Uninstalled")]
        [string]$ExpectedState
    )

    $scrPath = Join-Path $env:WINDIR "System32\PortfolioSaver.Screensaver.scr"
    $manifestPath = Join-Path $env:ProgramData "PortfolioSaverScreensaver\installed-files.txt"
    $uninstallKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PortfolioSaverScreensaver"

    $checks = @(
        [pscustomobject]@{
            Name = "Screensaver file"
            Present = Test-Path $scrPath
            Details = $scrPath
        },
        [pscustomobject]@{
            Name = "Install manifest"
            Present = Test-Path $manifestPath
            Details = $manifestPath
        },
        [pscustomobject]@{
            Name = "Uninstall registry key"
            Present = Test-Path $uninstallKey
            Details = $uninstallKey
        }
    )

    $expectedPresent = $ExpectedState -eq "Installed"
    $failedChecks = @($checks | Where-Object { $_.Present -ne $expectedPresent })
    $lines = New-Object System.Collections.Generic.List[string]
    $exitCode = 1
    $lines.Add("PortfolioSaver expected state: $ExpectedState") | Out-Null
    $lines.Add("") | Out-Null
    foreach ($check in $checks) {
        $status = if ($check.Present) { "Present" } else { "Missing" }
        $lines.Add(("{0,-24} {1,-8} {2}" -f $check.Name, $status, $check.Details)) | Out-Null
    }
    $lines.Add("") | Out-Null
    if ($failedChecks.Count -eq 0) {
        $exitCode = 0
        $lines.Add("Validation passed.") | Out-Null
    }
    else {
        $lines.Add("Validation failed.") | Out-Null
    }

    return [pscustomobject]@{
        ExpectedState = $ExpectedState
        ExitCode = $exitCode
        Output = ($lines -join [Environment]::NewLine)
    }
}

function Get-ConfigProcessInfo {
    $config = Get-Process PortfolioSaver.Config -ErrorAction SilentlyContinue | Select-Object -First 1 ProcessName, Id, MainWindowTitle, Path
    if ($null -eq $config) {
        return $null
    }

    return [pscustomobject]@{
        ProcessName = $config.ProcessName
        Id = $config.Id
        MainWindowTitle = $config.MainWindowTitle
        Path = $config.Path
    }
}

$summary = [ordered]@{
    StartedAt = (Get-Date).ToString("o")
    UserName = $env:USERNAME
    IsAdministrator = Test-IsAdministrator
    Install = $null
    DirectConfigLaunch = $null
    ConfigureLaunchShell = $null
    ConfigureLaunchCmd = $null
    Uninstall = $null
    Succeeded = $false
}

try {
    Write-Log "Smoke test starting."
    Write-Log "Admin token detected: $($summary.IsAdministrator)"

    $stageRoot = Join-Path $workspaceRoot "build\artifacts\installer-stage"
    $installerScript = Join-Path $stageRoot "Install-PortfolioSaverScreensaver.ps1"
    $uninstallScript = Join-Path $stageRoot "Uninstall-PortfolioSaverScreensaver.ps1"

    if (-not (Test-Path $installerScript)) {
        throw "Installer script not found: $installerScript"
    }

    Write-Log "Running installer script from $installerScript"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Installer script exited with code $LASTEXITCODE"
    }

    $installValidation = Test-InstalledState -ExpectedState Installed
    $summary.Install = $installValidation
    Write-Log "Install validation exit code: $($installValidation.ExitCode)"
    Write-Log $installValidation.Output

    if ($installValidation.ExitCode -ne 0) {
        throw "Install validation failed."
    }

    $screensaverPath = Join-Path $env:WINDIR "System32\PortfolioSaver.Screensaver.scr"
    $configPath = Join-Path $env:WINDIR "System32\PortfolioSaver.Config.exe"
    if (-not (Test-Path $screensaverPath)) {
        throw "Installed screensaver not found: $screensaverPath"
    }

    Get-Process PortfolioSaver.Config -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    Write-Log "Launching installed config app directly"
    $directConfigProcess = $null
    if (Test-Path $configPath) {
        $directConfigProcess = Start-Process -FilePath $configPath -PassThru
        Start-Sleep -Seconds 5
    }

    $directConfigInfo = Get-ConfigProcessInfo
    $summary.DirectConfigLaunch = [pscustomobject]@{
        ConfigPathExists = (Test-Path $configPath)
        ConfigProcess = $directConfigInfo
        Success = ($null -ne $directConfigInfo -and -not [string]::IsNullOrWhiteSpace($directConfigInfo.MainWindowTitle))
    }

    if ($summary.DirectConfigLaunch.Success) {
        Write-Log "Direct config launch succeeded: $($directConfigInfo.MainWindowTitle)"
    }
    else {
        Write-Log "Direct config launch did not appear."
    }

    Get-Process PortfolioSaver.Config -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    Write-Log "Launching installed screensaver with /c via shell"
    $configureLogPath = Join-Path $env:TEMP "PortfolioSaver.Screensaver.configure.log"
    Remove-Item -LiteralPath $configureLogPath -Force -ErrorAction SilentlyContinue
    $configureProcess = Start-Process -FilePath $screensaverPath -ArgumentList "/c" -PassThru
    Start-Sleep -Seconds 5

    $configInfo = Get-ConfigProcessInfo
    $configureExited = $configureProcess.HasExited
    $configureLog = if (Test-Path $configureLogPath) { (Get-Content -LiteralPath $configureLogPath -Raw) } else { $null }

    $summary.ConfigureLaunchShell = [pscustomobject]@{
        LauncherProcessName = $configureProcess.ProcessName
        LauncherPath = $configureProcess.Path
        ScreensaverProcessExited = $configureExited
        ConfigProcess = $configInfo
        LogContents = $configureLog
        Success = ($null -ne $configInfo -and -not [string]::IsNullOrWhiteSpace($configInfo.MainWindowTitle))
    }

    if ($summary.ConfigureLaunchShell.Success) {
        Write-Log "Settings UI launched: $($configInfo.MainWindowTitle)"
    }
    else {
        Write-Log "Shell launch did not open settings."
        if (-not [string]::IsNullOrWhiteSpace($configureLog)) {
            Write-Log "Shell launch diagnostics:"
            Write-Log $configureLog.TrimEnd()
        }
    }

    Get-Process PortfolioSaver.Config -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process PortfolioSaver.Screensaver -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    Write-Log "Launching installed screensaver with /c via cmd.exe"
    Remove-Item -LiteralPath $configureLogPath -Force -ErrorAction SilentlyContinue
    $cmdLaunch = Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$screensaverPath`" /c" -PassThru
    Start-Sleep -Seconds 5

    $cmdConfigInfo = Get-ConfigProcessInfo
    $cmdLaunchExited = $cmdLaunch.HasExited
    $cmdConfigureLog = if (Test-Path $configureLogPath) { (Get-Content -LiteralPath $configureLogPath -Raw) } else { $null }

    $summary.ConfigureLaunchCmd = [pscustomobject]@{
        LauncherProcessName = $cmdLaunch.ProcessName
        LauncherPath = $cmdLaunch.Path
        LauncherExited = $cmdLaunchExited
        ConfigProcess = $cmdConfigInfo
        LogContents = $cmdConfigureLog
        Success = ($null -ne $cmdConfigInfo -and -not [string]::IsNullOrWhiteSpace($cmdConfigInfo.MainWindowTitle))
    }

    if ($summary.ConfigureLaunchCmd.Success) {
        Write-Log "cmd.exe launch opened settings: $($cmdConfigInfo.MainWindowTitle)"
    }
    else {
        Write-Log "cmd.exe launch did not open settings."
        if (-not [string]::IsNullOrWhiteSpace($cmdConfigureLog)) {
            Write-Log "cmd.exe diagnostics:"
            Write-Log $cmdConfigureLog.TrimEnd()
        }
    }

    Get-Process PortfolioSaver.Config -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process PortfolioSaver.Screensaver -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    Write-Log "Running uninstall script from $uninstallScript"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $uninstallScript
    if ($LASTEXITCODE -ne 0) {
        throw "Uninstall script exited with code $LASTEXITCODE"
    }

    $uninstallValidation = Test-InstalledState -ExpectedState Uninstalled
    $summary.Uninstall = $uninstallValidation
    Write-Log "Uninstall validation exit code: $($uninstallValidation.ExitCode)"
    Write-Log $uninstallValidation.Output

    if ($uninstallValidation.ExitCode -ne 0) {
        throw "Uninstall validation failed."
    }

    $summary.Succeeded = [bool]($summary.DirectConfigLaunch.Success -and ($summary.ConfigureLaunchShell.Success -or $summary.ConfigureLaunchCmd.Success))
}
catch {
    Write-Log "Smoke test failed: $($_.Exception.Message)"
    $summary.Error = $_.Exception.ToString()
}
finally {
    $summary.FinishedAt = (Get-Date).ToString("o")
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resultPath -Encoding ASCII
    Write-Log "Smoke test finished. Results written to $resultPath"
}
