$ErrorActionPreference = "Stop"
$taskName = "PortfolioLaunchScreensaver"
$screensaverExe = Join-Path $env:USERPROFILE "Desktop\PortfolioVmUx\publish\screensaver\PortfolioSaver.Screensaver.exe"
$action = New-ScheduledTaskAction -Execute $screensaverExe -Argument "/s"
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1)
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
Unregister-ScheduledTask -TaskName "PortfolioLaunchConfig" -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
Stop-Process -Name "PortfolioSaver.Config" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "PortfolioSaver.Screensaver" -Force -ErrorAction SilentlyContinue
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Get-ScheduledTaskInfo -TaskName $taskName | Select-Object LastRunTime,LastTaskResult,NextRunTime
