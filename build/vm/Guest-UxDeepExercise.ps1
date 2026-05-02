param(
    [ValidateRange(1, 1440)]
    [int]$ScreensaverDurationMinutes = 6,
    [ValidateRange(1, 3600)]
    [int]$CaptureIntervalSeconds = 5,
    [string]$RootPath = (Join-Path $env:USERPROFILE 'Desktop\PortfolioVmUx'),
    [string]$ResultName = ('ux-deep-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string]$ResultRootPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = $RootPath
$configExe = Join-Path $root 'publish\config\PortfolioSaver.Config.exe'
$desktopExe = Join-Path $root 'publish\desktop\PortfolioSaver.Desktop.exe'
if ([string]::IsNullOrWhiteSpace($ResultRootPath)) {
    $ResultRootPath = Join-Path $root 'results'
}
$resultName = $ResultName
$results = Join-Path $ResultRootPath $resultName

New-Item -ItemType Directory -Force -Path $results | Out-Null

function Capture-Screen {
    param([Parameter(Mandatory=$true)][string]$Path)

    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bitmap.Size)
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Find-ConfigWindow {
    param([int]$TimeoutSeconds = 20)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $children = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($child in $children) {
            $name = [string]$child.Current.Name
            if ($name -like '*PORTFOLIO VISUALIZER Config*') {
                return $child
            }
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)

    return $null
}

function Get-TabItems {
    param($Window)

    $tabCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)
    $tabs = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabCondition)
    $result = @()
    foreach ($t in $tabs) { $result += $t }
    return $result
}

function Select-TabItem {
    param($Tab)

    try {
        $pattern = $Tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $pattern.Select()
        Start-Sleep -Milliseconds 350
        return $true
    }
    catch {
        return $false
    }
}

function Get-ExerciseControls {
    param($Window)

    $types = @(
        [System.Windows.Automation.ControlType]::Edit,
        [System.Windows.Automation.ControlType]::Button,
        [System.Windows.Automation.ControlType]::CheckBox,
        [System.Windows.Automation.ControlType]::ComboBox,
        [System.Windows.Automation.ControlType]::Slider
    )

    $conditions = @()
    foreach ($ct in $types) {
        $conditions += New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            $ct)
    }

    $orCondition = New-Object System.Windows.Automation.OrCondition($conditions)
    $all = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $orCondition)

    $list = @()
    foreach ($c in $all) {
        if ($c.Current.IsOffscreen) { continue }
        $list += $c
    }

    return $list | Sort-Object { $_.Current.BoundingRectangle.Top }, { $_.Current.BoundingRectangle.Left }
}

function Close-ConfigChildWindows {
    param([int]$MainProcessId)

    for ($pass = 0; $pass -lt 6; $pass++) {
        $closedOne = $false
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)

        foreach ($w in $windows) {
            if ($w.Current.ProcessId -ne $MainProcessId) { continue }
            $title = [string]$w.Current.Name
            if ($title -like '*PORTFOLIO VISUALIZER Config*') { continue }

            try {
                $wp = $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
                $wp.Close()
                $closedOne = $true
                Start-Sleep -Milliseconds 180
                continue
            }
            catch {}

            try {
                $okCondition = New-Object System.Windows.Automation.AndCondition(
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                        [System.Windows.Automation.ControlType]::Button)),
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::NameProperty,
                        'OK')))
                $ok = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $okCondition)
                if ($ok -ne $null) {
                    $inv = $ok.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                    $inv.Invoke()
                    $closedOne = $true
                    Start-Sleep -Milliseconds 180
                    continue
                }
            }
            catch {}

            try { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } catch {}
            $closedOne = $true
            Start-Sleep -Milliseconds 180
        }

        if (-not $closedOne) { break }
    }
}

function Exercise-Control {
    param(
        $Control,
        [System.Collections.Generic.HashSet[string]]$InvokedButtons
    )

    $type = $Control.Current.ControlType.ProgrammaticName

    try { $Control.SetFocus() } catch {}
    Start-Sleep -Milliseconds 120

    if ($type -eq [System.Windows.Automation.ControlType]::Edit.ProgrammaticName) {
        try {
            $vp = $Control.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            if (-not $vp.Current.IsReadOnly) {
                $value = [string]$vp.Current.Value
                $vp.SetValue($value)
            }
        }
        catch {
            try { [System.Windows.Forms.SendKeys]::SendWait('{END}{LEFT}{RIGHT}') } catch {}
        }
    }
    elseif ($type -eq [System.Windows.Automation.ControlType]::CheckBox.ProgrammaticName) {
        try {
            $tp = $Control.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            $tp.Toggle()
            Start-Sleep -Milliseconds 120
            $tp.Toggle()
        }
        catch {}
    }
    elseif ($type -eq [System.Windows.Automation.ControlType]::ComboBox.ProgrammaticName) {
        try {
            $ecp = $Control.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            $ecp.Expand()
            Start-Sleep -Milliseconds 120
            $ecp.Collapse()
        }
        catch {
            try { [System.Windows.Forms.SendKeys]::SendWait('%{DOWN}{ESC}') } catch {}
        }
    }
    elseif ($type -eq [System.Windows.Automation.ControlType]::Slider.ProgrammaticName) {
        try {
            $rp = $Control.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
            if (-not $rp.Current.IsReadOnly) {
                $v = [double]$rp.Current.Value
                $step = [Math]::Max(1.0, [double]$rp.Current.SmallChange)
                $target = [Math]::Min([double]$rp.Current.Maximum, $v + $step)
                $rp.SetValue($target)
                Start-Sleep -Milliseconds 120
                $rp.SetValue($v)
            }
        }
        catch {}
    }
    elseif ($type -eq [System.Windows.Automation.ControlType]::Button.ProgrammaticName) {
        # Non-destructive button exercise: focus only (no invoke), to avoid
        # external app launches and modal chains that block full traversal.
        $null = $InvokedButtons
    }
}

function Find-ElementMetadataByProcessId {
    param(
        [int]$ProcessId,
        [string[]]$NameFragments = @(),
        [string[]]$AutomationIds = @(),
        [int]$TimeoutSeconds = 8
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $all = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($item in $all) {
                try {
                    if ($item.Current.ProcessId -ne $ProcessId) { continue }

                    $automationId = [string]$item.Current.AutomationId
                    if ($AutomationIds.Count -gt 0) {
                        foreach ($targetAutomationId in $AutomationIds) {
                            if (-not [string]::IsNullOrWhiteSpace($targetAutomationId) -and
                                $automationId -eq $targetAutomationId) {
                                return [ordered]@{
                                    Name = [string]$item.Current.Name
                                    AutomationId = $automationId
                                    HelpText = [string]$item.Current.HelpText
                                }
                            }
                        }
                    }

                    $metadata = @(
                        [string]$item.Current.Name,
                        [string]$item.Current.HelpText
                    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

                    foreach ($fragment in $NameFragments) {
                        if ([string]::IsNullOrWhiteSpace($fragment)) { continue }
                        if ($metadata | Where-Object { $_ -like "*$fragment*" }) {
                            return [ordered]@{
                                Name = [string]$item.Current.Name
                                AutomationId = $automationId
                                HelpText = [string]$item.Current.HelpText
                            }
                        }
                    }
                }
                catch {
                    continue
                }
            }
        }
        catch {
            Start-Sleep -Milliseconds 300
            continue
        }

        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)

    return $null
}

if (-not (Test-Path $configExe)) { throw "Missing config executable: $configExe" }
if (-not (Test-Path $desktopExe)) { throw "Missing desktop executable: $desktopExe" }

$summary = [ordered]@{
    StartedAt = (Get-Date).ToString('o')
    ResultsPath = $results
    ConfigShots = 0
    ScreensaverShots = 0
    DesktopShots = 0
    ConfigPhaseStatus = "Pending"
    DesktopPhaseStatus = "Pending"
    ScreensaverPhaseStatus = "LegacyNotRun"
    ConfigVersionCheck = "Pending"
    DesktopVersionCheck = "Pending"
    ScreensaverVersionCheck = "LegacyNotRun"
    FullScreenToggleStatus = "Pending"
    Notes = @()
    PlannedScreensaverDurationMinutes = $ScreensaverDurationMinutes
    CaptureIntervalSeconds = $CaptureIntervalSeconds
}

$summaryPath = Join-Path $results 'ux-deep-summary.json'
$legacySummaryPath = Join-Path $results 'vm-ux-summary.json'
$logPath = Join-Path $results 'ux-deep-run.log'

function Write-SummaryFiles {
    $json = $summary | ConvertTo-Json -Depth 6
    Write-TextFileWithRetry -Path $summaryPath -Content $json
    Write-TextFileWithRetry -Path $legacySummaryPath -Content $json
}

function Write-TextFileWithRetry {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $attempts = 0
    while ($true) {
        try {
            Set-Content -LiteralPath $Path -Value $Content -Encoding UTF8
            return
        }
        catch {
            $attempts++
            if ($attempts -ge 20) {
                throw
            }

            Start-Sleep -Milliseconds 200
        }
    }
}

$summary.ExportMode = 'LocalWorkspace'
$summary.ResultName = $resultName
$summary.ResultPath = $results
Write-SummaryFiles
Start-Transcript -Path $logPath -Force | Out-Null

try {
    Get-Process PortfolioSaver.Config,PortfolioSaver.Desktop,PortfolioSaver.Screensaver -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    try {
        $config = Start-Process -FilePath $configExe -PassThru
        Start-Sleep -Seconds 3
        $window = Find-ConfigWindow -TimeoutSeconds 20
        if ($null -eq $window) { throw 'Could not locate config window via UI Automation.' }
        if ([string]$window.Current.Name -like '*BETA-5.5*') {
            $summary.ConfigVersionCheck = "Passed"
        }
        else {
            $summary.ConfigVersionCheck = "Failed"
            $summary.Notes += "Config window title missing expected BETA-5.5 marker: '$([string]$window.Current.Name)'"
        }

        $tabs = Get-TabItems -Window $window
        if ($tabs.Count -eq 0) { throw 'No tab items found in config window.' }

        $shotIndex = 1
        foreach ($tab in $tabs) {
            [void](Select-TabItem -Tab $tab)
            $tabName = "tab"
            try { $tabName = (($tab.Current.Name -replace '[^A-Za-z0-9_-]','_')) } catch {}
            Capture-Screen -Path (Join-Path $results ("config-tab-{0:D3}-{1}.png" -f $shotIndex, $tabName))
            $summary.ConfigShots++
            $shotIndex++

            $controls = Get-ExerciseControls -Window $window
            $invokedButtons = New-Object 'System.Collections.Generic.HashSet[string]'
            $controlIndex = 1
            foreach ($control in $controls) {
                Exercise-Control -Control $control -InvokedButtons $invokedButtons
                $typeName = "control"
                $safeName = "unnamed"
                try {
                    $typeName = ($control.Current.ControlType.LocalizedControlType -replace '\s+','-')
                    $name = [string]$control.Current.Name
                    if (-not [string]::IsNullOrWhiteSpace($name)) {
                        $safeName = ($name -replace '[^A-Za-z0-9_-]','_')
                    }
                }
                catch {
                    $summary.Notes += "Control metadata read failed on tab '$tabName': $($_.Exception.Message)"
                }

                Capture-Screen -Path (Join-Path $results ("config-{0:D3}-{1:D3}-{2}-{3}.png" -f $shotIndex, $controlIndex, $typeName, $safeName))
                $summary.ConfigShots++
                $controlIndex++

                Close-ConfigChildWindows -MainProcessId $config.Id

                if ($controlIndex -gt 400) {
                    $summary.Notes += "Control traversal capped at 400 controls on tab '$tabName'."
                    break
                }
            }
        }

        $summary.ConfigPhaseStatus = "Completed"
        Write-SummaryFiles
    }
    catch {
        $summary.ConfigPhaseStatus = "Failed"
        $summary.Notes += "Config phase error: $($_.Exception.Message)"
        Write-SummaryFiles
    }
    finally {
        Get-Process PortfolioSaver.Config -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }

    try {
        $desktop = Start-Process -FilePath $desktopExe -PassThru
        Start-Sleep -Seconds 5
        $versionMatch = Find-ElementMetadataByProcessId `
            -ProcessId $desktop.Id `
            -AutomationIds @('ScreensaverVersionWatermark', 'ScreensaverHostWindow', 'MainWindowTitle') `
            -NameFragments @('beta5', 'Version 0.9.0-beta', '0.9.0-beta', 'Portfolio Visualizer') `
            -TimeoutSeconds 10
        if ($null -eq $versionMatch) {
            $desktop.Refresh()
            if ($desktop.MainWindowTitle -like '*beta5*' -or
                $desktop.MainWindowTitle -like '*0.9.0-beta*' -or
                $desktop.MainWindowTitle -like '*Portfolio Visualizer*') {
                $versionMatch = [ordered]@{
                    Name = $desktop.MainWindowTitle
                    AutomationId = 'MainWindowTitleFallback'
                    HelpText = [string]::Empty
                }
            }
        }
        if ($null -ne $versionMatch) {
            $summary.DesktopVersionCheck = "Passed"
            $summary.Notes += ("Desktop version element observed: name='{0}' automation_id='{1}' help='{2}'" -f
                $versionMatch.Name,
                $versionMatch.AutomationId,
                $versionMatch.HelpText)
        }
        else {
            $summary.DesktopVersionCheck = "Failed"
            $summary.Notes += "Desktop version element containing the expected beta marker was not detected."
        }

        Start-Sleep -Seconds 1
        try { [System.Windows.Forms.SendKeys]::SendWait('{F11}') } catch {}
        Start-Sleep -Seconds 2
        $desktopFull = Join-Path $results 'desktop-fullscreen-entry.png'
        Capture-Screen -Path $desktopFull
        $summary.DesktopShots++
        $summary.ScreensaverShots++
        $summary.DesktopPhaseStatus = "Running"
        Write-SummaryFiles

        try { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } catch {}
        Start-Sleep -Seconds 2
        $desktopWindowed = Join-Path $results 'desktop-windowed-after-esc.png'
        Capture-Screen -Path $desktopWindowed
        $summary.DesktopShots++
        $summary.ScreensaverShots++
        $summary.FullScreenToggleStatus = "Completed"
        Write-SummaryFiles

        $targetFrames = [Math]::Max(1, [int][Math]::Ceiling(($ScreensaverDurationMinutes * 60.0) / $CaptureIntervalSeconds))
        for ($i = 1; $i -le $targetFrames; $i++) {
            if ($desktop.HasExited) {
                throw "Desktop process exited early at frame $i (exit code: $($desktop.ExitCode))."
            }

            $path = Join-Path $results ("desktop-{0:D3}.png" -f $i)
            Capture-Screen -Path $path
            $summary.ScreensaverShots++
            $summary.DesktopShots++
            Write-SummaryFiles
            Start-Sleep -Seconds $CaptureIntervalSeconds
        }

        $summary.DesktopPhaseStatus = "Completed"
        Write-SummaryFiles
    }
    catch {
        $summary.DesktopPhaseStatus = "Failed"
        $summary.Notes += "Desktop phase error: $($_.Exception.Message)"
        Write-SummaryFiles
    }
    finally {
        try { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } catch {}
        Start-Sleep -Seconds 1
        Get-Process PortfolioSaver.Desktop,PortfolioSaver.Screensaver -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
finally {
    $summary.FinishedAt = (Get-Date).ToString('o')
    Write-SummaryFiles
    Stop-Transcript | Out-Null
    try {
        $traceRoot = Join-Path $env:APPDATA "PortfolioSaver\Trace"
        $localTraceTarget = Join-Path $results 'trace'
        if (Test-Path $traceRoot) {
            New-Item -ItemType Directory -Force -Path $localTraceTarget | Out-Null
            foreach ($traceName in @("trace.circular.log", "trace.circular.idx")) {
                $tracePath = Join-Path $traceRoot $traceName
                if (Test-Path $tracePath) {
                    Copy-Item -LiteralPath $tracePath -Destination (Join-Path $localTraceTarget $traceName) -Force
                }
            }
        }
    }
    catch {
        Write-Output ("HOST_EXPORT_ERROR=" + $_.Exception.Message)
    }
    Write-Output "RESULTS=$results"
    Write-Output "SUMMARY=$summaryPath"
}

