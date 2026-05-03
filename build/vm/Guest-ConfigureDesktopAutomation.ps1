param(
    [Parameter(Mandatory = $true)]
    [string]$RootPath,
    [Parameter(Mandatory = $true)]
    [string]$UserName,
    [Parameter(Mandatory = $true)]
    [string]$Password
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-Shortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [string]$Arguments = '',
        [string]$WorkingDirectory = ''
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    if (-not [string]::IsNullOrWhiteSpace($Arguments)) {
        $shortcut.Arguments = $Arguments
    }
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $shortcut.WorkingDirectory = $WorkingDirectory
    }
    $shortcut.Save()
}

$scriptsPath = Join-Path $RootPath 'scripts'
$agentPath = Join-Path $RootPath 'publish\agent\PortfolioSaver.VmAgent.exe'
$startupPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
$agentShortcutPath = Join-Path $startupPath 'PortfolioSaver VmAgent.lnk'
$agentLauncherPath = Join-Path $scriptsPath 'Start-PortfolioSaverVmAgent.cmd'

New-Item -ItemType Directory -Force -Path $scriptsPath,$startupPath | Out-Null

$agentLauncher = @"
@echo off
taskkill /IM PortfolioSaver.VmAgent.exe /F >nul 2>&1
cd /d "$RootPath"
start "" /min "$agentPath" --root-path "$RootPath"
"@
Set-Content -LiteralPath $agentLauncherPath -Value $agentLauncher -Encoding ASCII

New-Shortcut -ShortcutPath $agentShortcutPath -TargetPath $agentLauncherPath -WorkingDirectory $RootPath

Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name ScreenSaveActive -Value '0'
Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name ScreenSaverIsSecure -Value '0'
Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name ScreenSaveTimeOut -Value '0'

Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' -Name AutoAdminLogon -Value '1'
Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' -Name DefaultUserName -Value $UserName
Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' -Name DefaultPassword -Value $Password

[pscustomobject]@{
    RootPath = $RootPath
    AgentPath = $agentPath
    AgentShortcutPath = $agentShortcutPath
    ScreenSaveActive = (Get-ItemProperty -Path 'HKCU:\Control Panel\Desktop').ScreenSaveActive
    AutoAdminLogon = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon').AutoAdminLogon
} | ConvertTo-Json -Compress
