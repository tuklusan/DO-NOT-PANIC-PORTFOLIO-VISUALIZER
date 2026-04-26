$ErrorActionPreference = 'Continue'

$outPath = 'C:\Temp\existing-tools-focused.txt'
Set-Content -LiteralPath $outPath -Value "Focused Tool Scan - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

$roots = @(
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
)

$pattern = 'Python|AutoHotkey|WinAppDriver|Windows Application Driver|Appium|Node|Git|Sysinternals|Selenium|Visual Studio|SDK|Driver|Oracle|VirtualBox|PuTTY|NSSM|7-Zip|jq|ripgrep|pywin|WDK|WINDRIVER'

Get-ItemProperty -Path $roots -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -and $_.DisplayName -match $pattern } |
    Select-Object DisplayName, DisplayVersion, Publisher |
    Sort-Object DisplayName -Unique |
    Format-Table -AutoSize |
    Out-String -Width 240 |
    Add-Content -LiteralPath $outPath

"Saved to $outPath" | Add-Content -LiteralPath $outPath
Get-Content -LiteralPath $outPath
