$ErrorActionPreference = 'Stop'

Set-ExecutionPolicy Bypass -Scope Process -Force
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072

$chocoInstallScript = (New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1')
Invoke-Expression $chocoInstallScript

$env:Path += ';C:\ProgramData\chocolatey\bin'
choco --version
