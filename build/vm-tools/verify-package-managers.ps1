$ErrorActionPreference = 'Stop'

$choco = 'C:\ProgramData\chocolatey\bin\choco.exe'
$scoop = Join-Path $env:USERPROFILE 'scoop\shims\scoop.cmd'

if (Test-Path -LiteralPath $choco) {
    Write-Host "ChocolateyVersion=$(& $choco --version)"
} else {
    Write-Host 'ChocolateyVersion=NOT_FOUND'
}

if (Test-Path -LiteralPath $scoop) {
    Write-Host "ScoopVersion=$(& $scoop --version)"
} else {
    Write-Host 'ScoopVersion=NOT_FOUND'
}
