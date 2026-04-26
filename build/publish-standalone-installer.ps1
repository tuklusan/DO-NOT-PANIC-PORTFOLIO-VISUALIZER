param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-MSBuildPath {
    $candidates = New-Object System.Collections.Generic.List[string]

    $programFilesX86 = [Environment]::GetFolderPath("ProgramFilesX86")
    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $vswherePath = Join-Path $programFilesX86 "Microsoft Visual Studio\Installer\vswhere.exe"
        if (Test-Path $vswherePath) {
            $installationPath = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
            if (-not [string]::IsNullOrWhiteSpace($installationPath)) {
                $candidates.Add((Join-Path $installationPath "MSBuild\Current\Bin\MSBuild.exe")) | Out-Null
            }
        }
    }

    $candidates.Add("D:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe") | Out-Null
    $candidates.Add("C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe") | Out-Null
    $candidates.Add("C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe") | Out-Null
    $candidates.Add("C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe") | Out-Null

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "MSBuild.exe was not found in the local Visual Studio installation."
}

function Publish-SelfContainedApp {
    param(
        [Parameter(Mandatory = $true)]
        [string]$MsBuildPath,

        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $true)]
        [string]$PublishDir,

        [Parameter(Mandatory = $true)]
        [string]$ScratchRoot
    )

    $intermediateRoot = Join-Path $ScratchRoot "obj"
    $outputRoot = Join-Path $ScratchRoot "bin"
    New-Item -ItemType Directory -Force -Path $PublishDir,$intermediateRoot,$outputRoot | Out-Null

    & $MsBuildPath $ProjectPath `
        /restore `
        /t:Publish `
        /p:Configuration=$Configuration `
        /p:RuntimeIdentifier=$RuntimeIdentifier `
        /p:SelfContained=true `
        /p:PublishSingleFile=true `
        /p:EnableCompressionInSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:PublishDir=$PublishDir `
        /p:PortfolioSaverInstallerBaseIntermediateRoot=$intermediateRoot `
        /p:PortfolioSaverInstallerBaseOutputRoot=$outputRoot

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $ProjectPath"
    }
}

function Publish-InstallerBootstrap {
    param(
        [Parameter(Mandatory = $true)]
        [string]$MsBuildPath,

        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $true)]
        [string]$PublishDir,

        [Parameter(Mandatory = $true)]
        [string]$ScratchRoot,

        [Parameter(Mandatory = $true)]
        [string]$PayloadZipPath
    )

    $intermediateRoot = Join-Path $ScratchRoot "obj"
    $outputRoot = Join-Path $ScratchRoot "bin"
    New-Item -ItemType Directory -Force -Path $PublishDir,$intermediateRoot,$outputRoot | Out-Null

    & $MsBuildPath $ProjectPath `
        /restore `
        /t:Publish `
        /p:Configuration=$Configuration `
        /p:RuntimeIdentifier=$RuntimeIdentifier `
        /p:SelfContained=true `
        /p:PublishSingleFile=true `
        /p:EnableCompressionInSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:PublishDir=$PublishDir `
        /p:InstallerPayloadPath=$PayloadZipPath `
        /p:PortfolioSaverInstallerBaseIntermediateRoot=$intermediateRoot `
        /p:PortfolioSaverInstallerBaseOutputRoot=$outputRoot

    if ($LASTEXITCODE -ne 0) {
        throw "Installer publish failed for $ProjectPath"
    }
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$artifactsRoot = Join-Path $root "build\artifacts"
$publishRoot = Join-Path $artifactsRoot "publish"
$screensaverPublishDir = Join-Path $publishRoot "screensaver"
$configPublishDir = Join-Path $publishRoot "config"
$installerPublishDir = Join-Path $publishRoot "installer"
$scratchRoot = Join-Path $artifactsRoot "scratch"
$screensaverScratchRoot = Join-Path $scratchRoot "screensaver"
$configScratchRoot = Join-Path $scratchRoot "config"
$installerScratchRoot = Join-Path $scratchRoot "installer"
$stageRoot = Join-Path $artifactsRoot "installer-stage"
$payloadRoot = Join-Path $stageRoot "payload"
$payloadZip = Join-Path $artifactsRoot "PortfolioSaverInstallerPayload.zip"
$installerRoot = Join-Path $root "build\installer"
$outputInstaller = Join-Path $artifactsRoot "PortfolioSaverScreensaverSetup.exe"
$msbuildPath = Get-MSBuildPath
$manifestScript = Join-Path $PSScriptRoot "generate-release-manifest.ps1"

Remove-Item -LiteralPath $screensaverPublishDir,$configPublishDir,$installerPublishDir,$scratchRoot,$stageRoot,$payloadZip,$outputInstaller -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $screensaverPublishDir,$configPublishDir,$installerPublishDir,$payloadRoot,$artifactsRoot,$screensaverScratchRoot,$configScratchRoot,$installerScratchRoot | Out-Null

$screensaverProject = Join-Path $root "src\PortfolioSaver.Screensaver\PortfolioSaver.Screensaver.csproj"
$configProject = Join-Path $root "src\PortfolioSaver.Config\PortfolioSaver.Config.csproj"
$installerProject = Join-Path $root "src\PortfolioSaver.Installer\PortfolioSaver.Installer.csproj"

Write-Host "Publishing screensaver with $msbuildPath..."
Publish-SelfContainedApp `
    -MsBuildPath $msbuildPath `
    -ProjectPath $screensaverProject `
    -PublishDir $screensaverPublishDir `
    -ScratchRoot $screensaverScratchRoot
& $manifestScript -PublishDir $screensaverPublishDir
if ($LASTEXITCODE -ne 0) {
    throw "Manifest generation failed for $screensaverPublishDir"
}

Write-Host "Publishing config app..."
Publish-SelfContainedApp `
    -MsBuildPath $msbuildPath `
    -ProjectPath $configProject `
    -PublishDir $configPublishDir `
    -ScratchRoot $configScratchRoot
& $manifestScript -PublishDir $configPublishDir
if ($LASTEXITCODE -ne 0) {
    throw "Manifest generation failed for $configPublishDir"
}

$screensaverExe = Join-Path $screensaverPublishDir "PortfolioSaver.Screensaver.exe"
$screensaverScr = Join-Path $payloadRoot "PortfolioSaver.Screensaver.scr"
if (-not (Test-Path $screensaverExe)) {
    throw "Published screensaver executable not found: $screensaverExe"
}

Copy-Item -LiteralPath $screensaverExe -Destination $screensaverScr -Force

$configExe = Join-Path $configPublishDir "PortfolioSaver.Config.exe"
if (-not (Test-Path $configExe)) {
    throw "Published config executable not found: $configExe"
}
Copy-Item -LiteralPath $configExe -Destination (Join-Path $payloadRoot "PortfolioSaver.Config.exe") -Force

$sampleAssets = Join-Path $screensaverPublishDir "Assets"
if (Test-Path $sampleAssets) {
    Copy-Item -LiteralPath $sampleAssets -Destination (Join-Path $payloadRoot "Assets") -Recurse -Force
}

# Create a payload-level manifest so every launched executable validates
# the exact installed file set before startup.
& $manifestScript -PublishDir $payloadRoot
if ($LASTEXITCODE -ne 0) {
    throw "Manifest generation failed for installer payload: $payloadRoot"
}

Copy-Item -LiteralPath (Join-Path $installerRoot "Install-PortfolioSaverScreensaver.ps1") -Destination (Join-Path $stageRoot "Install-PortfolioSaverScreensaver.ps1") -Force
Copy-Item -LiteralPath (Join-Path $installerRoot "Uninstall-PortfolioSaverScreensaver.ps1") -Destination (Join-Path $stageRoot "Uninstall-PortfolioSaverScreensaver.ps1") -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stageRoot, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "Publishing standalone installer..."
Publish-InstallerBootstrap `
    -MsBuildPath $msbuildPath `
    -ProjectPath $installerProject `
    -PublishDir $installerPublishDir `
    -ScratchRoot $installerScratchRoot `
    -PayloadZipPath $payloadZip

$builtInstaller = Join-Path $installerPublishDir "PortfolioSaverScreensaverSetup.exe"
if (-not (Test-Path $builtInstaller)) {
    throw "Published installer executable not found: $builtInstaller"
}

$finalInstallerPath = $outputInstaller
try {
    Copy-Item -LiteralPath $builtInstaller -Destination $outputInstaller -Force
}
catch [System.IO.IOException] {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $finalInstallerPath = Join-Path $artifactsRoot "PortfolioSaverScreensaverSetup-$timestamp.exe"
    Copy-Item -LiteralPath $builtInstaller -Destination $finalInstallerPath -Force
}

if (-not (Test-Path $finalInstallerPath)) {
    throw "Installer was not created: $finalInstallerPath"
}

Write-Host "Standalone installer created at:"
Write-Host $finalInstallerPath
