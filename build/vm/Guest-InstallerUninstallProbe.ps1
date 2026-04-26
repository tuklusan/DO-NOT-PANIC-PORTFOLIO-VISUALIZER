Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoShare = "\\VBOXSVR\codexrepo"
if (-not (Test-Path $repoShare)) {
    throw "Repo share not available: $repoShare"
}

$result = [ordered]@{
    startedAt = (Get-Date).ToString("o")
    installTriggered = $false
    installDetected = $false
    uninstallTriggered = $false
    uninstallDetected = $false
    appDataCleanupVerified = $false
    status = "UNKNOWN"
    notes = @()
}

$systemScr = Join-Path $env:WINDIR "System32\PortfolioSaver.Screensaver.scr"
$configExe = Join-Path $env:WINDIR "System32\PortfolioSaver.Config.exe"
$programDataRoot = Join-Path $env:ProgramData "PortfolioSaverScreensaver"
$manifestPath = Join-Path $programDataRoot "installed-files.txt"

$localRoot = Join-Path $env:LOCALAPPDATA "PortfolioSaver"
$backgroundCache = Join-Path $localRoot "Backgrounds\ExchangePhotoCache"
$historyCache = Join-Path $localRoot "Caches\History"
$symbolProfile = Join-Path $localRoot "symbol-profiles.json"
$providerLedger = Join-Path $localRoot "provider-query-usage.json"

New-Item -ItemType Directory -Force -Path $backgroundCache | Out-Null
New-Item -ItemType Directory -Force -Path $historyCache | Out-Null
Set-Content -LiteralPath (Join-Path $backgroundCache "probe.txt") -Value "probe" -Encoding ASCII
Set-Content -LiteralPath (Join-Path $historyCache "probe.txt") -Value "probe" -Encoding ASCII
Set-Content -LiteralPath $symbolProfile -Value "{}" -Encoding ASCII
Set-Content -LiteralPath $providerLedger -Value "{}" -Encoding ASCII
$result.notes += "Seeded LocalAppData probe cache files."

$installScript = Join-Path $repoShare "build\artifacts\installer-stage\Install-PortfolioSaverScreensaver.ps1"
$stagingRoot = Join-Path $env:TEMP "PortfolioSaverScreensaverInstaller-Probe"

if (-not (Test-Path $installScript)) {
    throw "Missing install script: $installScript"
}

$result.installTriggered = $true
& powershell -NoProfile -ExecutionPolicy Bypass -File $installScript -StagingRoot $stagingRoot
Start-Sleep -Seconds 20

$installed = (Test-Path $systemScr) -and (Test-Path $configExe) -and (Test-Path $manifestPath)
$result.installDetected = $installed

if (-not $installed) {
    $result.status = "BLOCKED"
    $result.notes += "Install artifacts not detected after trigger. Likely UAC elevation pending/manual in guest."
}
else {
    $result.notes += "Install artifacts detected in System32/ProgramData."
    $uninstallScript = Join-Path $repoShare "build\installer\Uninstall-PortfolioSaverScreensaver.ps1"
    if (-not (Test-Path $uninstallScript)) {
        $uninstallScript = Join-Path $programDataRoot "Uninstall-PortfolioSaverScreensaver.ps1"
    }
    if (-not (Test-Path $uninstallScript)) {
        throw "Missing uninstall script: $uninstallScript"
    }

    $result.uninstallTriggered = $true
    & powershell -NoProfile -ExecutionPolicy Bypass -File $uninstallScript
    Start-Sleep -Seconds 20

    $stillInstalled = (Test-Path $systemScr) -or (Test-Path $configExe) -or (Test-Path $manifestPath)
    $result.uninstallDetected = -not $stillInstalled
    $result.appDataCleanupVerified =
        (-not (Test-Path $backgroundCache)) -and
        (-not (Test-Path $historyCache)) -and
        (-not (Test-Path $symbolProfile)) -and
        (-not (Test-Path $providerLedger))

    if ($result.uninstallDetected -and $result.appDataCleanupVerified) {
        $result.status = "PASS"
        $result.notes += "Uninstall removed installed files and LocalAppData caches."
    }
    else {
        $result.status = "FAIL"
        $result.notes += "Uninstall probe found residual files/caches."
    }
}

$result.finishedAt = (Get-Date).ToString("o")
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$guestOutDir = Join-Path $env:USERPROFILE "Desktop\PortfolioVmUx\results"
New-Item -ItemType Directory -Force -Path $guestOutDir | Out-Null
$guestOutPath = Join-Path $guestOutDir ("t039-probe-" + $stamp + ".json")
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $guestOutPath -Encoding UTF8

$hostOutDir = Join-Path $repoShare "build\vm\artifacts\vm-results\t039-probe"
New-Item -ItemType Directory -Force -Path $hostOutDir | Out-Null
Copy-Item -LiteralPath $guestOutPath -Destination (Join-Path $hostOutDir ("t039-probe-" + $stamp + ".json")) -Force

Write-Output ("T039_RESULT=" + $result.status)
Write-Output ("T039_GUEST_JSON=" + $guestOutPath)
