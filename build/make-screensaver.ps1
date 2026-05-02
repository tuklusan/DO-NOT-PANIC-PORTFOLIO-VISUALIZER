param(
    [string]$Configuration = "Release"
)

$publishDir = Join-Path $PSScriptRoot "..\src\PortfolioSaver.Screensaver\bin\$Configuration\net10.0-windows\win-x64\publish"
$exePath = Join-Path $publishDir "PortfolioSaver.Screensaver.exe"
$scrPath = Join-Path $publishDir "PortfolioSaver.Screensaver.scr"

if (-not (Test-Path $exePath)) {
    throw "Published screensaver executable not found. Run publish.ps1 first."
}

Copy-Item $exePath $scrPath -Force
Write-Host "Created $scrPath"
