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
    [switch]$IncludePublishArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'VmSshCommon.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$tempRoot = Join-Path $env:TEMP ('PortfolioSaverVmPush-' + [guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $tempRoot 'repo-snapshot.tar'
$publishRoot = Join-Path $repoRoot 'build\artifacts\publish-safe-temp'
$localManifest = Join-Path $repoRoot 'build\vm\artifacts\host-runs'
$localTestSecretsPath = Join-Path $repoRoot 'build\vm\test-secrets.json'
$essentialOverlayFiles = @(
    'global.json'
)
$bundle = $null

try {
    New-Item -ItemType Directory -Force -Path $tempRoot,$localManifest | Out-Null
    $bundle = New-VmSshSessionBundle -HostName $VmHost -Port $VmPort

    Write-VmSshStep "Ensuring guest workspace directories"
    Ensure-VmDirectory -Bundle $bundle -RemotePath $RootPath
    Ensure-VmDirectory -Bundle $bundle -RemotePath (Join-Path $RootPath 'scripts')
    Ensure-VmDirectory -Bundle $bundle -RemotePath (Join-Path $RootPath 'artifacts')
    Ensure-VmDirectory -Bundle $bundle -RemotePath (Join-Path $RootPath 'logs')
    Ensure-VmFreeSpace -Bundle $bundle -RootPath $RootPath -MinimumFreeGb 8 | Out-Null

    $bootstrapRemoteDirectory = Join-Path $RootPath 'scripts'
    $bootstrapRemotePath = Join-Path $bootstrapRemoteDirectory 'Guest-BootstrapVmRemoteTools.ps1'
    Send-VmItem -Bundle $bundle -LocalPath (Join-Path $PSScriptRoot 'Guest-BootstrapVmRemoteTools.ps1') -RemoteDestination $bootstrapRemoteDirectory

    if ($Bootstrap) {
        Write-VmSshStep "Bootstrapping the guest toolchain and workspace"
        $bootstrapCommand = @"
& '$bootstrapRemotePath' -RootPath '$RootPath'
"@
        Invoke-VmPwshCommand -Bundle $bundle -Command $bootstrapCommand -TimeOutSeconds 1800 | Out-Null
    }

    Write-VmSshStep "Building a clean workspace archive"
    New-VmWorkspaceArchive -RepoRoot $repoRoot -ArchivePath $archivePath

    $remoteArchive = Join-Path $RootPath 'artifacts\repo-snapshot.tar'
    $remoteArtifactsDirectory = Join-Path $RootPath 'artifacts'
    $remoteTestSecretsPath = Join-Path $RootPath 'artifacts\test-secrets.json'
    Write-VmSshStep "Uploading repository snapshot"
    Send-VmItem -Bundle $bundle -LocalPath $archivePath -RemoteDestination $remoteArtifactsDirectory

    if (Test-Path $localTestSecretsPath) {
        Write-VmSshStep "Uploading VM test secrets overlay"
        Send-VmItem -Bundle $bundle -LocalPath $localTestSecretsPath -RemoteDestination $remoteArtifactsDirectory
    }
    else {
        $clearSecretsCommand = @"
Remove-Item -LiteralPath '$remoteTestSecretsPath' -Force -ErrorAction SilentlyContinue
"@
        Write-VmSshStep "Clearing any stale VM test secrets overlay"
        Invoke-VmPwshCommand -Bundle $bundle -Command $clearSecretsCommand -TimeOutSeconds 60 | Out-Null
    }

    $remoteRepo = Join-Path $RootPath 'repo'
    $expandCommand = @"
`$repoPath = '$remoteRepo'
`$archivePath = '$remoteArchive'
if (Test-Path `$repoPath) {
    Remove-Item -LiteralPath `$repoPath -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path `$repoPath | Out-Null
& tar -xf `$archivePath -C `$repoPath
if (`$LASTEXITCODE -ne 0) {
    throw 'tar extraction of repo snapshot failed.'
}
"@
    Write-VmSshStep "Expanding repository snapshot inside guest workspace"
    Invoke-VmPwshCommand -Bundle $bundle -Command $expandCommand -TimeOutSeconds 1800 | Out-Null

    $remoteRepoRoot = Join-Path $RootPath 'repo'
    $remoteOverlayRoot = Join-Path $RootPath 'artifacts\overlay'
    $remoteVmToolsTargetRoot = Join-Path $remoteRepoRoot 'build\vm'
    Ensure-VmDirectory -Bundle $bundle -RemotePath $remoteOverlayRoot
    foreach ($relativePath in $essentialOverlayFiles) {
        $localOverlayPath = Join-Path $repoRoot $relativePath
        if (-not (Test-Path $localOverlayPath)) {
            continue
        }

        Send-VmItem -Bundle $bundle -LocalPath $localOverlayPath -RemoteDestination $remoteOverlayRoot
    }

    $vmToolsLocalRoot = Join-Path $repoRoot 'build\vm'
    $vmToolFiles = Get-ChildItem -LiteralPath $vmToolsLocalRoot -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @('.ps1', '.json', '.md') }
    foreach ($vmToolFile in $vmToolFiles) {
        Send-VmItem -Bundle $bundle -LocalPath $vmToolFile.FullName -RemoteDestination $remoteOverlayRoot
    }

    $applyOverlayCommand = @"
`$overlayRoot = '$remoteOverlayRoot'
`$repoRoot = '$remoteRepoRoot'
`$vmTargetRoot = '$remoteVmToolsTargetRoot'
if (Test-Path (Join-Path `$overlayRoot 'global.json')) {
    Copy-Item -LiteralPath (Join-Path `$overlayRoot 'global.json') -Destination (Join-Path `$repoRoot 'global.json') -Force
}
New-Item -ItemType Directory -Force -Path `$vmTargetRoot | Out-Null
Get-ChildItem -LiteralPath `$overlayRoot -File -Force -ErrorAction SilentlyContinue |
    Where-Object { `$_.Name -ne 'global.json' } |
    ForEach-Object {
        Copy-Item -LiteralPath `$_.FullName -Destination (Join-Path `$vmTargetRoot `$_.Name) -Force
    }
"@
    Invoke-VmPwshCommand -Bundle $bundle -Command $applyOverlayCommand -TimeOutSeconds 120 | Out-Null

    if ($IncludePublishArtifacts -and (Test-Path $publishRoot)) {
        $publishArchive = Join-Path $tempRoot 'publish-safe-temp.zip'
        if (Test-Path $publishArchive) {
            Remove-Item -LiteralPath $publishArchive -Force -ErrorAction SilentlyContinue
        }

        Write-VmSshStep "Packaging publish artifacts"
        Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $publishArchive -CompressionLevel Optimal -Force
        $remotePublishArchive = Join-Path $RootPath 'artifacts\publish-safe-temp.zip'
        Send-VmItem -Bundle $bundle -LocalPath $publishArchive -RemoteDestination $remoteArtifactsDirectory

        $expandPublishCommand = @"
`$publishPath = Join-Path '$RootPath' 'publish'
`$zipPath = '$remotePublishArchive'
if (Test-Path `$publishPath) {
    Remove-Item -LiteralPath `$publishPath -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path `$publishPath | Out-Null
Expand-Archive -LiteralPath `$zipPath -DestinationPath `$publishPath -Force
"@
        Write-VmSshStep "Expanding publish artifacts inside guest workspace"
        Invoke-VmPwshCommand -Bundle $bundle -Command $expandPublishCommand -TimeOutSeconds 1800 | Out-Null
    }

    $manifest = [ordered]@{
        GeneratedAt = (Get-Date).ToString('o')
        VmHost = $VmHost
        RootPath = $RootPath
        Bootstrap = [bool]$Bootstrap
        IncludePublishArtifacts = [bool]$IncludePublishArtifacts
        ArchiveName = 'repo-snapshot.tar'
        PublishArtifactPresent = [bool](Test-Path $publishRoot)
        TestSecretsPresent = [bool](Test-Path $localTestSecretsPath)
    }
    $manifestPath = Join-Path $localManifest ("ssh-push-{0:yyyyMMdd-HHmmss}.json" -f (Get-Date))
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

    Write-Output ("VM_PUSH_MANIFEST=" + $manifestPath)
}
finally {
    if ($null -ne $bundle) {
        Remove-VmSshSessionBundle -Bundle $bundle
    }
    if (Test-Path $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
