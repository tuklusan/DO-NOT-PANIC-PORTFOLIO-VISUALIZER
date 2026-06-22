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
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,
    [string]$ProgramName = "DO NOT PANIC PORTFOLIO VISUALIZER"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $normalizedBase = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $baseUri = New-Object System.Uri($normalizedBase)
    $targetUri = New-Object System.Uri([System.IO.Path]::GetFullPath($TargetPath))
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('\', '/')
}

$resolvedPublishDir = (Resolve-Path -LiteralPath $PublishDir).Path
if (-not (Test-Path -LiteralPath $resolvedPublishDir -PathType Container)) {
    throw "Publish directory not found: $PublishDir"
}

$manifestPath = Join-Path $resolvedPublishDir "release-manifest.json"
$files = Get-ChildItem -LiteralPath $resolvedPublishDir -Recurse -File | Where-Object {
    $_.FullName -ne $manifestPath
}

$primaryExe = Get-ChildItem -LiteralPath $resolvedPublishDir -File -Filter "*.exe" | Select-Object -First 1
$productVersion = if ($null -ne $primaryExe) { $primaryExe.VersionInfo.ProductVersion } else { "unknown" }

$entries = foreach ($file in ($files | Sort-Object FullName)) {
    $relativePath = Get-RelativePath -BasePath $resolvedPublishDir -TargetPath $file.FullName
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToLowerInvariant()
    [ordered]@{
        path = $relativePath
        sizeBytes = [int64]$file.Length
        sha256 = $hash
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    productName = $ProgramName
    productVersion = $productVersion
    generatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    files = @($entries)
}

$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Output ("Generated manifest: {0} ({1} files, version {2})" -f $manifestPath, @($entries).Count, $productVersion)
