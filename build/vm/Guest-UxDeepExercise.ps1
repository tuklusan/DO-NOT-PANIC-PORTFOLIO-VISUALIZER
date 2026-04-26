param(
    [ValidateRange(1, 180)]
    [int]$ScreensaverDurationMinutes = 6,
    [ValidateRange(1, 60)]
    [int]$CaptureIntervalSeconds = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = Join-Path $env:USERPROFILE 'Desktop\PortfolioVmUx'
$configExe = Join-Path $root 'publish\config\PortfolioSaver.Config.exe'
$saverExe = Join-Path $root 'publish\screensaver\PortfolioSaver.Screensaver.exe'
$resultName = 'ux-deep-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$repoShare = "\\VBOXSVR\codexrepo"
$localResultsRoot = Join-Path $root 'results'
$results = Join-Path $localResultsRoot $resultName
$hostResultRoot = $null
$usingDirectHostResults = $false

if (Test-Path $repoShare) {
    try {
        $hostResultRoot = Join-Path $repoShare 'build\vm\artifacts\vm-results'
        New-Item -ItemType Directory -Force -Path $hostResultRoot | Out-Null
        $results = Join-Path $hostResultRoot $resultName
        $usingDirectHostResults = $true
    }
    catch {
        $hostResultRoot = $null
        $results = Join-Path $localResultsRoot $resultName
        $usingDirectHostResults = $false
    }
}

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
if (-not (Test-Path $saverExe)) { throw "Missing screensaver executable: $saverExe" }

$summary = [ordered]@{
    StartedAt = (Get-Date).ToString('o')
    ResultsPath = $results
    ConfigShots = 0
    ScreensaverShots = 0
    ConfigPhaseStatus = "Pending"
    ScreensaverPhaseStatus = "Pending"
    ConfigVersionCheck = "Pending"
    ScreensaverVersionCheck = "Pending"
    Notes = @()
    PlannedScreensaverDurationMinutes = $ScreensaverDurationMinutes
    CaptureIntervalSeconds = $CaptureIntervalSeconds
}

$summaryPath = Join-Path $results 'ux-deep-summary.json'
$legacySummaryPath = Join-Path $results 'vm-ux-summary.json'
$logPath = Join-Path $results 'ux-deep-run.log'

function Write-SummaryFiles {
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $legacySummaryPath -Encoding UTF8
}

$summary.ExportMode = if ($usingDirectHostResults) { 'DirectHostShare' } else { 'LocalThenCopy' }
$summary.ResultName = $resultName
$summary.ResultPath = $results
Write-SummaryFiles
Start-Transcript -Path $logPath -Force | Out-Null

try {
    Get-Process PortfolioSaver.Config -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process PortfolioSaver.Screensaver -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

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
        $priorDisableInputExit = $env:PORTFOLIOSAVER_DISABLE_INPUT_EXIT
        $env:PORTFOLIOSAVER_DISABLE_INPUT_EXIT = '1'
        $saver = Start-Process -FilePath $saverExe -ArgumentList '/s' -PassThru
        Start-Sleep -Seconds 5
        $versionMatch = Find-ElementMetadataByProcessId `
            -ProcessId $saver.Id `
            -AutomationIds @('ScreensaverVersionWatermark', 'ScreensaverHostWindow') `
            -NameFragments @('beta5', 'Version 0.9.0-beta', '0.9.0-beta', 'Portfolio Screensaver') `
            -TimeoutSeconds 10
        if ($null -eq $versionMatch) {
            $saver.Refresh()
            if ($saver.MainWindowTitle -like '*beta5*' -or
                $saver.MainWindowTitle -like '*0.9.0-beta*' -or
                $saver.MainWindowTitle -like '*Portfolio Screensaver*') {
                $versionMatch = [ordered]@{
                    Name = $saver.MainWindowTitle
                    AutomationId = 'MainWindowTitleFallback'
                    HelpText = [string]::Empty
                }
            }
        }
        if ($null -ne $versionMatch) {
            $summary.ScreensaverVersionCheck = "Passed"
            $summary.Notes += ("Screensaver version element observed: name='{0}' automation_id='{1}' help='{2}'" -f
                $versionMatch.Name,
                $versionMatch.AutomationId,
                $versionMatch.HelpText)
        }
        else {
            $summary.ScreensaverVersionCheck = "Failed"
            $summary.Notes += "Screensaver version element containing the expected beta marker was not detected."
        }

        $targetFrames = [Math]::Max(1, [int][Math]::Ceiling(($ScreensaverDurationMinutes * 60.0) / $CaptureIntervalSeconds))
        for ($i = 1; $i -le $targetFrames; $i++) {
            if ($saver.HasExited) {
                throw "Screensaver process exited early at frame $i (exit code: $($saver.ExitCode))."
            }

            $path = Join-Path $results ("screensaver-{0:D3}.png" -f $i)
            Capture-Screen -Path $path
            $summary.ScreensaverShots++
            Start-Sleep -Seconds $CaptureIntervalSeconds
        }

        $summary.ScreensaverPhaseStatus = "Completed"
        Write-SummaryFiles
    }
    catch {
        $summary.ScreensaverPhaseStatus = "Failed"
        $summary.Notes += "Screensaver phase error: $($_.Exception.Message)"
        Write-SummaryFiles
    }
    finally {
        if ($null -eq $priorDisableInputExit) {
            Remove-Item Env:PORTFOLIOSAVER_DISABLE_INPUT_EXIT -ErrorAction SilentlyContinue
        }
        else {
            $env:PORTFOLIOSAVER_DISABLE_INPUT_EXIT = $priorDisableInputExit
        }

        try { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } catch {}
        Start-Sleep -Seconds 1
        Get-Process PortfolioSaver.Screensaver -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
finally {
    $summary.FinishedAt = (Get-Date).ToString('o')
    Write-SummaryFiles
    Stop-Transcript | Out-Null
    try {
        if ((-not $usingDirectHostResults) -and (Test-Path $repoShare)) {
            $traceRoot = Join-Path $env:APPDATA "PortfolioSaver\Trace"
            $traceTarget = Join-Path (Join-Path $repoShare "build\vm\artifacts\trace") ($resultName + "-trace")
            $hostRoot = Join-Path $repoShare "build\vm\artifacts\vm-results"
            $hostTarget = Join-Path $hostRoot $resultName
            New-Item -ItemType Directory -Force -Path $hostRoot | Out-Null
            if (Test-Path $hostTarget) {
                Remove-Item -LiteralPath $hostTarget -Recurse -Force -ErrorAction SilentlyContinue
            }

            Copy-Item -LiteralPath $results -Destination $hostTarget -Recurse -Force
            if (Test-Path $traceRoot) {
                New-Item -ItemType Directory -Force -Path $traceTarget | Out-Null
                foreach ($traceName in @("trace.circular.log", "trace.circular.idx")) {
                    $tracePath = Join-Path $traceRoot $traceName
                    if (Test-Path $tracePath) {
                        Copy-Item -LiteralPath $tracePath -Destination (Join-Path $traceTarget $traceName) -Force
                    }
                }
            }
            Write-Output ("HOST_RESULT_DIR=" + $hostTarget)
            if (Test-Path $traceTarget) {
                Write-Output ("HOST_TRACE_DIR=" + $traceTarget)
            }
        }
        elseif ($usingDirectHostResults -and (Test-Path $repoShare)) {
            $traceRoot = Join-Path $env:APPDATA "PortfolioSaver\Trace"
            $traceTarget = Join-Path (Join-Path $repoShare "build\vm\artifacts\trace") ($resultName + "-trace")
            if (Test-Path $traceRoot) {
                New-Item -ItemType Directory -Force -Path $traceTarget | Out-Null
                foreach ($traceName in @("trace.circular.log", "trace.circular.idx")) {
                    $tracePath = Join-Path $traceRoot $traceName
                    if (Test-Path $tracePath) {
                        Copy-Item -LiteralPath $tracePath -Destination (Join-Path $traceTarget $traceName) -Force
                    }
                }
            }
            Write-Output ("HOST_RESULT_DIR=" + $results)
            if (Test-Path $traceTarget) {
                Write-Output ("HOST_TRACE_DIR=" + $traceTarget)
            }
        }
    }
    catch {
        Write-Output ("HOST_EXPORT_ERROR=" + $_.Exception.Message)
    }
    Write-Output "RESULTS=$results"
    Write-Output "SUMMARY=$summaryPath"
}

