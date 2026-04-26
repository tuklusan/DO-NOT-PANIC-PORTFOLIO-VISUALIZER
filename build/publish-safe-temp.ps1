param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [int]$TimeoutSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ("[{0}] {1}" -f (Get-Date).ToString("HH:mm:ss"), $Message)
}

function Resolve-DotNetCli {
    $preferred = Join-Path $env:USERPROFILE ".dotnet8\dotnet.exe"
    if (Test-Path $preferred) {
        return $preferred
    }

    return "dotnet"
}

function Get-RelativePathLegacy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $normalizedBase = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $baseUri = New-Object System.Uri($normalizedBase)
    $targetUri = New-Object System.Uri([System.IO.Path]::GetFullPath($TargetPath))
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

function Test-Deadline {
    param(
        [Parameter(Mandatory = $true)]
        [datetime]$Deadline,
        [Parameter(Mandatory = $true)]
        [string]$NextStep
    )

    if ((Get-Date) -ge $Deadline) {
        throw "Global timeout reached before step: $NextStep"
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$tempRoot = Join-Path $env:TEMP "PortfolioSaverPublishWorkspace"
$manifestScript = Join-Path $PSScriptRoot "generate-release-manifest.ps1"
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)

$publishRoot = Join-Path $repoRoot "build\artifacts\publish-safe-temp"
$screensaverOut = Join-Path $publishRoot "screensaver"
$configOut = Join-Path $publishRoot "config"
$dotnetCli = Resolve-DotNetCli

Write-Step "Preparing temp publish workspace: $tempRoot"
if (Test-Path $tempRoot) {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

Copy-Item -LiteralPath (Join-Path $repoRoot "PortfolioScreensaver.sln") -Destination $tempRoot -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "Directory.Build.props") -Destination $tempRoot -Force

$srcTarget = Join-Path $tempRoot "src"
$null = robocopy (Join-Path $repoRoot "src") $srcTarget /E /XD bin obj
$srcCopyExit = $LASTEXITCODE
if ($srcCopyExit -gt 7) {
    throw "Workspace mirror failed. robocopy exit: src=$srcCopyExit"
}

Write-Step "Seeding local obj restore assets"
$assetPatterns = @(
    "project.assets.json",
    "project.nuget.cache",
    "*.nuget.dgspec.json",
    "*.nuget.g.props",
    "*.nuget.g.targets"
)

$assetFiles = Get-ChildItem -Path (Join-Path $repoRoot "src") -Recurse -File | Where-Object {
    $name = $_.Name
    foreach ($pattern in $assetPatterns) {
        if ($name -like $pattern) { return $true }
    }
    return $false
}

foreach ($assetFile in $assetFiles) {
    $relativePath = Get-RelativePathLegacy -BasePath $repoRoot -TargetPath $assetFile.FullName
    $destinationPath = Join-Path $tempRoot $relativePath
    $destinationDir = Split-Path -Path $destinationPath -Parent
    New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null
    Copy-Item -LiteralPath $assetFile.FullName -Destination $destinationPath -Force
}

if (Test-Path $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path $screensaverOut,$configOut | Out-Null

$screensaverProject = ".\src\PortfolioSaver.Screensaver\PortfolioSaver.Screensaver.csproj"
$configProject = ".\src\PortfolioSaver.Config\PortfolioSaver.Config.csproj"

Push-Location $tempRoot
try {
    Test-Deadline -Deadline $deadline -NextStep "restore screensaver"
    Write-Step "Restoring screensaver project with $dotnetCli"
    & $dotnetCli restore $screensaverProject -r $RuntimeIdentifier --disable-parallel --ignore-failed-sources -m:1 -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Restore failed for screensaver" }

    Test-Deadline -Deadline $deadline -NextStep "restore config"
    Write-Step "Restoring config project"
    & $dotnetCli restore $configProject -r $RuntimeIdentifier --disable-parallel --ignore-failed-sources -m:1 -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Restore failed for config" }

    Test-Deadline -Deadline $deadline -NextStep "publish screensaver"
    Write-Step "Publishing screensaver"
    & $dotnetCli publish $screensaverProject -c $Configuration -r $RuntimeIdentifier --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --no-restore --disable-parallel -m:1 -o $screensaverOut -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for screensaver" }

    Test-Deadline -Deadline $deadline -NextStep "publish config"
    Write-Step "Publishing config app"
    & $dotnetCli publish $configProject -c $Configuration -r $RuntimeIdentifier --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --no-restore --disable-parallel -m:1 -o $configOut -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for config" }
}
finally {
    Pop-Location
}

Test-Deadline -Deadline $deadline -NextStep "manifest generation"
Write-Step "Generating release manifests"
& $manifestScript -PublishDir $screensaverOut
if ($LASTEXITCODE -ne 0) {
    throw "Manifest generation failed for $screensaverOut"
}
& $manifestScript -PublishDir $configOut
if ($LASTEXITCODE -ne 0) {
    throw "Manifest generation failed for $configOut"
}

Write-Step "SAFE_TEMP_PUBLISH_OK output=$publishRoot"
