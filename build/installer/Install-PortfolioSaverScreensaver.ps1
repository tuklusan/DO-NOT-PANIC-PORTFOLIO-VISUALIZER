[CmdletBinding()]
param(
    [string]$StagingRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-NativeSystemDirectory {
    $system32Path = Join-Path $env:WINDIR "System32"
    $sysnativePath = Join-Path $env:WINDIR "Sysnative"

    if (-not [Environment]::Is64BitProcess -and [Environment]::Is64BitOperatingSystem -and (Test-Path $sysnativePath)) {
        return $sysnativePath
    }

    return $system32Path
}

function Copy-ToPersistentStagingRoot {
    $persistentRoot = Join-Path $env:TEMP ("PortfolioSaverScreensaverInstaller-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $persistentRoot | Out-Null

    foreach ($item in (Get-ChildItem -LiteralPath $PSScriptRoot -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $persistentRoot -Recurse -Force
    }

    return $persistentRoot
}

function Start-ElevatedInstall {
    $persistentRoot = Copy-ToPersistentStagingRoot
    $scriptPath = Join-Path $persistentRoot "Install-PortfolioSaverScreensaver.ps1"
    $arguments = "-ExecutionPolicy Bypass -File `"$scriptPath`" -StagingRoot `"$persistentRoot`""
    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $arguments | Out-Null
}

if (-not (Test-IsAdministrator)) {
    Write-Host "Requesting administrator rights to install the screensaver..."
    Start-ElevatedInstall
    exit 0
}

$sourceRoot = Join-Path $PSScriptRoot "payload"
if (-not (Test-Path $sourceRoot)) {
    throw "Installer payload folder not found: $sourceRoot"
}

$installRoot = Get-NativeSystemDirectory
$installRootDisplay = Join-Path $env:WINDIR "System32"
$stateRoot = Join-Path $env:ProgramData "PortfolioSaverScreensaver"
$manifestPath = Join-Path $stateRoot "installed-files.txt"
$uninstallScriptTarget = Join-Path $stateRoot "Uninstall-PortfolioSaverScreensaver.ps1"
$uninstallRegistryKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PortfolioSaverScreensaver"

New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null

$installedPaths = New-Object System.Collections.Generic.List[string]
$directories = Get-ChildItem $sourceRoot -Recurse -Directory | Sort-Object FullName
foreach ($directory in $directories) {
    $relativePath = $directory.FullName.Substring($sourceRoot.Length).TrimStart('\')
    $targetDirectory = if ([string]::IsNullOrWhiteSpace($relativePath)) { $installRoot } else { Join-Path $installRoot $relativePath }
    $manifestDirectory = if ([string]::IsNullOrWhiteSpace($relativePath)) { $installRootDisplay } else { Join-Path $installRootDisplay $relativePath }
    New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    $installedPaths.Add($manifestDirectory)
}

$files = Get-ChildItem $sourceRoot -Recurse -File | Sort-Object FullName
foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($sourceRoot.Length).TrimStart('\')
    $targetPath = Join-Path $installRoot $relativePath
    $manifestTargetPath = Join-Path $installRootDisplay $relativePath
    $targetDirectory = Split-Path -Parent $targetPath
    New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $targetPath -Force
    $installedPaths.Add($manifestTargetPath)
    Write-Host "Installed $relativePath"
}

$uninstallSource = Join-Path $PSScriptRoot "Uninstall-PortfolioSaverScreensaver.ps1"
Copy-Item -LiteralPath $uninstallSource -Destination $uninstallScriptTarget -Force
$installedPaths.Add($uninstallScriptTarget)

$installedPaths | Sort-Object -Unique | Set-Content -LiteralPath $manifestPath -Encoding ASCII

$uninstallCommand = "powershell.exe -ExecutionPolicy Bypass -File `"$uninstallScriptTarget`""
$screensaverPath = Join-Path $installRootDisplay "PortfolioSaver.Screensaver.scr"
New-Item -Path $uninstallRegistryKey -Force | Out-Null
Set-ItemProperty -Path $uninstallRegistryKey -Name "DisplayName" -Value "DO NOT PANIC PORTFOLIO VISUALIZER"
Set-ItemProperty -Path $uninstallRegistryKey -Name "Publisher" -Value "SANYALnet Labs"
Set-ItemProperty -Path $uninstallRegistryKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $uninstallRegistryKey -Name "InstallDate" -Value (Get-Date -Format "yyyyMMdd")
Set-ItemProperty -Path $uninstallRegistryKey -Name "InstallLocation" -Value $installRootDisplay
Set-ItemProperty -Path $uninstallRegistryKey -Name "DisplayIcon" -Value $screensaverPath
Set-ItemProperty -Path $uninstallRegistryKey -Name "UninstallString" -Value $uninstallCommand
Set-ItemProperty -Path $uninstallRegistryKey -Name "QuietUninstallString" -Value $uninstallCommand
Set-ItemProperty -Path $uninstallRegistryKey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallRegistryKey -Name "NoRepair" -Value 1 -Type DWord

if (-not [string]::IsNullOrWhiteSpace($StagingRoot) -and (Test-Path $StagingRoot)) {
    $stagingFullPath = [System.IO.Path]::GetFullPath($StagingRoot)
    $tempFullPath = [System.IO.Path]::GetFullPath($env:TEMP)
    $cleanupAllowed = $stagingFullPath.StartsWith($tempFullPath, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $stagingFullPath).StartsWith("PortfolioSaverScreensaverInstaller-", [System.StringComparison]::OrdinalIgnoreCase)

    if (-not $cleanupAllowed) {
        Write-Host "Skipping staging cleanup for non-temporary path: $stagingFullPath"
    }
    else {
    $escapedStagingRoot = $StagingRoot.Replace("'", "''")
    $cleanupScript = "Start-Sleep -Seconds 5; Remove-Item -LiteralPath '$escapedStagingRoot' -Recurse -Force -ErrorAction SilentlyContinue"
    Start-Process -FilePath "powershell.exe" -WindowStyle Hidden -ArgumentList "-NoProfile -ExecutionPolicy Bypass -Command `"$cleanupScript`"" | Out-Null
    }
}

Write-Host ""
Write-Host "DO NOT PANIC PORTFOLIO VISUALIZER installed to $installRootDisplay"
Write-Host "Open Windows Screen Saver Settings and choose 'PortfolioSaver.Screensaver'."
Write-Host "To remove it later, run:"
Write-Host "powershell.exe -ExecutionPolicy Bypass -File `"$uninstallScriptTarget`""
