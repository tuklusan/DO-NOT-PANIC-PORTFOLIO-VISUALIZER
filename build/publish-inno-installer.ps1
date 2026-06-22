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
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [int]$PublishTimeoutSeconds = 1200,
    [switch]$SkipPublish,
    [switch]$SkipCompile,
    [switch]$RequireIscc
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host ("[{0}] {1}" -f (Get-Date).ToString('HH:mm:ss'), $Message)
}

function Resolve-Iscc {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    foreach ($candidate in @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Source directory not found: $Source"
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $copyExit = 0
    robocopy $Source $Destination /E /NFL /NDL /NJH /NJS /NP | Out-Null
    $copyExit = $LASTEXITCODE
    if ($copyExit -gt 7) {
        throw "robocopy failed: $Source -> $Destination (exit=$copyExit)"
    }
}

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required file not found: $Source"
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Get-PortfolioSaverVersion {
    param([Parameter(Mandatory = $true)][string]$DirectoryBuildPropsPath)

    [xml]$props = Get-Content -Raw -LiteralPath $DirectoryBuildPropsPath
    $version = $props.Project.PropertyGroup.PortfolioSaverVersion | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "PortfolioSaverVersion not found in $DirectoryBuildPropsPath"
    }

    return [string]$version
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactsRoot = Join-Path $repoRoot 'build\artifacts'
$safeTempRoot = Join-Path $artifactsRoot 'publish-safe-temp'
$innoRoot = Join-Path $artifactsRoot 'inno'
$payloadRoot = Join-Path $innoRoot 'payload'
$outputRoot = Join-Path $innoRoot 'output'
$scriptPath = Join-Path $repoRoot 'build\installer\DoNotPanicPortfolioVisualizer.iss'
$cleanupScript = Join-Path $repoRoot 'build\installer\Cleanup-DoNotPanicPortfolioVisualizer.ps1'
$manifestScript = Join-Path $repoRoot 'build\generate-release-manifest.ps1'
$licensePath = Join-Path $repoRoot 'LICENSE'
$iconPath = Join-Path $repoRoot 'src\PortfolioSaver.Shared\Assets\Branding\dnppv-icon-rev-3.ico'
$version = Get-PortfolioSaverVersion -DirectoryBuildPropsPath (Join-Path $repoRoot 'Directory.Build.props')
if (-not (Test-Path -LiteralPath $manifestScript -PathType Leaf)) {
    throw "Release manifest generator not found: $manifestScript"
}

if (-not $SkipPublish) {
    Write-Step 'Running canonical safe-temp publish before Inno payload assembly'
    & (Join-Path $repoRoot 'build\publish-safe-temp.ps1') -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier -TimeoutSeconds $PublishTimeoutSeconds
    if ($LASTEXITCODE -ne 0) {
        throw "publish-safe-temp.ps1 failed with exit code $LASTEXITCODE"
    }
}

$desktopRoot = Join-Path $safeTempRoot 'desktop'
$configRoot = Join-Path $safeTempRoot 'config'
$screensaverRoot = Join-Path $safeTempRoot 'screensaver'
if ($SkipPublish) {
    foreach ($requiredSafeTempRoot in @($desktopRoot, $configRoot, $screensaverRoot)) {
        if (-not (Test-Path -LiteralPath $requiredSafeTempRoot -PathType Container)) {
            throw "Safe-temp publish directory missing: $requiredSafeTempRoot. Run without -SkipPublish or run build/publish-safe-temp.ps1 first."
        }
    }
}

Remove-Item -LiteralPath $innoRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $payloadRoot,$outputRoot | Out-Null

Write-Step 'Assembling Inno Program Files payload'
Copy-DirectoryContents -Source $desktopRoot -Destination $payloadRoot
Copy-DirectoryContents -Source $configRoot -Destination $payloadRoot
Copy-DirectoryContents -Source $screensaverRoot -Destination $payloadRoot
Copy-RequiredFile -Source (Join-Path $screensaverRoot 'PortfolioSaver.Screensaver.exe') -Destination (Join-Path $payloadRoot 'PortfolioSaver.Screensaver.scr')
Copy-RequiredFile -Source $cleanupScript -Destination (Join-Path $payloadRoot 'Installer\Cleanup-DoNotPanicPortfolioVisualizer.ps1')
Get-ChildItem -LiteralPath $payloadRoot -Recurse -File -Include '*.pdb','*.nupkg' -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

foreach ($requiredPayloadFile in @(
    'PortfolioSaver.Desktop.exe',
    'PortfolioSaver.Config.exe',
    'PortfolioSaver.Screensaver.scr',
    'LICENSE',
    'THIRD-PARTY-NOTICES.md',
    'THIRD-PARTY-LICENSES\APACHE-2.0.txt',
    'YFinanceServer\YFinance.NET.Server.dll',
    'Installer\Cleanup-DoNotPanicPortfolioVisualizer.ps1'
)) {
    $path = Join-Path $payloadRoot $requiredPayloadFile
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Inno payload missing required file: $requiredPayloadFile"
    }
}

& $manifestScript -PublishDir $payloadRoot
if ($LASTEXITCODE -ne 0) {
    throw "Manifest generation failed for Inno payload."
}

$isccPath = Resolve-Iscc
if ($SkipCompile) {
    Write-Step "INNO_PAYLOAD_READY payload=$payloadRoot"
    return
}

if ([string]::IsNullOrWhiteSpace($isccPath)) {
    $message = 'Inno Setup compiler ISCC.exe was not found. Install Inno Setup 6 or rerun with -SkipCompile for static payload validation.'
    if ($RequireIscc) {
        throw $message
    }

    Write-Warning $message
    Write-Step "INNO_PAYLOAD_READY payload=$payloadRoot"
    return
}

Write-Step "Compiling Inno setup with $isccPath"
$isccArgs = @(
    "/DSourceRoot=$payloadRoot",
    "/DOutputRoot=$outputRoot",
    "/DLicenseFile=$licensePath",
    "/DAppVersion=$version"
)
if (Test-Path -LiteralPath $iconPath) {
    $isccArgs += "/DIconFile=$iconPath"
}
$isccArgs += $scriptPath

& $isccPath @isccArgs
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

$setupPath = Join-Path $outputRoot "DoNotPanicPortfolioVisualizerSetup-$version.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Expected Inno setup output was not created: $setupPath"
}

Write-Step "INNO_SETUP_CREATED setup=$setupPath"
