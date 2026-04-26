param(
    [ValidateSet("auto", "publish-safe-temp", "publish-next", "publish")]
    [string]$PublishSource = "auto",
    [switch]$ResetRuntimeData,
    [switch]$RequireInstaller,
    [switch]$ApplyVmSettings = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoShare = "\\VBOXSVR\codexrepo"
if (-not (Test-Path $repoShare)) {
    throw "Repo share not available: $repoShare"
}

function Get-ExpectedSemanticVersion {
    param([string]$RepoRoot)

    $versionFile = Join-Path $RepoRoot "src\PortfolioSaver.Shared\PortfolioVersion.cs"
    if (-not (Test-Path $versionFile)) {
        return $null
    }

    $semanticLine = Select-String -Path $versionFile -Pattern 'SemanticVersion\s*=\s*"([^"]+)"' -AllMatches | Select-Object -First 1
    if ($null -eq $semanticLine -or $semanticLine.Matches.Count -eq 0) {
        return $null
    }

    return $semanticLine.Matches[0].Groups[1].Value
}

function Get-PublishCandidateInfo {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Candidate
    )

    $configExe = Join-Path $Candidate.Path "config\PortfolioSaver.Config.exe"
    $saverExe = Join-Path $Candidate.Path "screensaver\PortfolioSaver.Screensaver.exe"
    if ((-not (Test-Path $configExe)) -or (-not (Test-Path $saverExe))) {
        return $null
    }

    $configInfo = Get-Item -LiteralPath $configExe
    $saverInfo = Get-Item -LiteralPath $saverExe
    $configManifestPath = Join-Path $Candidate.Path "config\release-manifest.json"
    $saverManifestPath = Join-Path $Candidate.Path "screensaver\release-manifest.json"

    return [pscustomobject]@{
        Name = $Candidate.Name
        Path = $Candidate.Path
        ConfigExe = $configExe
        ScreensaverExe = $saverExe
        ConfigProductVersion = $configInfo.VersionInfo.ProductVersion
        ScreensaverProductVersion = $saverInfo.VersionInfo.ProductVersion
        LastWriteTimeUtc = if ($configInfo.LastWriteTimeUtc -gt $saverInfo.LastWriteTimeUtc) { $configInfo.LastWriteTimeUtc } else { $saverInfo.LastWriteTimeUtc }
        HasConfigManifest = Test-Path $configManifestPath
        HasScreensaverManifest = Test-Path $saverManifestPath
    }
}

$desktopRoot = Join-Path $env:USERPROFILE "Desktop\PortfolioVmUx"
$publishRoot = Join-Path $desktopRoot "publish"
$publishCandidates = @(
    @{ Name = "publish-safe-temp"; Path = (Join-Path $repoShare "build\artifacts\publish-safe-temp") },
    @{ Name = "publish-next"; Path = (Join-Path $repoShare "build\artifacts\publish-next") },
    @{ Name = "publish"; Path = (Join-Path $repoShare "build\artifacts\publish") }
)
$expectedSemanticVersion = Get-ExpectedSemanticVersion -RepoRoot $repoShare
$candidateInfos = $publishCandidates | ForEach-Object { Get-PublishCandidateInfo -Candidate $_ } | Where-Object { $null -ne $_ }
if (@($candidateInfos).Count -eq 0) {
    throw "Could not resolve publish source. Checked: $($publishCandidates.Path -join '; ')"
}

$selectedPublishInfo = $null
if ($PublishSource -eq "auto") {
    $eligibleInfos = $candidateInfos | Where-Object { $_.HasConfigManifest -and $_.HasScreensaverManifest }
    if ($expectedSemanticVersion) {
        $eligibleInfos = $eligibleInfos | Where-Object {
            $_.ConfigProductVersion -like "*$expectedSemanticVersion*" -and
            $_.ScreensaverProductVersion -like "*$expectedSemanticVersion*"
        }
    }

    $selectedPublishInfo = $eligibleInfos | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
}
else {
    $selectedPublishInfo = $candidateInfos | Where-Object { $_.Name -eq $PublishSource } | Select-Object -First 1
}

if ($null -eq $selectedPublishInfo) {
    $details = $candidateInfos | ForEach-Object {
        "{0}: config={1}; saver={2}; manifests=config:{3},saver:{4}" -f $_.Name, $_.ConfigProductVersion, $_.ScreensaverProductVersion, $_.HasConfigManifest, $_.HasScreensaverManifest
    }
    throw "No eligible publish source found for '$PublishSource'. ExpectedSemanticVersion=$expectedSemanticVersion. Candidates: $($details -join ' | ')"
}

if (-not $selectedPublishInfo.HasConfigManifest -or -not $selectedPublishInfo.HasScreensaverManifest) {
    throw "Selected publish source '$($selectedPublishInfo.Name)' is missing release manifests. config=$($selectedPublishInfo.HasConfigManifest) screensaver=$($selectedPublishInfo.HasScreensaverManifest)"
}

if ($expectedSemanticVersion) {
    $configMatches = $selectedPublishInfo.ConfigProductVersion -like "*$expectedSemanticVersion*"
    $saverMatches = $selectedPublishInfo.ScreensaverProductVersion -like "*$expectedSemanticVersion*"
    if (-not ($configMatches -and $saverMatches)) {
        throw "Selected publish source '$($selectedPublishInfo.Name)' is stale. expected=$expectedSemanticVersion config=$($selectedPublishInfo.ConfigProductVersion) screensaver=$($selectedPublishInfo.ScreensaverProductVersion)"
    }
}

$selectedPublish = $publishCandidates | Where-Object { $_.Name -eq $selectedPublishInfo.Name } | Select-Object -First 1

$publishShare = $selectedPublish.Path
$installerShare = Join-Path $repoShare "build\artifacts\PortfolioSaverScreensaverSetup.exe"
$validationScriptShare = Join-Path $repoShare "build\vm\Run-VmUxValidation.ps1"
$exportScriptShare = Join-Path $repoShare "build\vm\Guest-ExportLatestVmUxResult.ps1"
$t039ProbeShare = Join-Path $repoShare "build\vm\Guest-InstallerUninstallProbe.ps1"
$uninstallScriptShare = Join-Path $repoShare "build\installer\Uninstall-PortfolioSaverScreensaver.ps1"
$vmSettingsShare = Join-Path $repoShare "build\vm\vm-settings.json"

New-Item -ItemType Directory -Force -Path $desktopRoot | Out-Null
if (Test-Path $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
foreach ($item in (Get-ChildItem -LiteralPath $publishShare -Force)) {
    $target = Join-Path $publishRoot $item.Name
    if (Test-Path $target -PathType Leaf) {
        Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
    }

    Copy-Item -LiteralPath $item.FullName -Destination $target -Recurse -Force
}
Copy-Item -LiteralPath $validationScriptShare -Destination (Join-Path $desktopRoot "Run-VmUxValidation.ps1") -Force
Copy-Item -LiteralPath $exportScriptShare -Destination (Join-Path $desktopRoot "Guest-ExportLatestVmUxResult.ps1") -Force
Copy-Item -LiteralPath $t039ProbeShare -Destination (Join-Path $desktopRoot "Guest-InstallerUninstallProbe.ps1") -Force
if (Test-Path $installerShare) {
    Copy-Item -LiteralPath $installerShare -Destination (Join-Path $desktopRoot "PortfolioSaverScreensaverSetup.exe") -Force
}
Copy-Item -LiteralPath $uninstallScriptShare -Destination (Join-Path $desktopRoot "Uninstall-PortfolioSaverScreensaver.ps1") -Force

# Force a clean runtime baseline in the guest so UX validation reflects latest code defaults,
# not stale persisted settings/cache from prior runs.
if ($ResetRuntimeData) {
    $roamingData = Join-Path $env:APPDATA "PortfolioSaver"
    $localData = Join-Path $env:LOCALAPPDATA "PortfolioSaver"
    if (Test-Path $roamingData) {
        Remove-Item -LiteralPath $roamingData -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path $localData) {
        Remove-Item -LiteralPath $localData -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($ApplyVmSettings -and (Test-Path $vmSettingsShare)) {
    $appDataRoot = Join-Path $env:APPDATA "PortfolioSaver"
    New-Item -ItemType Directory -Force -Path $appDataRoot | Out-Null
    Copy-Item -LiteralPath $vmSettingsShare -Destination (Join-Path $appDataRoot "settings.json") -Force
}

$required = @(
    (Join-Path $publishRoot "config\PortfolioSaver.Config.exe"),
    (Join-Path $publishRoot "config\release-manifest.json"),
    (Join-Path $publishRoot "screensaver\PortfolioSaver.Screensaver.exe"),
    (Join-Path $publishRoot "screensaver\release-manifest.json"),
    (Join-Path $desktopRoot "Run-VmUxValidation.ps1"),
    (Join-Path $desktopRoot "Guest-ExportLatestVmUxResult.ps1"),
    (Join-Path $desktopRoot "Guest-InstallerUninstallProbe.ps1"),
    (Join-Path $desktopRoot "Uninstall-PortfolioSaverScreensaver.ps1")
)
if ($RequireInstaller) {
    $required += (Join-Path $desktopRoot "PortfolioSaverScreensaverSetup.exe")
}

$missing = $required | Where-Object { -not (Test-Path $_) }
if (@($missing).Count -gt 0) {
    throw "Missing staged files: $($missing -join '; ')"
}

$configExe = Join-Path $publishRoot "config\PortfolioSaver.Config.exe"
$saverExe = Join-Path $publishRoot "screensaver\PortfolioSaver.Screensaver.exe"
$configInfo = Get-Item -LiteralPath $configExe
$saverInfo = Get-Item -LiteralPath $saverExe
$manifest = [ordered]@{
    PreparedAt = (Get-Date).ToString("o")
    GuestUser = $env:USERNAME
    ExpectedSemanticVersion = $expectedSemanticVersion
    PublishSource = $selectedPublish.Name
    PublishShare = $publishShare
    ResetRuntimeData = [bool]$ResetRuntimeData
    ApplyVmSettings = [bool]$ApplyVmSettings
    ConfigExe = @{
        Path = $configExe
        Length = $configInfo.Length
        LastWriteTimeUtc = $configInfo.LastWriteTimeUtc.ToString("o")
        ProductVersion = $configInfo.VersionInfo.ProductVersion
        FileVersion = $configInfo.VersionInfo.FileVersion
    }
    ScreensaverExe = @{
        Path = $saverExe
        Length = $saverInfo.Length
        LastWriteTimeUtc = $saverInfo.LastWriteTimeUtc.ToString("o")
        ProductVersion = $saverInfo.VersionInfo.ProductVersion
        FileVersion = $saverInfo.VersionInfo.FileVersion
    }
}
$manifestPath = Join-Path $desktopRoot "staged-build.json"
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
try {
    $hostManifestRoot = Join-Path $repoShare "build\vm\artifacts\staged-builds"
    New-Item -ItemType Directory -Force -Path $hostManifestRoot | Out-Null
    $hostManifestPath = Join-Path $hostManifestRoot ("staged-build-{0:yyyyMMdd-HHmmss}.json" -f (Get-Date))
    Copy-Item -LiteralPath $manifestPath -Destination $hostManifestPath -Force
    Write-Output ("VMUX_STAGE_MANIFEST=" + $hostManifestPath)
}
catch {
    Write-Output ("VMUX_STAGE_MANIFEST_ERROR=" + $_.Exception.Message)
}
Write-Output ("VMUX_PREP_DONE source={0} saverBytes={1}" -f $selectedPublish.Name, $saverInfo.Length)
