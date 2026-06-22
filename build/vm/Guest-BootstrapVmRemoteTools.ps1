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
param(
    [string]$RootPath = 'C:\vmharness\portfolio-saver'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-CommandPathOrNull {
    param([Parameter(Mandatory = $true)][string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) { return $null }
    return $command.Source
}

function Ensure-ChocoPackage {
    param(
        [Parameter(Mandatory = $true)][string]$PackageName,
        [Parameter(Mandatory = $true)][string]$CommandName
    )

    $existing = Get-CommandPathOrNull -Name $CommandName
    if ($null -ne $existing) {
        return [ordered]@{
            Package = $PackageName
            Command = $CommandName
            Status = 'present'
            Path = $existing
        }
    }

    if (-not (Get-Command choco.exe -ErrorAction SilentlyContinue)) {
        throw "Chocolatey is required to install $PackageName but is not available."
    }

    choco install $PackageName -y --no-progress | Out-Null
    $installed = Get-CommandPathOrNull -Name $CommandName
    if ($null -eq $installed) {
        throw "Command '$CommandName' was still not found after installing package '$PackageName'."
    }

    return [ordered]@{
        Package = $PackageName
        Command = $CommandName
        Status = 'installed'
        Path = $installed
    }
}

$directories = @(
    $RootPath,
    (Join-Path $RootPath 'repo'),
    (Join-Path $RootPath 'publish'),
    (Join-Path $RootPath 'artifacts'),
    (Join-Path $RootPath 'results'),
    (Join-Path $RootPath 'logs'),
    (Join-Path $RootPath 'scripts')
)

foreach ($directory in $directories) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$packageStatus = @()
$packageStatus += Ensure-ChocoPackage -PackageName 'powershell-core' -CommandName 'pwsh.exe'
$packageStatus += Ensure-ChocoPackage -PackageName 'dotnet-10.0-sdk' -CommandName 'dotnet.exe'

$toolSnapshot = [ordered]@{
    pwsh = Get-CommandPathOrNull -Name 'pwsh.exe'
    dotnet = Get-CommandPathOrNull -Name 'dotnet.exe'
    git = Get-CommandPathOrNull -Name 'git.exe'
    python = Get-CommandPathOrNull -Name 'python.exe'
    jq = Get-CommandPathOrNull -Name 'jq.exe'
    rg = Get-CommandPathOrNull -Name 'rg.exe'
    sevenZip = Get-CommandPathOrNull -Name '7z.exe'
    ssh = Get-CommandPathOrNull -Name 'ssh.exe'
    winAppDriver = if (Test-Path 'C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe') { 'C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe' } else { $null }
}

$report = [ordered]@{
    GeneratedAt = (Get-Date).ToString('o')
    RootPath = $RootPath
    Directories = $directories
    PackageStatus = $packageStatus
    ToolSnapshot = $toolSnapshot
    DotNetSdks = (& dotnet --list-sdks)
    PowerShellVersion = (& pwsh -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()')
}

$reportPath = Join-Path (Join-Path $RootPath 'logs') ("bootstrap-{0:yyyyMMdd-HHmmss}.json" -f (Get-Date))
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8

Write-Output ("BOOTSTRAP_REPORT=" + $reportPath)
