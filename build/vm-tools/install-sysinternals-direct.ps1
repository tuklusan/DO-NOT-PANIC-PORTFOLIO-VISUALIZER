$ErrorActionPreference = 'Stop'

$targetDir = 'C:\Tools\Sysinternals'
$zipPath = 'C:\Temp\SysinternalsSuite.zip'
$url = 'https://download.sysinternals.com/files/SysinternalsSuite.zip'

New-Item -ItemType Directory -Path 'C:\Temp' -Force | Out-Null
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $zipPath
Expand-Archive -LiteralPath $zipPath -DestinationPath $targetDir -Force

Write-Host "Installed Sysinternals to $targetDir"
