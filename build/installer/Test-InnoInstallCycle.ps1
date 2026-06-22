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
#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SetupPath,
    [string]$LogRoot = '',
    [switch]$AllowNonElevatedSkip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-LoggedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$StdoutPath,
        [Parameter(Mandatory = $true)][string]$StderrPath
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Failed to start process: $FilePath"
    }

    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    Set-Content -LiteralPath $StdoutPath -Value $stdout -Encoding UTF8
    Set-Content -LiteralPath $StderrPath -Value $stderr -Encoding UTF8

    if ($process.ExitCode -ne 0) {
        throw "Process failed with exit code $($process.ExitCode): $FilePath $($Arguments -join ' ')"
    }
}

function Get-InstalledInnoUninstallerPath {
    $uninstallKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B0839D4C-1D29-4D9C-95E3-C88E4D8E37E5}_is1'
    $properties = Get-ItemProperty -LiteralPath $uninstallKey -ErrorAction SilentlyContinue
    if ($null -eq $properties -or [string]::IsNullOrWhiteSpace([string]$properties.UninstallString)) {
        return ''
    }

    $uninstallString = [string]$properties.UninstallString
    $match = [regex]::Match($uninstallString, '^\s*"(?<path>[^"]+)"')
    if ($match.Success) {
        return $match.Groups['path'].Value
    }

    return ($uninstallString -split '\s+', 2)[0]
}

function Wait-InstallRootRemoved {
    $installRoot = Join-Path $env:ProgramFiles 'SANYALnet Labs\DoNotPanicPortfolioVisualizer'
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        if (-not (Test-Path -LiteralPath $installRoot)) {
            return
        }

        Start-Sleep -Milliseconds 500
    }
}

function Assert-InstalledState {
    param([bool]$ExpectedInstalled)

    $installRoot = Join-Path $env:ProgramFiles 'SANYALnet Labs\DoNotPanicPortfolioVisualizer'
    $desktopExe = Join-Path $installRoot 'PortfolioSaver.Desktop.exe'
    $license = Join-Path $installRoot 'LICENSE'
    $apache = Join-Path $installRoot 'THIRD-PARTY-LICENSES\APACHE-2.0.txt'
    $uninstallKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B0839D4C-1D29-4D9C-95E3-C88E4D8E37E5}_is1'

    if ($ExpectedInstalled) {
        foreach ($path in @($desktopExe, $license, $apache)) {
            if (-not (Test-Path -LiteralPath $path)) {
                throw "Expected installed file missing: $path"
            }
        }

        if (-not (Test-Path -LiteralPath $uninstallKey)) {
            throw "Expected Inno uninstall key missing: $uninstallKey"
        }
        return
    }

    if (Test-Path -LiteralPath $desktopExe) {
        throw "Desktop executable still present after uninstall: $desktopExe"
    }

    $staleUninstallers = @()
    if (Test-Path -LiteralPath $installRoot) {
        $staleUninstallers = @(Get-ChildItem -LiteralPath $installRoot -Filter 'unins*.exe' -Force -ErrorAction SilentlyContinue |
            ForEach-Object { $_.FullName })
    }

    if ($staleUninstallers.Count -gt 0) {
        throw "Inno uninstaller stub still present after uninstall: $($staleUninstallers -join '; ')"
    }

    if (Test-Path -LiteralPath $installRoot) {
        $remaining = @(Get-ChildItem -LiteralPath $installRoot -Force -ErrorAction SilentlyContinue |
            ForEach-Object { $_.FullName })
        throw "Install root still present after uninstall: $installRoot. Remaining: $($remaining -join '; ')"
    }

    if (Test-Path -LiteralPath $uninstallKey) {
        throw "Inno uninstall key still present after uninstall: $uninstallKey"
    }
}

if (-not (Test-Path -LiteralPath $SetupPath -PathType Leaf)) {
    throw "Setup executable not found: $SetupPath"
}

if ([string]::IsNullOrWhiteSpace($LogRoot)) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $LogRoot = Join-Path $repoRoot 'build\validation\artifacts\inno-install-cycle'
}

New-Item -ItemType Directory -Force -Path $LogRoot | Out-Null
$resolvedLogRoot = (Resolve-Path -LiteralPath $LogRoot).Path

if (-not (Test-IsAdministrator)) {
    $message = 'INNO_INSTALL_CYCLE_SKIPPED=NeedsElevatedAdministratorContext'
    Set-Content -LiteralPath (Join-Path $resolvedLogRoot 'elevation-required.log') -Value $message -Encoding UTF8
    if ($AllowNonElevatedSkip) {
        Write-Output $message
        return
    }

    throw 'Inno silent install/uninstall validation must run from an already elevated administrator context. UAC prompts cannot be safely auto-accepted from a non-elevated process.'
}

$setupLog = Join-Path $resolvedLogRoot 'install.log'
$uninstallLog = Join-Path $resolvedLogRoot 'uninstall.log'

Invoke-LoggedProcess `
    -FilePath (Resolve-Path -LiteralPath $SetupPath).Path `
    -Arguments @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$setupLog") `
    -StdoutPath (Join-Path $resolvedLogRoot 'install.stdout.log') `
    -StderrPath (Join-Path $resolvedLogRoot 'install.stderr.log')

Assert-InstalledState -ExpectedInstalled $true

$uninstaller = Get-InstalledInnoUninstallerPath
if ([string]::IsNullOrWhiteSpace($uninstaller)) {
    throw 'Could not locate Inno uninstaller path from registry. The expected uninstall key may be missing or invalid.'
}

if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
    throw "Inno uninstaller missing: $uninstaller"
}

Invoke-LoggedProcess `
    -FilePath $uninstaller `
    -Arguments @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$uninstallLog") `
    -StdoutPath (Join-Path $resolvedLogRoot 'uninstall.stdout.log') `
    -StderrPath (Join-Path $resolvedLogRoot 'uninstall.stderr.log')

Wait-InstallRootRemoved
Assert-InstalledState -ExpectedInstalled $false
Write-Output "INNO_INSTALL_CYCLE_OK=$resolvedLogRoot"
