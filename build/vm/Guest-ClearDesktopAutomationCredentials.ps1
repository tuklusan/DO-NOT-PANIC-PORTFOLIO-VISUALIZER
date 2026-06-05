param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$winlogonPath = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'

Remove-ItemProperty -Path $winlogonPath -Name DefaultPassword -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $winlogonPath -Name DefaultUserName -ErrorAction SilentlyContinue
Set-ItemProperty -Path $winlogonPath -Name AutoAdminLogon -Value '0'

$state = Get-ItemProperty -Path $winlogonPath
[pscustomobject]@{
    AutoAdminLogon = $state.AutoAdminLogon
    DefaultPasswordPresent = $null -ne $state.PSObject.Properties['DefaultPassword']
} | ConvertTo-Json -Compress
