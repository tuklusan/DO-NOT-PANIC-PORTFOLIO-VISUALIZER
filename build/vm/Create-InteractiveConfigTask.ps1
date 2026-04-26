$ErrorActionPreference = "Stop"
$taskName = "PortfolioLaunchConfig"
$configExe = Join-Path $env:USERPROFILE "Desktop\PortfolioVmUx\publish\config\PortfolioSaver.Config.exe"
$action = New-ScheduledTaskAction -Execute $configExe
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1)
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Get-ScheduledTaskInfo -TaskName $taskName | Select-Object LastRunTime,LastTaskResult,NextRunTime

