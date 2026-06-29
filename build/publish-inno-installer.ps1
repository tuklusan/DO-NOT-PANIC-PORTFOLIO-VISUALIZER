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
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [int]$PublishTimeoutSeconds = 1200,
    [switch]$SkipPublish,
    [switch]$SkipCompile,
    [switch]$RequireIscc,
    [string]$CodeSigningCertificateThumbprint = $env:DNPPV_CODESIGN_THUMBPRINT,
    [string]$CodeSigningTimestampUrl = $(if ([string]::IsNullOrWhiteSpace($env:DNPPV_CODESIGN_TIMESTAMP_URL)) { 'https://timestamp.digicert.com' } else { $env:DNPPV_CODESIGN_TIMESTAMP_URL }),
    [string]$CodeSigningExpectedCommonName = $(if ([string]::IsNullOrWhiteSpace($env:DNPPV_CODESIGN_EXPECTED_CN)) { 'SANYALnet Labs' } else { $env:DNPPV_CODESIGN_EXPECTED_CN }),
    [switch]$RequireCodeSigning
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

function Resolve-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $sdkBinRoots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
        (Join-Path $env:ProgramFiles 'Windows Kits\10\bin')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) }

    foreach ($sdkBinRoot in $sdkBinRoots) {
        foreach ($versionDirectory in Get-ChildItem -LiteralPath $sdkBinRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending) {
            foreach ($architecture in @('x64', 'x86')) {
                $candidate = Join-Path $versionDirectory.FullName "$architecture\signtool.exe"
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    return $candidate
                }
            }
        }
    }

    return $null
}

function New-InstallerLicenseDisplayFile {
    param(
        [Parameter(Mandatory = $true)][string]$SourceLicensePath,
        [Parameter(Mandatory = $true)][string]$DestinationLicensePath
    )

    # The canonical root LICENSE is copied unchanged into the payload. This
    # installer-display copy only joins the warranty paragraph so Inno RichEdit
    # does not render fragments such as "IN" / "NO EVENT" on separate lines.
    if (-not (Test-Path -LiteralPath $SourceLicensePath -PathType Leaf)) {
        throw "Required license file not found: $SourceLicensePath"
    }

    $displayText = Get-Content -Raw -LiteralPath $SourceLicensePath
    $displayText = $displayText -replace "`r`n", "`n"

    $warrantyPattern = '(?ms)^7\. No Warranty\.\s*(.*?)(?=\n\n(?:This license|8\.|\z))'
    $warrantyMatch = [regex]::Match($displayText, $warrantyPattern)
    if ($warrantyMatch.Success) {
        $displayText = [regex]::Replace(
            $displayText,
            $warrantyPattern,
            {
                param($match)
                '7. No Warranty. ' + (([string]$match.Groups[1].Value -split "\n") | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 }) -join ' '
            },
            1)
    }
    else {
        Write-Warning 'Installer license display workaround did not find the warranty paragraph; using the root LICENSE text unchanged.'
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $DestinationLicensePath) | Out-Null
    Set-Content -LiteralPath $DestinationLicensePath -Value ($displayText -replace "`n", "`r`n") -Encoding UTF8BOM
}

function Get-CertificateSubjectCommonName {
    param([string]$Subject)

    foreach ($part in $Subject -split ',') {
        $trimmed = $part.Trim()
        if ($trimmed.StartsWith('CN=', [StringComparison]::OrdinalIgnoreCase)) {
            return $trimmed.Substring(3).Trim()
        }
    }

    return ''
}

function Invoke-InstallerCodeSigning {
    param(
        [Parameter(Mandatory = $true)][string]$SetupPath,
        [string]$CertificateThumbprint,
        [string]$TimestampUrl,
        [string]$ExpectedCommonName,
        [switch]$RequireSigning
    )

    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $message = 'INNO_SETUP_UNSIGNED reason=no-code-signing-thumbprint; set DNPPV_CODESIGN_THUMBPRINT or pass -CodeSigningCertificateThumbprint to produce a trusted SANYALnet Labs publisher prompt.'
        if ($RequireSigning) {
            throw $message
        }

        Write-Warning $message
        return
    }

    if ([string]::IsNullOrWhiteSpace($TimestampUrl)) {
        throw 'Code-signing timestamp URL must be absolute HTTPS and must not be empty.'
    }

    [Uri]$timestampUri = [Uri]'https://timestamp.invalid'
    if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestampUri) -or
        $timestampUri.Scheme -ne [Uri]::UriSchemeHttps) {
        throw "Code-signing timestamp URL must be absolute HTTPS: $TimestampUrl"
    }

    $signTool = Resolve-SignTool
    if ([string]::IsNullOrWhiteSpace($signTool)) {
        $message = 'INNO_SETUP_UNSIGNED reason=signtool-not-found; install Windows SDK SignTool or rerun without signing requirement.'
        if ($RequireSigning) {
            throw $message
        }

        Write-Warning $message
        return
    }

    Write-Step "Signing Inno setup with Authenticode certificate thumbprint $CertificateThumbprint"
    & $signTool sign /fd SHA256 /tr $TimestampUrl /td SHA256 /sha1 $CertificateThumbprint "$SetupPath"
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed with exit code $LASTEXITCODE"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $SetupPath
    if ($signature.Status -ne 'Valid') {
        throw "Signed setup did not verify as valid. Status=$($signature.Status); Signer=$($signature.SignerCertificate.Subject)"
    }
    $normalizedExpectedThumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
    $normalizedActualThumbprint = ($signature.SignerCertificate.Thumbprint -replace '\s', '').ToUpperInvariant()
    if (-not [string]::Equals($normalizedActualThumbprint, $normalizedExpectedThumbprint, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Signed setup thumbprint did not match requested signing certificate. Expected=$normalizedExpectedThumbprint; Actual=$normalizedActualThumbprint"
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedCommonName)) {
        $actualCommonName = Get-CertificateSubjectCommonName -Subject $signature.SignerCertificate.Subject
        if (-not [string]::Equals($actualCommonName, $ExpectedCommonName, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Signed setup signer common name did not match expected common name '$ExpectedCommonName'. Actual signer=$($signature.SignerCertificate.Subject)"
        }
    }

    Write-Step "INNO_SETUP_SIGNED signer=$($signature.SignerCertificate.Subject)"
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

    $versionNode = Select-Xml -LiteralPath $DirectoryBuildPropsPath -XPath '/*[local-name()="Project"]/*[local-name()="PropertyGroup"]/*[local-name()="PortfolioSaverVersion"]' |
        Select-Object -First 1
    $version = if ($null -ne $versionNode) { $versionNode.Node.InnerText } else { $null }
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
$installerLicensePath = Join-Path $innoRoot 'LICENSE-INSTALLER-DISPLAY.txt'
$iconPath = Join-Path $repoRoot 'src\PortfolioSaver.Shared\Assets\Branding\dnppv-icon-rev-3.ico'
$version = Get-PortfolioSaverVersion -DirectoryBuildPropsPath (Join-Path $repoRoot 'Directory.Build.props')
if (-not (Test-Path -LiteralPath $manifestScript -PathType Leaf)) {
    throw "Release manifest generator not found: $manifestScript"
}

if (-not $SkipPublish) {
    Write-Step 'Running canonical safe-temp publish before Inno payload assembly'
    & (Join-Path $repoRoot 'build\publish-safe-temp.ps1') -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier -TimeoutSeconds $PublishTimeoutSeconds
    if (-not $?) {
        throw "publish-safe-temp.ps1 did not complete successfully (exit code $LASTEXITCODE)."
    }
}

$desktopRoot = Join-Path $safeTempRoot 'desktop'
$configRoot = Join-Path $safeTempRoot 'config'
$screensaverRoot = Join-Path $safeTempRoot 'screensaver'
$serverRoot = Join-Path $safeTempRoot 'server'
foreach ($requiredSafeTempRoot in @($desktopRoot, $configRoot, $screensaverRoot, $serverRoot)) {
    if (-not (Test-Path -LiteralPath $requiredSafeTempRoot -PathType Container)) {
        $hint = if ($SkipPublish) {
            'Run without -SkipPublish or run build/publish-safe-temp.ps1 first.'
        }
        else {
            'publish-safe-temp.ps1 did not produce the expected canonical output directories.'
        }
        throw "Safe-temp publish directory missing: $requiredSafeTempRoot. $hint"
    }
}

Remove-Item -LiteralPath $innoRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $payloadRoot,$outputRoot | Out-Null
New-InstallerLicenseDisplayFile -SourceLicensePath $licensePath -DestinationLicensePath $installerLicensePath

Write-Step 'Assembling Inno Program Files payload'
Copy-DirectoryContents -Source $desktopRoot -Destination $payloadRoot
Copy-DirectoryContents -Source $configRoot -Destination $payloadRoot
Copy-DirectoryContents -Source $screensaverRoot -Destination $payloadRoot
Copy-DirectoryContents -Source $serverRoot -Destination (Join-Path $payloadRoot 'YFinanceServer')
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
$manifestPath = Join-Path $payloadRoot 'release-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Manifest generation failed for Inno payload: missing $manifestPath"
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
    "/DLicenseFile=$installerLicensePath",
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

Invoke-InstallerCodeSigning `
    -SetupPath $setupPath `
    -CertificateThumbprint $CodeSigningCertificateThumbprint `
    -TimestampUrl $CodeSigningTimestampUrl `
    -ExpectedCommonName $CodeSigningExpectedCommonName `
    -RequireSigning:$RequireCodeSigning

Write-Step "INNO_SETUP_CREATED setup=$setupPath"
