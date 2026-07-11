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
  [ValidateRange(5, 1440)]
  [int]$DurationMinutes = 30
)

. "$PSScriptRoot\..\vm\VmSshCommon.ps1"
$ErrorActionPreference='Stop'
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$root='C:\vmharness\portfolio-saver'
$installerLocal=(Resolve-Path (Join-Path $repoRoot 'build\artifacts\inno\output\DoNotPanicPortfolioVisualizerSetup-1.0.exe')).Path
$resultName='installed-soak-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$localParent=(Resolve-Path (Join-Path $repoRoot 'build\validation\artifacts')).Path
$localResultRoot=Join-Path $localParent $resultName
$durationSeconds=[int]($DurationMinutes * 60)
$lastCaptureDelaySeconds=[Math]::Max(30, $durationSeconds - 45)
$captureDelays=0..6 | ForEach-Object { [int][Math]::Round(30 + ($_ * (($lastCaptureDelaySeconds - 30) / 6.0))) } | Sort-Object -Unique
$captureDelayText=($captureDelays -join ',')
$invokeTimeoutSeconds=[Math]::Max(600, $durationSeconds + 300)
$bundle=New-VmSshSessionBundle
try {
  $userParts=Get-VmSshCredentialPartsFromEnv
  $remoteUser=$userParts.UserName
  $remoteInstallerDir="$root\installers"
  $remoteInstaller="$remoteInstallerDir\DoNotPanicPortfolioVisualizerSetup-1.0.exe"
  $remoteResult="$root\results\$resultName"
  $cleanup = @"
`$ErrorActionPreference='Stop'
Get-Process PortfolioSaver.Desktop,PortfolioSaver.Config,YFinance.NET.Server -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
schtasks /Delete /TN 'DnppvInstalledSoakDesktop' /F *>`$null 2>&1
`$uninstallKeys = @('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')
`$uninstaller = Get-ItemProperty `$uninstallKeys -ErrorAction SilentlyContinue | Where-Object { `$_.DisplayName -like '*DO NOT PANIC PORTFOLIO VISUALIZER*' } | Select-Object -First 1 -ExpandProperty UninstallString
if (`$uninstaller) {
  `$exe = `$uninstaller.Trim('"')
  if (Test-Path -LiteralPath `$exe) { Start-Process -FilePath `$exe -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait }
}
Remove-Item -LiteralPath '$root\results' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath '$root\installers' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "`$env:LOCALAPPDATA\DoNotPanicPortfolioVisualizer" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path '$remoteInstallerDir' | Out-Null
New-Item -ItemType Directory -Force -Path '$remoteResult' | Out-Null
"@
  Invoke-VmPwshCommand -Bundle $bundle -Command $cleanup -TimeOutSeconds 180 | Out-Null
  Send-VmItem -Bundle $bundle -LocalPath $installerLocal -RemoteDestination $remoteInstallerDir
  $localRemoteScript=Join-Path $env:TEMP "RunInstalledSoak.ps1"
  $remoteScriptContent = @"
`$ErrorActionPreference='Stop'
`$installer='$remoteInstaller'
`$result='$remoteResult'
`$captureDir=Join-Path `$result 'scene-captures'
`$metricsPath=Join-Path `$result 'resource-samples.csv'
New-Item -ItemType Directory -Force -Path `$captureDir | Out-Null
function Write-ResourceSample([string]`$phase) {
  `$timestamp=(Get-Date).ToUniversalTime().ToString('o')
  `$processes=Get-Process PortfolioSaver.Desktop,YFinance.NET.Server -ErrorAction SilentlyContinue
  foreach (`$process in `$processes) {
    [pscustomobject]@{
      TimestampUtc=`$timestamp
      Phase=`$phase
      ProcessName=`$process.ProcessName
      Id=`$process.Id
      CpuSeconds=[math]::Round([double]`$process.CPU,3)
      WorkingSetMB=[math]::Round(`$process.WorkingSet64/1MB,1)
      PrivateMemoryMB=[math]::Round(`$process.PrivateMemorySize64/1MB,1)
      Threads=`$process.Threads.Count
      Handles=`$process.HandleCount
    } | Export-Csv -LiteralPath `$metricsPath -Append -NoTypeInformation
  }
}
Start-Process -FilePath `$installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait
`$uninstallKeys = @('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')
`$installRecord = Get-ItemProperty `$uninstallKeys -ErrorAction SilentlyContinue | Where-Object { `$_.DisplayName -like '*DO NOT PANIC PORTFOLIO VISUALIZER*' } | Select-Object -First 1
`$installRoot = if (`$installRecord -and `$installRecord.InstallLocation) { `$installRecord.InstallLocation } else { 'C:\Program Files\SANYALnet Labs\DoNotPanicPortfolioVisualizer' }
`$desktopExe=Join-Path `$installRoot 'PortfolioSaver.Desktop.exe'
`$configExe=Join-Path `$installRoot 'PortfolioSaver.Config.exe'
if (!(Test-Path -LiteralPath `$desktopExe)) { throw "Desktop exe missing: `$desktopExe" }
if (!(Test-Path -LiteralPath `$configExe)) { throw "Config exe missing: `$configExe" }
`$summary=[ordered]@{
  ResultName='$resultName'
  ResultDir=`$result
  InstalledDesktopExe=`$desktopExe
  InstalledConfigExe=`$configExe
  InstalledAtUtc=(Get-Date).ToUniversalTime().ToString('o')
  DesktopProcessDiedEarly=`$false
  DesktopProcessAliveAtEndBeforeStop=`$false
  DurationSeconds=$durationSeconds
}
`$taskName='DnppvInstalledSoakDesktop'
`$taskTime=(Get-Date).AddMinutes(1).ToString('HH:mm')
`$launchCmd=Join-Path `$result 'launch-installed-soak.cmd'
`$launchLines=@(
  '@echo off',
  "set PORTFOLIOSAVER_CAPTURE_DIR=`$captureDir",
  'set PORTFOLIOSAVER_CAPTURE_STEM=installed-soak',
  "set PORTFOLIOSAVER_CAPTURE_DELAYS=$captureDelayText",
  "cd /d ""`$installRoot""",
  "start """" ""`$desktopExe"""
)
Set-Content -LiteralPath `$launchCmd -Value `$launchLines -Encoding ASCII
`$taskAction='cmd.exe /c ""' + `$launchCmd + '""'
# /IT is intentional: the installed-soak lane validates real GUI rendering in the logged-on test user's desktop session.
schtasks /Create /TN `$taskName /TR `$taskAction /SC ONCE /ST `$taskTime /IT /RU '$remoteUser' /F | Out-Null
schtasks /Run /TN `$taskName | Out-Null
`$deadline=(Get-Date).AddSeconds($durationSeconds)
`$launchDeadline=(Get-Date).AddSeconds(90)
while ((Get-Date) -lt `$launchDeadline -and -not (Get-Process PortfolioSaver.Desktop -ErrorAction SilentlyContinue)) { Start-Sleep -Seconds 2 }
if (-not (Get-Process PortfolioSaver.Desktop -ErrorAction SilentlyContinue)) { throw 'Desktop process did not launch.' }
# The task is only a logged-on desktop-session launch bridge. Delete it as soon as
# the process exists so the scheduled /ST time cannot trigger a second launch.
schtasks /Delete /TN `$taskName /F *>`$null 2>&1
Write-ResourceSample 'startup'
`$nextEvidenceAt=(Get-Date).AddMinutes(5)
`$evidenceIndex=0
while ((Get-Date) -lt `$deadline) {
  if (-not (Get-Process PortfolioSaver.Desktop -ErrorAction SilentlyContinue)) { `$summary.DesktopProcessDiedEarly=`$true; break }
  if ((Get-Date) -ge `$nextEvidenceAt) {
    `$evidenceIndex++
    Write-ResourceSample ("soak-{0:00}" -f `$evidenceIndex)
    `$nextEvidenceAt=(Get-Date).AddMinutes(5)
  }
  Start-Sleep -Seconds 5
}
`$summary.DesktopProcessAliveAtEndBeforeStop = [bool](Get-Process PortfolioSaver.Desktop -ErrorAction SilentlyContinue)
Write-ResourceSample 'before-stop'
Get-Process PortfolioSaver.Desktop,PortfolioSaver.Config,YFinance.NET.Server -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3
  `$traceSource="`$env:LOCALAPPDATA\DoNotPanicPortfolioVisualizer\Trace"
  `$traceDest=Join-Path `$result 'trace'
  if (Test-Path -LiteralPath `$traceSource) { Copy-Item -LiteralPath `$traceSource -Destination `$traceDest -Recurse -Force }
  `$summary.CompletedAtUtc=(Get-Date).ToUniversalTime().ToString('o')
  `$summary.TraceFiles=@(if (Test-Path -LiteralPath `$traceDest) { Get-ChildItem -LiteralPath `$traceDest -File | ForEach-Object FullName })
  `$summary.ScreenshotFiles=@(Get-ChildItem -LiteralPath `$captureDir -File -Filter '*.png' -ErrorAction SilentlyContinue | ForEach-Object FullName)
  `$summary.ResourceSampleFile=if (Test-Path -LiteralPath `$metricsPath) { `$metricsPath } else { `$null }
  `$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path `$result 'summary.json') -Encoding UTF8
  schtasks /Delete /TN `$taskName /F *>`$null 2>&1
"@
  Set-Content -LiteralPath $localRemoteScript -Value $remoteScriptContent -Encoding UTF8
  $remoteScript="$root\RunInstalledSoak.ps1"
  Send-VmItem -Bundle $bundle -LocalPath $localRemoteScript -RemoteDestination $root
  Invoke-VmRawCommand -Bundle $bundle -Command "pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$remoteScript`"" -TimeOutSeconds $invokeTimeoutSeconds | Out-Null
  New-Item -ItemType Directory -Force -Path $localParent | Out-Null
  Receive-VmItem -Bundle $bundle -RemotePath $remoteResult -LocalDestination $localParent
  $summaryPath=Join-Path $localResultRoot 'summary.json'
  if (Test-Path -LiteralPath $summaryPath) {
    $summary=Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
    $screenshotCount=@($summary.ScreenshotFiles).Count
    # The app schedules seven scene captures; require five so visual evidence cannot pass on a token capture.
    if ($screenshotCount -lt 5) { throw "Installed soak did not capture sufficient screenshot evidence. Captured screenshots: $screenshotCount. See $localResultRoot" }
    if (-not $summary.ResourceSampleFile) { throw "Installed soak did not capture resource-sample evidence. See $localResultRoot" }
    $resourceSampleFileName = Split-Path -Leaf ([string]$summary.ResourceSampleFile)
    $localResourceSamplePath = Join-Path $localResultRoot $resourceSampleFileName
    $resourceSamples = @(Import-Csv -LiteralPath $localResourceSamplePath | Where-Object { $_.ProcessName -eq 'PortfolioSaver.Desktop' })
    if ($resourceSamples.Count -lt 2) { throw "Installed soak did not capture enough desktop resource samples. See $localResultRoot" }
    $maxPrivateMemoryMb = ($resourceSamples | Measure-Object -Property PrivateMemoryMB -Maximum).Maximum
    $maxThreads = ($resourceSamples | Measure-Object -Property Threads -Maximum).Maximum
    if ([double]$maxPrivateMemoryMb -gt 1024) { throw "Installed soak exceeded desktop private-memory guardrail: $maxPrivateMemoryMb MB. See $localResultRoot" }
    if ([int]$maxThreads -gt 64) { throw "Installed soak exceeded desktop thread-count guardrail: $maxThreads. See $localResultRoot" }
  } else {
    throw "Installed soak did not produce summary.json. See $localResultRoot"
  }
  Write-Host "LOCAL_RESULT=$localResultRoot"
}
finally {
  try { Invoke-VmPwshCommand -Bundle $bundle -Command "Get-Process PortfolioSaver.Desktop,PortfolioSaver.Config,YFinance.NET.Server -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; schtasks /Delete /TN 'DnppvInstalledSoakDesktop' /F *>`$null 2>&1" -TimeOutSeconds 60 | Out-Null } catch {}
  Remove-VmSshSessionBundle -Bundle $bundle
}

