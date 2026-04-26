[CmdletBinding()]
param(
    [ValidateSet("Installed", "Uninstalled")]
    [string]$ExpectedState = "Installed"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scrPath = Join-Path $env:WINDIR "System32\PortfolioSaver.Screensaver.scr"
$manifestPath = Join-Path $env:ProgramData "PortfolioSaverScreensaver\installed-files.txt"
$uninstallKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PortfolioSaverScreensaver"

$checks = @(
    [pscustomobject]@{
        Name = "Screensaver file"
        Present = Test-Path $scrPath
        Details = $scrPath
    },
    [pscustomobject]@{
        Name = "Install manifest"
        Present = Test-Path $manifestPath
        Details = $manifestPath
    },
    [pscustomobject]@{
        Name = "Uninstall registry key"
        Present = Test-Path $uninstallKey
        Details = $uninstallKey
    }
)

$expectedPresent = $ExpectedState -eq "Installed"
$failedChecks = $checks | Where-Object { $_.Present -ne $expectedPresent }

Write-Host ""
Write-Host "PortfolioSaver expected state: $ExpectedState"
Write-Host ""

foreach ($check in $checks) {
    $status = if ($check.Present) { "Present" } else { "Missing" }
    Write-Host ("{0,-24} {1,-8} {2}" -f $check.Name, $status, $check.Details)
}

Write-Host ""
if ($failedChecks.Count -eq 0) {
    Write-Host "Validation passed."
    exit 0
}

Write-Host "Validation failed."
exit 1
