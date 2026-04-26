Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoShare = "\\VBOXSVR\codexrepo"
if (-not (Test-Path $repoShare)) {
    throw "Repo share not available: $repoShare"
}

$resultsRoot = Join-Path $env:USERPROFILE "Desktop\PortfolioVmUx\results"
if (-not (Test-Path $resultsRoot)) {
    throw "No VM UX results root: $resultsRoot"
}

$latest = Get-ChildItem -LiteralPath $resultsRoot -Directory |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $latest) {
    throw "No VM UX result folder found under $resultsRoot"
}

$summaryPath = Join-Path $latest.FullName "vm-ux-summary.json"
if (-not (Test-Path $summaryPath)) {
    throw "Missing summary file in latest result folder: $summaryPath"
}

$hostRoot = Join-Path $repoShare "build\vm\artifacts\vm-results"
$hostTarget = Join-Path $hostRoot $latest.Name
New-Item -ItemType Directory -Force -Path $hostRoot | Out-Null
if (Test-Path $hostTarget) {
    Remove-Item -LiteralPath $hostTarget -Recurse -Force -ErrorAction SilentlyContinue
}

Copy-Item -LiteralPath $latest.FullName -Destination $hostTarget -Recurse -Force

Write-Output ("LATEST_RESULTS=" + $latest.FullName)
Write-Output ("LATEST_SUMMARY=" + $summaryPath)
Write-Output ("HOST_RESULT_DIR=" + $hostTarget)
