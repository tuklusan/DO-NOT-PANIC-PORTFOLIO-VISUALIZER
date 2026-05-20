param(
    [ValidateRange(1, 10080)]
    [int]$ScreensaverDurationMinutes = 6,
    [ValidateRange(1, 3600)]
    [int]$CaptureIntervalSeconds = 5,
    [int]$DisplayWidth,
    [int]$DisplayHeight,
    [string]$DisplayProfile = 'default',
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
Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeWindowBounds {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
}
"@
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeMouseInput {
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
}
"@
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeDisplaySettings {
    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int CDS_UPDATEREGISTRY = 0x00000001;
    public const int DISP_CHANGE_SUCCESSFUL = 0;
    public const int DM_PELSWIDTH = 0x00080000;
    public const int DM_PELSHEIGHT = 0x00100000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmNup;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)]
    public static extern int EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsW", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);
}
"@

$root = $RootPath
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

function Get-WindowRectangle {
    param([Parameter(Mandatory=$true)][System.Diagnostics.Process]$Process)

    $Process.Refresh()
    if ($Process.MainWindowHandle -eq [IntPtr]::Zero) {
        return $null
    }

    $rect = New-Object NativeWindowBounds+RECT
    if (-not [NativeWindowBounds]::GetWindowRect($Process.MainWindowHandle, [ref]$rect)) {
        return $null
    }

    return [pscustomobject]@{
        Left = $rect.Left
        Top = $rect.Top
        Width = ($rect.Right - $rect.Left)
        Height = ($rect.Bottom - $rect.Top)
    }
}

function Test-IsTrueFullscreen {
    param([Parameter(Mandatory=$true)][System.Diagnostics.Process]$Process)

    $rect = Get-WindowRectangle -Process $Process
    if ($null -eq $rect) {
        return $false
    }

    $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    return [Math]::Abs($rect.Left - $screen.Left) -le 1 -and
           [Math]::Abs($rect.Top - $screen.Top) -le 1 -and
           [Math]::Abs($rect.Width - $screen.Width) -le 2 -and
           [Math]::Abs($rect.Height - $screen.Height) -le 2
}

function Focus-ProcessWindow {
    param([Parameter(Mandatory=$true)][System.Diagnostics.Process]$Process)

    $Process.Refresh()
    if ($Process.MainWindowHandle -eq [IntPtr]::Zero) {
        return $false
    }

    try {
        [Microsoft.VisualBasic.Interaction]::AppActivate($Process.Id) | Out-Null
        Start-Sleep -Milliseconds 40
    }
    catch {}

    try {
        $window = [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
        if ($null -ne $window) {
            $window.SetFocus()
            Start-Sleep -Milliseconds 40
        }
    }
    catch {}

    return $true
}

function Get-ProcessWindowElement {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $Process.Refresh()
        if ($Process.MainWindowHandle -ne [IntPtr]::Zero) {
            try {
                $window = [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
                if ($null -ne $window) {
                    return $window
                }
            }
            catch {}
        }

        $children = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($child in $children) {
            if ($child.Current.ProcessId -ne $Process.Id) { continue }
            $name = [string]$child.Current.Name
            if ($name -like '*PORTFOLIO VISUALIZER Config*' -or
                $name -like '*DO NOT PANIC PORTFOLIO VISUALIZER*') {
                return $child
            }
        }
        Start-Sleep -Milliseconds 90
    } while ((Get-Date) -lt $deadline)

    return $null
}

function Get-TabItems {
    param($Window)

    try {
        $tabCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::TabItem)
        $tabs = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabCondition)
        $result = @()
        foreach ($t in $tabs) { $result += $t }
        return $result
    }
    catch {
        return @()
    }
}

function Find-TabItemByName {
    param(
        [Parameter(Mandatory = $true)]$Window,
        [Parameter(Mandatory = $true)][string]$TabName
    )

    foreach ($tab in @(Get-TabItems -Window $Window)) {
        try {
            if ([string]$tab.Current.Name -eq $TabName) {
                return $tab
            }
        }
        catch {
            continue
        }
    }

    return $null
}

function Select-TabItem {
    param($Tab)

    try {
        $Tab.SetFocus()
        Start-Sleep -Milliseconds 25
        try { [System.Windows.Forms.SendKeys]::SendWait(' ') } catch {}
        Start-Sleep -Milliseconds 50
        $selected = $Tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        return $selected.Current.IsSelected
    }
    catch {
        try {
            $pattern = $Tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $pattern.Select()
            Start-Sleep -Milliseconds 60
            return $true
        }
        catch {
            return $false
        }
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

    try {
        $orCondition = New-Object System.Windows.Automation.OrCondition($conditions)
        $all = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $orCondition)

        $list = @()
        foreach ($c in $all) {
            if ($c.Current.IsOffscreen) { continue }
            $list += $c
        }

        return $list | Sort-Object { $_.Current.BoundingRectangle.Top }, { $_.Current.BoundingRectangle.Left }
    }
    catch {
        return @()
    }
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
                Start-Sleep -Milliseconds 75
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
                    Start-Sleep -Milliseconds 75
                    continue
                }
            }
            catch {}

            try { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } catch {}
            $closedOne = $true
            Start-Sleep -Milliseconds 75
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
    Start-Sleep -Milliseconds 40

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
            Start-Sleep -Milliseconds 40
            $tp.Toggle()
        }
        catch {}
    }
    elseif ($type -eq [System.Windows.Automation.ControlType]::ComboBox.ProgrammaticName) {
        try {
            $ecp = $Control.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            $ecp.Expand()
            Start-Sleep -Milliseconds 40
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
                Start-Sleep -Milliseconds 40
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

function Get-RepresentativeExerciseControls {
    param(
        [Parameter(Mandatory = $true)]$Controls,
        [int]$MaximumCount = 10
    )

    $selected = @()
    $seenKeys = New-Object 'System.Collections.Generic.HashSet[string]'
    $preferredTypes = @(
        [System.Windows.Automation.ControlType]::Edit.ProgrammaticName,
        [System.Windows.Automation.ControlType]::CheckBox.ProgrammaticName,
        [System.Windows.Automation.ControlType]::ComboBox.ProgrammaticName,
        [System.Windows.Automation.ControlType]::Slider.ProgrammaticName
    )

    foreach ($preferredType in $preferredTypes) {
        foreach ($control in $Controls) {
            try {
                if ($control.Current.ControlType.ProgrammaticName -ne $preferredType) { continue }

                $name = [string]$control.Current.Name
                $automationId = [string]$control.Current.AutomationId
                $key = '{0}|{1}|{2}' -f $preferredType, $automationId, $name
                if (-not $seenKeys.Add($key)) { continue }

                $selected += $control
                break
            }
            catch {
                continue
            }
        }
    }

    foreach ($control in $Controls) {
        if ($selected.Count -ge $MaximumCount) { break }
        try {
            $type = $control.Current.ControlType.ProgrammaticName
            if ($type -eq [System.Windows.Automation.ControlType]::Button.ProgrammaticName) { continue }

            $name = [string]$control.Current.Name
            $automationId = [string]$control.Current.AutomationId
            $key = '{0}|{1}|{2}' -f $type, $automationId, $name
            if (-not $seenKeys.Add($key)) { continue }
            $selected += $control
        }
        catch {
            continue
        }
    }

    return @($selected | Select-Object -First $MaximumCount)
}

function Send-KeySequence {
    param(
        [Parameter(Mandatory = $true)][string[]]$Keys,
        [int]$DelayMilliseconds = 35
    )

    foreach ($key in $Keys) {
        try { [System.Windows.Forms.SendKeys]::SendWait($key) } catch {}
        Start-Sleep -Milliseconds $DelayMilliseconds
    }
}

function Get-ScrollPatternTarget {
    param(
        [Parameter(Mandatory = $true)]$Window,
        [string]$TabName
    )

    try {
        $all = $Window.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)

        $best = $null
        $bestArea = -1.0
        foreach ($candidate in $all) {
            try {
                $pattern = $candidate.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
                if ($null -eq $pattern) { continue }
                if (-not $pattern.Current.VerticallyScrollable) { continue }

                $rect = $candidate.Current.BoundingRectangle
                $area = [double]([Math]::Max(0, $rect.Width) * [Math]::Max(0, $rect.Height))
                if ($area -le $bestArea) { continue }

                $best = [pscustomobject]@{
                    Element = $candidate
                    Pattern = $pattern
                }
                $bestArea = $area
            }
            catch {
                continue
            }
        }

        return $best
    }
    catch {
        return $null
    }
}

function Try-ScrollWindowContent {
    param(
        [Parameter(Mandatory = $true)]$Window,
        [string]$TabName,
        [int]$PageCount = 1
    )

    $target = Get-ScrollPatternTarget -Window $Window -TabName $TabName
    if ($null -eq $target) {
        return $false
    }

    for ($index = 0; $index -lt $PageCount; $index++) {
        try {
            $target.Pattern.Scroll(
                [System.Windows.Automation.ScrollAmount]::NoAmount,
                [System.Windows.Automation.ScrollAmount]::LargeIncrement)
            Start-Sleep -Milliseconds 75
        }
        catch {
            return $false
        }
    }

    return $true
}

function Perform-KeyboardScrollPass {
    param(
        [Parameter(Mandatory = $true)]$Window,
        [int]$TabSteps = 10,
        [int]$DelayMilliseconds = 35
    )

    if ($TabSteps -le 0) {
        return $false
    }

    try { $Window.SetFocus() } catch {}
    Start-Sleep -Milliseconds 30

    for ($index = 0; $index -lt $TabSteps; $index++) {
        Send-KeySequence -Keys @('{TAB}') -DelayMilliseconds $DelayMilliseconds
    }

    return $true
}

function Perform-VisibleConfigActivity {
    param(
        [Parameter(Mandatory = $true)]$Window,
        [Parameter(Mandatory = $true)][string]$TabName
    )

    try { $Window.SetFocus() } catch {}
    Start-Sleep -Milliseconds 40
    $invokedButtons = New-Object 'System.Collections.Generic.HashSet[string]'
    $controls = @(Get-ExerciseControls -Window $Window)
    $maxControls = if ($TabName -eq 'Advanced') { 8 } else { 6 }
    $representativeControls = @(Get-RepresentativeExerciseControls -Controls $controls -MaximumCount $maxControls)

    foreach ($control in $representativeControls) {
        Exercise-Control -Control $control -InvokedButtons $invokedButtons
        Start-Sleep -Milliseconds 45
        Send-KeySequence -Keys @('{TAB}') -DelayMilliseconds 30
    }

    if ($TabName -eq 'Advanced') {
        $didScroll = Perform-KeyboardScrollPass -Window $Window -TabSteps 14 -DelayMilliseconds 28
        if (-not $didScroll) {
            $didScroll = Try-ScrollWindowContent -Window $Window -TabName $TabName -PageCount 2
        }
        else {
            $null = Try-ScrollWindowContent -Window $Window -TabName $TabName -PageCount 1
        }
        return $true
    }

    $null = Perform-KeyboardScrollPass -Window $Window -TabSteps 6 -DelayMilliseconds 30
    return $false
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
            Start-Sleep -Milliseconds 90
            continue
        }

        Start-Sleep -Milliseconds 90
    } while ((Get-Date) -lt $deadline)

    return $null
}

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
    RequestedDisplayProfile = $DisplayProfile
    RequestedDisplayWidth = if ($DisplayWidth -gt 0) { $DisplayWidth } else { $null }
    RequestedDisplayHeight = if ($DisplayHeight -gt 0) { $DisplayHeight } else { $null }
    SupportedDisplayModes = @()
}

$summaryPath = Join-Path $results 'ux-deep-summary.json'
$legacySummaryPath = Join-Path $results 'vm-ux-summary.json'
$logPath = Join-Path $results 'ux-deep-run.log'
$referenceSpotCheckPath = Join-Path $results 'reference-spot-checks.jsonl'
$referenceComparisonPath = Join-Path $results 'reference-spot-check-comparisons.jsonl'

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

            Start-Sleep -Milliseconds 80
        }
    }
}

function Reset-PortfolioTraceRoot {
    $traceRoot = Join-Path $env:APPDATA "PortfolioSaver\Trace"
    if (Test-Path $traceRoot) {
        Remove-Item -LiteralPath $traceRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    New-Item -ItemType Directory -Force -Path $traceRoot | Out-Null
}

function Get-CurrentVirtualScreenSize {
    $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    return [pscustomobject]@{
        Width = $screen.Width
        Height = $screen.Height
    }
}

function Get-AvailableDisplayModes {
    $modes = New-Object System.Collections.Generic.List[object]
    $modeIndex = 0

    while ($true) {
        $mode = New-Object NativeDisplaySettings+DEVMODE
        $mode.dmSize = [System.Runtime.InteropServices.Marshal]::SizeOf([type]([NativeDisplaySettings+DEVMODE]))
        if ([NativeDisplaySettings]::EnumDisplaySettings($null, $modeIndex, [ref]$mode) -eq 0) {
            break
        }

        $modes.Add([pscustomobject]@{
                Width = $mode.dmPelsWidth
                Height = $mode.dmPelsHeight
                BitsPerPixel = $mode.dmBitsPerPel
                DisplayFrequency = $mode.dmDisplayFrequency
                ModeIndex = $modeIndex
            })

        $modeIndex++
    }

    return @($modes | Sort-Object Width, Height, BitsPerPixel, DisplayFrequency -Unique)
}

function Get-CimSupportedDisplayModes {
    $modes = New-Object System.Collections.Generic.List[object]

    try {
        $resolutions = Get-CimInstance -ClassName CIM_VideoControllerResolution -ErrorAction Stop |
            Sort-Object HorizontalResolution, VerticalResolution -Descending
        foreach ($resolution in $resolutions) {
            if ($null -eq $resolution.HorizontalResolution -or $null -eq $resolution.VerticalResolution) { continue }
            $modes.Add([pscustomobject]@{
                    Width = [int]$resolution.HorizontalResolution
                    Height = [int]$resolution.VerticalResolution
                    BitsPerPixel = $null
                    DisplayFrequency = [int]$resolution.RefreshRate
                    ModeIndex = $null
                })
        }
    }
    catch {}

    return @($modes | Sort-Object Width, Height, DisplayFrequency -Unique)
}

function Format-DisplayModeNames {
    param(
        [Parameter(Mandatory = $true)]$Modes
    )

    $names = New-Object System.Collections.Generic.List[string]
    foreach ($mode in @($Modes)) {
        if ($null -eq $mode) { continue }

        if ($mode -is [string]) {
            if (-not [string]::IsNullOrWhiteSpace($mode)) {
                $names.Add($mode.Trim())
            }
            continue
        }

        $widthProperty = $mode.PSObject.Properties['Width']
        $heightProperty = $mode.PSObject.Properties['Height']
        if ($null -ne $widthProperty -and $null -ne $heightProperty) {
            $name = "{0} x {1}" -f $widthProperty.Value, $heightProperty.Value
            if (-not [string]::IsNullOrWhiteSpace($name)) {
                $names.Add($name.Trim())
            }
        }
    }

    return @($names | Sort-Object -Unique)
}

function Find-TopLevelWindowByNameLike {
    param(
        [Parameter(Mandatory = $true)][string]$NameLike,
        [int]$TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $children = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
                [System.Windows.Automation.TreeScope]::Children,
                [System.Windows.Automation.Condition]::TrueCondition)
            for ($index = 0; $index -lt $children.Count; $index++) {
                try {
                    $child = $children.Item($index)
                    if ($null -eq $child) { continue }
                    if ($child.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) { continue }
                    $name = [string]$child.Current.Name
                    if ($name -like $NameLike) {
                        return $child
                    }
                }
                catch {
                    continue
                }
            }
        }
        catch {}

        Start-Sleep -Milliseconds 150
    } while ((Get-Date) -lt $deadline)

    return $null
}

function Try-ApplyDisplayResolutionViaSettings {
    param(
        [int]$Width,
        [int]$Height
    )

    try {
        Start-Process -FilePath 'cmd.exe' -ArgumentList '/c start "" ms-settings:display' | Out-Null
        Start-Sleep -Milliseconds 1400

        $window = Find-TopLevelWindowByNameLike -NameLike '*Settings*' -TimeoutSeconds 20
        if ($null -eq $window) {
            return [pscustomobject]@{
                Applied = $false
                RequestedWidth = $Width
                RequestedHeight = $Height
                ResultCode = 'settings-window-not-found'
                AvailableModes = @(Format-DisplayModeNames -Modes (Get-CimSupportedDisplayModes))
            }
        }

        try { $window.SetFocus() } catch {}
        Start-Sleep -Milliseconds 200

        $combo = Find-DescendantByNameAndControlType -Root $window -Name 'Display resolution' -ControlType ([System.Windows.Automation.ControlType]::ComboBox)
        if ($null -eq $combo) {
            $combo = Find-DescendantByNameLikeAndControlType -Root $window -NameLike '*resolution*' -ControlType ([System.Windows.Automation.ControlType]::ComboBox)
        }
        if ($null -eq $combo) {
            return [pscustomobject]@{
                Applied = $false
                RequestedWidth = $Width
                RequestedHeight = $Height
                ResultCode = 'settings-combo-not-found'
                AvailableModes = @(Format-DisplayModeNames -Modes (Get-CimSupportedDisplayModes))
            }
        }

        try { $combo.SetFocus() } catch {}
        Start-Sleep -Milliseconds 120

        $expanded = Expand-AutomationElement -Element $combo
        if (-not $expanded) {
            try { [System.Windows.Forms.SendKeys]::SendWait('%{DOWN}') } catch {}
        }
        Start-Sleep -Milliseconds 350

        $targetName = "$Width x $Height"
        $modeItems = @()
        try {
            $listItems = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::ListItem)))
            for ($index = 0; $index -lt $listItems.Count; $index++) {
                $item = $listItems.Item($index)
                if ($null -eq $item) { continue }
                $name = [string]$item.Current.Name
                if ([string]::IsNullOrWhiteSpace($name)) { continue }
                if ($name -match '^\d+\s*x\s*\d+') {
                    $modeItems += $name.Trim()
                }
            }
        }
        catch {}

        $availableModes = @($modeItems | Sort-Object -Unique)
        if ($availableModes.Count -eq 0) {
            $availableModes = @(Format-DisplayModeNames -Modes (Get-CimSupportedDisplayModes))
        }
        $targetItem = Find-DescendantByNameAndControlType -Root ([System.Windows.Automation.AutomationElement]::RootElement) -Name $targetName -ControlType ([System.Windows.Automation.ControlType]::ListItem)
        if ($null -eq $targetItem) {
            $targetItem = Find-DescendantByNameLikeAndControlType -Root ([System.Windows.Automation.AutomationElement]::RootElement) -NameLike "$targetName*" -ControlType ([System.Windows.Automation.ControlType]::ListItem)
        }

        if ($null -eq $targetItem) {
            try { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } catch {}
            return [pscustomobject]@{
                Applied = $false
                RequestedWidth = $Width
                RequestedHeight = $Height
                ResultCode = 'settings-mode-not-found'
                AvailableModes = @($availableModes)
            }
        }

        $selected = $false
        try {
            $selectionItemPattern = $targetItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $selectionItemPattern.Select()
            $selected = $true
        }
        catch {
            $selected = Invoke-AutomationElement -Element $targetItem
        }

        if (-not $selected) {
            try {
                $selected = Click-AutomationElementCenter -Element $targetItem
            }
            catch {}
        }

        Start-Sleep -Milliseconds 1200

        $keepButton = Find-DescendantByNameLikeAndControlType -Root ([System.Windows.Automation.AutomationElement]::RootElement) -NameLike 'Keep changes*' -ControlType ([System.Windows.Automation.ControlType]::Button)
        if ($null -ne $keepButton) {
            if (-not (Invoke-AutomationElement -Element $keepButton)) {
                [void](Click-AutomationElementCenter -Element $keepButton)
            }
            Start-Sleep -Milliseconds 800
        }

        $runtime = Get-CurrentVirtualScreenSize
        $applied = ($runtime.Width -eq $Width -and $runtime.Height -eq $Height)
        return [pscustomobject]@{
            Applied = $applied
            RequestedWidth = $Width
            RequestedHeight = $Height
            ResultCode = if ($applied) { 'settings-applied' } else { 'settings-apply-nochange' }
            AvailableModes = @($availableModes)
        }
    }
    catch {
        return [pscustomobject]@{
            Applied = $false
            RequestedWidth = $Width
            RequestedHeight = $Height
            ResultCode = ('settings-error: ' + $_.Exception.Message)
            AvailableModes = @(Format-DisplayModeNames -Modes (Get-CimSupportedDisplayModes))
        }
    }
    finally {
        $settingsWindow = Find-TopLevelWindowByNameLike -NameLike '*Settings*' -TimeoutSeconds 2
        if ($null -ne $settingsWindow) {
            try { $settingsWindow.SetFocus() } catch {}
            try { [System.Windows.Forms.SendKeys]::SendWait('%{F4}') } catch {}
            Start-Sleep -Milliseconds 150
        }
    }
}

function Try-ApplyDisplayResolution {
    param(
        [int]$Width,
        [int]$Height
    )

    if ($Width -le 0 -or $Height -le 0) {
        return [pscustomobject]@{
            Applied = $false
            RequestedWidth = $null
            RequestedHeight = $null
            ResultCode = $null
        }
    }

    $mode = New-Object NativeDisplaySettings+DEVMODE
    $mode.dmSize = [System.Runtime.InteropServices.Marshal]::SizeOf([type]([NativeDisplaySettings+DEVMODE]))
    $currentEnum = [NativeDisplaySettings]::EnumDisplaySettings($null, -1, [ref]$mode)
    $availableModes = @(Get-CimSupportedDisplayModes)
    if ($availableModes.Count -eq 0) {
        $availableModes = @(Get-AvailableDisplayModes)
    }

    if ($currentEnum -eq 0) {
        return Try-ApplyDisplayResolutionViaSettings -Width $Width -Height $Height
    }

    $mode.dmPelsWidth = $Width
    $mode.dmPelsHeight = $Height
    $mode.dmFields = 0x180000
    $result = [NativeDisplaySettings]::ChangeDisplaySettings([ref]$mode, 0)
    Start-Sleep -Milliseconds 900
    $payload = [pscustomobject]@{
        Applied = ($result -eq [NativeDisplaySettings]::DISP_CHANGE_SUCCESSFUL)
        RequestedWidth = $Width
        RequestedHeight = $Height
        ResultCode = $result
        AvailableModes = @($availableModes)
    }
    if (-not $payload.Applied) {
        $settingsFallback = Try-ApplyDisplayResolutionViaSettings -Width $Width -Height $Height
        if ($settingsFallback.Applied -or $settingsFallback.AvailableModes.Count -gt 0) {
            return $settingsFallback
        }
    }

    return $payload
}

function Write-ReferenceSpotCheck {
    param(
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][int]$CaptureIndex
    )

    $displayedSample = @(Get-PreferredDisplayedTapeSample)
    $symbols = if ($displayedSample.Count -gt 0) {
        @($displayedSample | Select-Object -ExpandProperty Symbol -Unique | Select-Object -First 6)
    }
    else {
        @('SPY', 'AAPL', 'MSFT', '^VIX', '^FTSE', 'DX-Y.NYB')
    }
    $referenceSource = 'ReferenceQuote'
    $referenceResults = @()
    $referenceError = $null

    try {
        $reference = Get-ReferenceSpotCheckResults -Symbols $symbols
        if ($null -ne $reference -and -not [string]::IsNullOrWhiteSpace([string]$reference.Source)) {
            $referenceSource = [string]$reference.Source
        }

        if ($null -ne $reference -and $null -ne $reference.Results) {
            $referenceResults = @($reference.Results)
        }

        if ($null -ne $reference -and -not [string]::IsNullOrWhiteSpace([string]$reference.Error)) {
            $referenceError = [string]$reference.Error
        }
    }
    catch {
        $referenceError = $_.Exception.Message
    }

    $payload = [pscustomobject]@{
        CapturedAt = (Get-Date).ToString('o')
        CaptureIndex = $CaptureIndex
        Source = $referenceSource
        Symbols = $symbols
        DisplayedSample = @($displayedSample)
        Results = @($referenceResults)
        Error = $referenceError
    }

    Add-Content -LiteralPath $OutputPath -Value ($payload | ConvertTo-Json -Compress) -Encoding UTF8
    Write-ReferenceSpotCheckComparison -OutputPath $referenceComparisonPath -Payload $payload
}

function Get-LatestDisplayedTapeSample {
    $tracePath = Join-Path $env:APPDATA 'PortfolioSaver\Trace\trace.circular.log'
    if (-not (Test-Path $tracePath)) {
        return @()
    }

    $tailText = Read-TextFileTailShared -Path $tracePath -MaxBytes 524288
    if ([string]::IsNullOrWhiteSpace($tailText)) {
        return @()
    }

    $line = ($tailText -split "`r?`n") |
        Where-Object { $_ -like '*event=DisplayedTapeSample*' } |
        Select-Object -Last 1

    if ([string]::IsNullOrWhiteSpace($line)) {
        return @()
    }

    $match = [regex]::Match($line, 'sample=\[(.*)\]\s*$')
    if (-not $match.Success) {
        return @()
    }

    $sampleText = $match.Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($sampleText)) {
        return @()
    }

    $items = New-Object System.Collections.Generic.List[object]
    foreach ($entry in ($sampleText -split ', ')) {
        $parts = $entry -split '~', 4
        if ($parts.Count -lt 4) {
            continue
        }

        $items.Add([pscustomobject]@{
            Symbol = $parts[0]
            LastText = $parts[1]
            ChangeText = $parts[2]
            State = $parts[3]
        })
    }

    return @($items)
}

function Test-IsDisplayedSampleFullyLive {
    param([Parameter(Mandatory = $true)][object[]]$DisplayedSample)

    if ($DisplayedSample.Count -eq 0) {
        return $false
    }

    return -not ($DisplayedSample | Where-Object { [string]$_.State -ne 'live' } | Select-Object -First 1)
}

function Get-PreferredDisplayedTapeSample {
    $tracePath = Join-Path $env:APPDATA 'PortfolioSaver\Trace\trace.circular.log'
    if (-not (Test-Path $tracePath)) {
        return @()
    }

    $tailText = Read-TextFileTailShared -Path $tracePath -MaxBytes 524288
    if ([string]::IsNullOrWhiteSpace($tailText)) {
        return @()
    }

    $sampleLines = @(($tailText -split "`r?`n") |
        Where-Object { $_ -like '*event=DisplayedTapeSample*' })
    if ($sampleLines.Count -eq 0) {
        return @()
    }

    $parsedSamples = New-Object System.Collections.Generic.List[object]
    foreach ($line in $sampleLines) {
        $match = [regex]::Match($line, 'sample=\[(.*)\]\s*$')
        if (-not $match.Success) {
            continue
        }

        $sampleText = $match.Groups[1].Value
        if ([string]::IsNullOrWhiteSpace($sampleText)) {
            continue
        }

        $items = New-Object System.Collections.Generic.List[object]
        foreach ($entry in ($sampleText -split ', ')) {
            $parts = $entry -split '~', 4
            if ($parts.Count -lt 4) {
                continue
            }

            $items.Add([pscustomobject]@{
                Symbol = $parts[0]
                LastText = $parts[1]
                ChangeText = $parts[2]
                State = $parts[3]
            })
        }

        if ($items.Count -gt 0) {
            $parsedSamples.Add(@($items))
        }
    }

    if ($parsedSamples.Count -eq 0) {
        return @()
    }

    foreach ($sample in (@($parsedSamples) | Select-Object -Reverse)) {
        if (Test-IsDisplayedSampleFullyLive -DisplayedSample $sample) {
            return @($sample)
        }
    }

    return @($parsedSamples[$parsedSamples.Count - 1])
}

function Read-TextFileTailShared {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$MaxBytes = 262144
    )

    try {
        $idxPath = [System.IO.Path]::ChangeExtension($Path, '.idx')
        if (Test-Path $idxPath) {
            $positionText = Get-Content -LiteralPath $idxPath -Raw -ErrorAction Stop
            $writePosition = 0
            if ([int]::TryParse($positionText.Trim(), [ref]$writePosition)) {
                $bytes = [System.IO.File]::ReadAllBytes($Path)
                if ($bytes.Length -gt 0) {
                    $position = [Math]::Max(0, [Math]::Min($writePosition, $bytes.Length))
                    $orderedBytes = if ($position -eq 0) {
                        $bytes
                    }
                    else {
                        $suffixLength = $bytes.Length - $position
                        $ordered = New-Object byte[] $bytes.Length
                        [Array]::Copy($bytes, $position, $ordered, 0, $suffixLength)
                        [Array]::Copy($bytes, 0, $ordered, $suffixLength, $position)
                        $ordered
                    }

                    $tailLength = [Math]::Min($MaxBytes, $orderedBytes.Length)
                    $start = [Math]::Max(0, $orderedBytes.Length - $tailLength)
                    return ([System.Text.Encoding]::UTF8.GetString($orderedBytes, $start, $tailLength)).Replace("`0", '')
                }
            }
        }

        $fileStream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $length = $fileStream.Length
            if ($length -le 0) {
                return ''
            }

            $bytesToRead = [int][Math]::Min([int64]$MaxBytes, $length)
            $start = [Math]::Max(0, $length - $bytesToRead)
            $null = $fileStream.Seek($start, [System.IO.SeekOrigin]::Begin)
            $buffer = New-Object byte[] $bytesToRead
            $offset = 0
            while ($offset -lt $bytesToRead) {
                $read = $fileStream.Read($buffer, $offset, $bytesToRead - $offset)
                if ($read -le 0) { break }
                $offset += $read
            }

            return ([System.Text.Encoding]::UTF8.GetString($buffer, 0, $offset)).Replace("`0", '')
        }
        finally {
            $fileStream.Dispose()
        }
    }
    catch {
        return ''
    }
}

function Try-ParseInvariantDecimal {
    param([string]$Text)

    $value = [decimal]::Zero
    if ([decimal]::TryParse($Text, [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$value)) {
        return $value
    }

    return $null
}

function Find-ConfigWindow {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        foreach ($window in @(Get-ProcessOwnedWindows -Process $Process)) {
            try {
                $title = [string]$window.Current.Name
                if ($title -like '*PORTFOLIO VISUALIZER Config*') {
                    return $window
                }
            }
            catch {
                continue
            }
        }

        try {
            $children = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
                [System.Windows.Automation.TreeScope]::Children,
                [System.Windows.Automation.Condition]::TrueCondition)
            for ($index = 0; $index -lt $children.Count; $index++) {
                try {
                    $child = $children.Item($index)
                    if ($null -eq $child) { continue }
                    if ($child.Current.ProcessId -ne $Process.Id) { continue }
                    if ($child.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) { continue }

                    $title = [string]$child.Current.Name
                    if ($title -like '*PORTFOLIO VISUALIZER Config*') {
                        return $child
                    }
                }
                catch {
                    continue
                }
            }
        }
        catch {}

        Start-Sleep -Milliseconds 90
    } while ((Get-Date) -lt $deadline)

    return $null
}

function Get-ProcessOwnedWindows {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process
    )

    try {
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)

        $ownedWindows = @()
        for ($index = 0; $index -lt $windows.Count; $index++) {
            try {
                $window = $windows.Item($index)
                if ($null -eq $window) { continue }
                if ($window.Current.ProcessId -ne $Process.Id) { continue }
                if ($window.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) { continue }
                if ([string]$window.Current.AutomationId -eq 'DesktopMainWindow') { continue }
                $ownedWindows += $window
            }
            catch {
                continue
            }
        }

        return @($ownedWindows)
    }
    catch {
        return @()
    }
}

function Get-ConfigBlockingDialog {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process
    )

    foreach ($window in @(Get-ProcessOwnedWindows -Process $Process)) {
        try {
            $title = [string]$window.Current.Name
            $automationId = [string]$window.Current.AutomationId
            if ($title -like '*PORTFOLIO VISUALIZER Config*' -or
                $title -like '*Validation Progress*' -or
                $automationId -eq 'DesktopMainWindow' -or
                $title -like 'DO NOT PANIC PORTFOLIO VISUALIZER*') {
                continue
            }

            $isModal = $false
            try {
                $windowPattern = $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
                if ($null -ne $windowPattern) {
                    $isModal = [bool]$windowPattern.Current.IsModal
                }
            }
            catch {}

            $textCondition = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text)
            $textNodes = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
            $messageParts = @()
            for ($index = 0; $index -lt $textNodes.Count; $index++) {
                $text = [string]$textNodes.Item($index).Current.Name
                if (-not [string]::IsNullOrWhiteSpace($text)) {
                    $messageParts += $text.Trim()
                }
            }

            $message = (($messageParts | Select-Object -Unique) -join ' ').Trim()
            if (-not $isModal -and [string]::IsNullOrWhiteSpace($title) -and [string]::IsNullOrWhiteSpace($message)) {
                continue
            }

            if (-not $isModal -and [string]::IsNullOrWhiteSpace($message)) {
                continue
            }

            return [pscustomobject]@{
                Title = $title
                Message = $message
            }
        }
        catch {
            continue
        }
    }

    return $null
}

function Close-ConfigWindowIfPresent {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        $Window
    )

    $ownedWindows = @(Get-ProcessOwnedWindows -Process $Process)
    if ($null -eq $Window -and $ownedWindows.Count -eq 0) {
        return
    }

    for ($attempt = 0; $attempt -lt 6; $attempt++) {
        $windowsToClose = @(Get-ProcessOwnedWindows -Process $Process)
        if ($null -ne $Window) {
            $windowsToClose = @($Window) + @($windowsToClose | Where-Object { $_ -ne $Window })
        }

        if ($windowsToClose.Count -eq 0) {
            return
        }

        foreach ($candidate in ($windowsToClose | Sort-Object { if ([string]$_.Current.Name -like '*PORTFOLIO VISUALIZER Config*') { 1 } else { 0 } })) {
            try {
                $windowPattern = $candidate.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
                $windowPattern.Close()
            }
            catch {
                try {
                    $candidate.SetFocus()
                    try { [System.Windows.Forms.SendKeys]::SendWait('{ENTER}') } catch {}
                    Start-Sleep -Milliseconds 160
                    try { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } catch {}
                    Start-Sleep -Milliseconds 160
                    try { [System.Windows.Forms.SendKeys]::SendWait('%{F4}') } catch {}
                }
                catch {
                    try {
                        [void](Focus-ProcessWindow -Process $Process)
                        [System.Windows.Forms.SendKeys]::SendWait('%{F4}')
                    }
                    catch {
                        try { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } catch {}
                    }
                }
            }

            Start-Sleep -Milliseconds 250
        }

        Start-Sleep -Milliseconds 150
        $remainingWindows = @(Get-ProcessOwnedWindows -Process $Process)
        if ($remainingWindows.Count -eq 0) {
            return
        }

        $Window = $remainingWindows | Where-Object { [string]$_.Current.Name -like '*PORTFOLIO VISUALIZER Config*' } | Select-Object -First 1
    }
}

function Find-DescendantByAutomationId {
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$AutomationId
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)

    try {
        return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    }
    catch {
        return $null
    }
}

function Find-DescendantByNameAndControlType {
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$ControlType
    )

    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $controlCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        $ControlType)
    $condition = New-Object System.Windows.Automation.AndCondition($nameCondition, $controlCondition)
    try {
        return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    }
    catch {
        return $null
    }
}

function Find-DescendantByNameLikeAndControlType {
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$NameLike,
        [Parameter(Mandatory = $true)]$ControlType
    )

    try {
        $items = $Root.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                $ControlType)))
        for ($index = 0; $index -lt $items.Count; $index++) {
            $item = $items.Item($index)
            if ($null -eq $item) { continue }
            $name = [string]$item.Current.Name
            if ($name -like $NameLike) {
                return $item
            }
        }
    }
    catch {}

    return $null
}

function Invoke-AutomationElement {
    param([Parameter(Mandatory = $true)]$Element)

    try {
        $invokePattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $invokePattern.Invoke()
        return $true
    }
    catch {
        return $false
    }
}

function Click-AutomationElementCenter {
    param([Parameter(Mandatory = $true)]$Element)

    try {
        $bounds = $Element.Current.BoundingRectangle
        if ($null -eq $bounds -or $bounds.Width -le 1 -or $bounds.Height -le 1) {
            return $false
        }

        $x = [int]([Math]::Round($bounds.Left + ($bounds.Width / 2.0)))
        $y = [int]([Math]::Round($bounds.Top + ($bounds.Height / 2.0)))
        [void][NativeMouseInput]::SetCursorPos($x, $y)
        Start-Sleep -Milliseconds 80
        [NativeMouseInput]::mouse_event([NativeMouseInput]::MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 40
        [NativeMouseInput]::mouse_event([NativeMouseInput]::MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
        return $true
    }
    catch {
        return $false
    }
}

function Get-ConfigStatusText {
    param(
        [Parameter(Mandatory = $true)]$Window
    )

    try {
        $statusElement = Find-DescendantByAutomationId -Root $Window -AutomationId 'ConfigStatusText'
        if ($null -ne $statusElement) {
            $status = [string]$statusElement.Current.Name
            if (-not [string]::IsNullOrWhiteSpace($status)) {
                return $status.Trim()
            }
        }

        $texts = $Window.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text)))

        for ($index = 0; $index -lt $texts.Count; $index++) {
            $text = [string]$texts.Item($index).Current.Name
            if ([string]::IsNullOrWhiteSpace($text)) { continue }
            if ($text -like '*Validation passed. Saving and closing in *' -or
                $text -like '*saved at *' -or
                $text -like '*Click Validate.*' -or
                $text -like '*Loading initial values*') {
                return $text.Trim()
            }
        }
    }
    catch {}

    return $null
}

function Validate-AndCloseConfigWindow {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        $Window
    )

    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        Close-ConfigChildWindows -MainProcessId $Process.Id
        $Window = Find-ConfigWindow -Process $Process -TimeoutSeconds 2

        if ($null -eq $Window) {
            return $true
        }

        $validateButton = Find-DescendantByAutomationId -Root $Window -AutomationId 'ConfigValidateButton'
        if ($null -eq $validateButton) {
            $validateButton = Find-DescendantByNameAndControlType -Root $Window -Name 'Validate' -ControlType ([System.Windows.Automation.ControlType]::Button)
        }
        if ($null -eq $validateButton) {
            $script:summary.Notes += 'Validate button could not be located in the config window.'
            break
        }

        try { $validateButton.SetFocus() } catch {}
        Start-Sleep -Milliseconds 25
        try { [System.Windows.Forms.SendKeys]::SendWait(' ') } catch {}
        Start-Sleep -Milliseconds 25
        try { [System.Windows.Forms.SendKeys]::SendWait('{ENTER}') } catch {}
        Start-Sleep -Milliseconds 60
        $invoked = Invoke-AutomationElement -Element $validateButton
        if (-not $invoked) {
            $invoked = Click-AutomationElementCenter -Element $validateButton
        }

        $deadline = (Get-Date).AddSeconds(120)
        $sawValidatedCountdown = $false
        do {
            Start-Sleep -Milliseconds 100
            $Process.Refresh()
            $blockingDialog = Get-ConfigBlockingDialog -Process $Process
            if ($null -ne $blockingDialog) {
                $script:summary.Notes += "Config validation dialog: $($blockingDialog.Title) - $($blockingDialog.Message)"
                return $false
            }

            $Window = Find-ConfigWindow -Process $Process -TimeoutSeconds 1
            if ($null -ne $Window) {
                $statusText = Get-ConfigStatusText -Window $Window
                if (-not [string]::IsNullOrWhiteSpace($statusText) -and
                    $statusText -like '*Validation passed. Saving and closing in *') {
                    $sawValidatedCountdown = $true
                }
                elseif (-not [string]::IsNullOrWhiteSpace($statusText) -and
                    $statusText -like '*saved at *') {
                    $sawValidatedCountdown = $true
                }
            }
        } while ($null -ne $Window -and (Get-Date) -lt $deadline)

        if ($null -eq $Window) {
            return $true
        }

        if ($sawValidatedCountdown) {
            $script:summary.Notes += 'Observed validation success countdown, but config window still remained after timeout.'
        }
    }

    return $false
}

function Expand-AutomationElement {
    param([Parameter(Mandatory = $true)]$Element)

    try {
        $expandPattern = $Element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $expandPattern.Expand()
        return $true
    }
    catch {
        return $false
    }
}

function Write-ReferenceSpotCheckComparison {
    param(
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)]$Payload
    )

    $comparison = [ordered]@{
        CapturedAt = $Payload.CapturedAt
        CaptureIndex = $Payload.CaptureIndex
        Source = 'DisplayedVsReferenceFeed'
        ReferenceSource = $Payload.Source
        Comparisons = @()
    }

    $resultMap = @{}
    foreach ($result in @($Payload.Results)) {
        if ($null -eq $result.Symbol) { continue }
        $resultMap[[string]$result.Symbol] = $result
    }

    foreach ($displayed in @($Payload.DisplayedSample)) {
        $symbol = [string]$displayed.Symbol
        $state = [string]$displayed.State

        if (-not $resultMap.ContainsKey($symbol)) {
            $comparison.Comparisons += [ordered]@{
                Symbol = $symbol
                State = $state
                Status = 'reference-missing'
            }
            continue
        }

        $reference = $resultMap[$symbol]
        $entry = [ordered]@{
            Symbol = $symbol
            State = $state
            DisplayedLast = [string]$displayed.LastText
            ReferenceLast = $reference.Last
        }

        if ($state -ne 'live') {
            $entry.Status = 'waiting'
            $comparison.Comparisons += $entry
            continue
        }

        $displayedValue = Try-ParseInvariantDecimal -Text ([string]$displayed.LastText)
        $referenceValue = $reference.Last
        if ($null -eq $displayedValue -or $null -eq $referenceValue) {
            $entry.Status = 'unparsable'
            $comparison.Comparisons += $entry
            continue
        }

        $absDiff = [Math]::Abs([decimal]$displayedValue - [decimal]$referenceValue)
        $pctDiff = if ([decimal]$referenceValue -ne 0) {
            [Math]::Abs(([double](([decimal]$displayedValue - [decimal]$referenceValue) / [decimal]$referenceValue)))
        }
        else {
            0.0
        }

        $entry.AbsoluteDifference = [decimal]::Round($absDiff, 4)
        $entry.PercentDifference = [Math]::Round($pctDiff * 100.0, 4)
        $entry.Status = if ($absDiff -le ([decimal]0.05) -or $pctDiff -le 0.0035) { 'close' } else { 'drift' }
        $comparison.Comparisons += $entry
    }

    Add-Content -LiteralPath $OutputPath -Value ($comparison | ConvertTo-Json -Compress) -Encoding UTF8
}

function Get-ReferenceSpotCheckResults {
    param(
        [Parameter(Mandatory = $true)][string[]]$Symbols
    )

    $twelveDataEnv = Get-Item env:PORTFOLIOSAVER_TWELVEDATA_API_KEY -ErrorAction SilentlyContinue
    $twelveDataApiKey = ''
    if ($null -ne $twelveDataEnv -and $null -ne $twelveDataEnv.Value) {
        $twelveDataApiKey = [string]$twelveDataEnv.Value
    }
    $twelveDataApiKey = $twelveDataApiKey.Trim()
    if (-not [string]::IsNullOrWhiteSpace($twelveDataApiKey)) {
        return Get-TwelveDataReferenceResults -Symbols $Symbols -ApiKey $twelveDataApiKey
    }

    return Get-YahooReferenceResults -Symbols $Symbols
}

function Get-TwelveDataReferenceResults {
    param(
        [Parameter(Mandatory = $true)][string[]]$Symbols,
        [Parameter(Mandatory = $true)][string]$ApiKey
    )

    $results = @()
    $errors = @()

    foreach ($symbol in $Symbols) {
        try {
            $encodedSymbol = [Uri]::EscapeDataString($symbol)
            $url = "https://api.twelvedata.com/quote?symbol=$encodedSymbol&apikey=$ApiKey"
            $quote = Invoke-RestMethod -Uri $url -TimeoutSec 20 -Headers @{ 'User-Agent' = 'PortfolioSaverVmHarness/1.0' }
            if ($null -ne $quote.code) {
                $message = $quote.code
                if ($null -ne $quote.message -and -not [string]::IsNullOrWhiteSpace([string]$quote.message)) {
                    $message = $quote.message
                }
                $errors += ([string]$message)
                continue
            }

            $lastText = $quote.price
            if ($null -ne $quote.close -and -not [string]::IsNullOrWhiteSpace([string]$quote.close)) {
                $lastText = $quote.close
            }
            $last = Try-ParseInvariantDecimal -Text ([string]$lastText)
            if ($null -eq $last) {
                $errors += "No parseable last value for $symbol from Twelve Data."
                continue
            }

            $changePercent = Try-ParseInvariantDecimal -Text ([string]$quote.percent_change)
            $results += [pscustomobject]@{
                Symbol = [string]$symbol
                Last = $last
                ChangePercent = $changePercent
                MarketTime = [string]$quote.datetime
                Currency = [string]$quote.currency
            }
        }
        catch {
            $errors += ([string]$_.Exception.Message)
        }
    }

    return [pscustomobject]@{
        Source = 'TwelveDataQuote'
        Results = @($results)
        Error = if ($errors.Count -gt 0 -and $results.Count -eq 0) { ($errors -join ' | ') } else { $null }
    }
}

function Get-YahooReferenceResults {
    param(
        [Parameter(Mandatory = $true)][string[]]$Symbols
    )

    $results = @()
    $error = $null

    try {
        $encodedSymbols = [Uri]::EscapeDataString(($Symbols -join ','))
        $response = Invoke-RestMethod -Uri ("https://query1.finance.yahoo.com/v7/finance/quote?symbols=$encodedSymbols") -TimeoutSec 20 -Headers @{ 'User-Agent' = 'PortfolioSaverVmHarness/1.0' }
        foreach ($quote in ($response.quoteResponse.result | Where-Object { $_ -ne $null })) {
            $results += [pscustomobject]@{
                Symbol = [string]$quote.symbol
                Last = $quote.regularMarketPrice
                ChangePercent = $quote.regularMarketChangePercent
                MarketTime = if ($quote.regularMarketTime) { [DateTimeOffset]::FromUnixTimeSeconds([long]$quote.regularMarketTime).ToString('o') } else { $null }
                Currency = [string]$quote.currency
            }
        }
    }
    catch {
        $error = $_.Exception.Message
    }

    return [pscustomobject]@{
        Source = 'YahooFinanceQuote'
        Results = @($results)
        Error = $error
    }
}

$summary.ExportMode = 'LocalWorkspace'
$summary.ResultName = $resultName
$summary.ResultPath = $results
Write-SummaryFiles
Start-Transcript -Path $logPath -Force | Out-Null

try {
    Reset-PortfolioTraceRoot
    $displayApply = Try-ApplyDisplayResolution -Width $DisplayWidth -Height $DisplayHeight
    $summary.DisplayResolutionChange = $displayApply
    if ($displayApply.PSObject.Properties.Name -contains 'AvailableModes') {
        $summary.SupportedDisplayModes = @(Format-DisplayModeNames -Modes $displayApply.AvailableModes)
    }
    $summary.RuntimeDesktopResolution = Get-CurrentVirtualScreenSize
    if ($DisplayWidth -gt 0 -and $DisplayHeight -gt 0 -and -not $displayApply.Applied) {
        $summary.Notes += "Requested display resolution ${DisplayWidth}x${DisplayHeight} could not be applied; result code $($displayApply.ResultCode)."
    }
    elseif ($DisplayWidth -gt 0 -and $DisplayHeight -gt 0) {
        $summary.Notes += "Requested display resolution ${DisplayWidth}x${DisplayHeight} applied before UX run."
    }
    Write-SummaryFiles
    Get-Process PortfolioSaver.Config,PortfolioSaver.Desktop,PortfolioSaver.Screensaver -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    $desktop = $null
    $window = $null
    $configClosedNaturally = $false

    try {
        $desktop = Start-Process -FilePath $desktopExe -PassThru
        Start-Sleep -Milliseconds 900
        $desktopWindow = Get-ProcessWindowElement -Process $desktop -TimeoutSeconds 15
        if ($null -eq $desktopWindow) {
            throw 'Could not locate desktop shell window via UI Automation.'
        }

        [void](Focus-ProcessWindow -Process $desktop)
        $optionsMenuItem = Find-DescendantByAutomationId -Root $desktopWindow -AutomationId 'OptionsMenuRoot'
        if ($null -ne $optionsMenuItem) {
            [void](Expand-AutomationElement -Element $optionsMenuItem)
            Start-Sleep -Milliseconds 50
        }

        $settingsMenuItem = Find-DescendantByAutomationId -Root $desktopWindow -AutomationId 'OptionsSettingsMenuItem'
        if ($null -eq $settingsMenuItem) {
            try {
                [System.Windows.Forms.SendKeys]::SendWait('%o')
                Start-Sleep -Milliseconds 40
                [System.Windows.Forms.SendKeys]::SendWait('s')
                Start-Sleep -Milliseconds 40
            }
            catch {}
        }
        else {
            if (-not (Invoke-AutomationElement -Element $settingsMenuItem)) {
                try {
                    [System.Windows.Forms.SendKeys]::SendWait('%o')
                    Start-Sleep -Milliseconds 40
                    [System.Windows.Forms.SendKeys]::SendWait('s')
                    Start-Sleep -Milliseconds 40
                }
                catch {
                    throw 'Failed to invoke Settings menu item via UI Automation.'
                }
            }
        }

        Start-Sleep -Milliseconds 250
        $window = Find-ConfigWindow -Process $desktop -TimeoutSeconds 20
        if ($null -eq $window) { throw 'Could not locate config window via UI Automation.' }
        if ([string]$window.Current.Name -like '*BETA-5.6*' -or
            [string]$window.Current.HelpText -like '*0.9.0-beta5.6*') {
            $summary.ConfigVersionCheck = "Passed"
        }
        else {
            $summary.ConfigVersionCheck = "Failed"
            $summary.Notes += "Config window title missing expected BETA-5.6 marker: '$([string]$window.Current.Name)'"
        }

        $tabs = Get-TabItems -Window $window
        if ($tabs.Count -eq 0) { throw 'No tab items found in config window.' }
        $tabNames = @(
            $tabs |
                ForEach-Object {
                    try { [string]$_.Current.Name } catch { $null }
                } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )

        $shotIndex = 1
        foreach ($rawTabName in $tabNames) {
            $window = Find-ConfigWindow -Process $desktop -TimeoutSeconds 2
            if ($null -eq $window) {
                throw 'Config window disappeared during tab traversal.'
            }

            $tab = Find-TabItemByName -Window $window -TabName $rawTabName
            if ($null -eq $tab) {
                $summary.Notes += "Could not reacquire tab '$rawTabName'; skipping."
                continue
            }

            if ($shotIndex -gt 1) {
                [void](Select-TabItem -Tab $tab)
            }
            Start-Sleep -Milliseconds 50
            $tabName = ($rawTabName -replace '[^A-Za-z0-9_-]','_')
            Capture-Screen -Path (Join-Path $results ("config-tab-{0:D3}-{1}.png" -f $shotIndex, $tabName))
            $summary.ConfigShots++
            $shotIndex++

            $scrolled = Perform-VisibleConfigActivity -Window $window -TabName $rawTabName
            if ($scrolled) {
                Start-Sleep -Milliseconds 80
                Capture-Screen -Path (Join-Path $results ("config-tab-{0:D3}-{1}-scrolled.png" -f $shotIndex, $tabName))
                $summary.ConfigShots++
                $shotIndex++
                Send-KeySequence -Keys @('{PGUP}','{HOME}') -DelayMilliseconds 70
                Start-Sleep -Milliseconds 60
            }
        }

        $configClosedNaturally = Validate-AndCloseConfigWindow -Process $desktop -Window $window
        if ($configClosedNaturally) {
            $window = Find-ConfigWindow -Process $desktop -TimeoutSeconds 2
            if ($null -ne $window) {
                $configClosedNaturally = $false
            }
        }

        if (-not $configClosedNaturally) {
            $summary.Notes += 'Validate did not close the config window automatically; falling back to forced close.'
            throw 'Validate did not close the config window automatically.'
        }
        else {
            $window = $null
        }

        $summary.ConfigPhaseStatus = "Completed"
        Write-SummaryFiles
    }
    catch {
        $summary.ConfigPhaseStatus = "Failed"
        $position = if ($_.InvocationInfo -and -not [string]::IsNullOrWhiteSpace($_.InvocationInfo.PositionMessage)) {
            $_.InvocationInfo.PositionMessage.Trim()
        }
        else {
            'position unavailable'
        }
        $summary.Notes += "Config phase error: $($_.Exception.Message) @ $position"
        Write-SummaryFiles
    }
    finally {
        if (-not $configClosedNaturally) {
            Close-ConfigWindowIfPresent -Process $desktop -Window $window
            Start-Sleep -Milliseconds 90
        }
    }

    try {
        if ($null -eq $desktop -or $desktop.HasExited) {
            throw 'Desktop process was not running after config phase.'
        }

        Start-Sleep -Milliseconds 150
        $desktopWindow = Get-ProcessWindowElement -Process $desktop -TimeoutSeconds 15
        if ($null -eq $desktopWindow) {
            throw 'Could not locate desktop shell window via UI Automation.'
        }
        [void](Focus-ProcessWindow -Process $desktop)
        $versionMatch = Find-ElementMetadataByProcessId `
            -ProcessId $desktop.Id `
            -AutomationIds @('ScreensaverVersionWatermark', 'ScreensaverHostWindow', 'DesktopMainWindow', 'MainWindowTitle') `
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
        [void](Focus-ProcessWindow -Process $desktop)
        $fullScreenMenuItem = Find-DescendantByAutomationId -Root $desktopWindow -AutomationId 'ViewFullScreenMenuItem'
        $fullScreenInvoked = $false
        if ($null -ne $fullScreenMenuItem) {
            $fullScreenInvoked = Invoke-AutomationElement -Element $fullScreenMenuItem
        }
        if (-not $fullScreenInvoked) {
            try { [System.Windows.Forms.SendKeys]::SendWait('{F11}') } catch {}
        }
        $fullScreenDeadline = (Get-Date).AddSeconds(8)
        do {
            Start-Sleep -Milliseconds 350
            $enteredFullScreen = Test-IsTrueFullscreen -Process $desktop
        } while (-not $enteredFullScreen -and (Get-Date) -lt $fullScreenDeadline)
        $desktopFull = Join-Path $results 'desktop-fullscreen-entry.png'
        Capture-Screen -Path $desktopFull
        $summary.DesktopShots++
        $summary.ScreensaverShots++
        $summary.DesktopPhaseStatus = "Running"
        if (-not $enteredFullScreen) {
            throw "Desktop shell did not enter true fullscreen; taskbar/work-area chrome appears to remain visible."
        }
        Write-SummaryFiles

        [void](Focus-ProcessWindow -Process $desktop)
        try { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } catch {}
        $windowedDeadline = (Get-Date).AddSeconds(8)
        do {
            Start-Sleep -Milliseconds 350
            $stillFullScreen = Test-IsTrueFullscreen -Process $desktop
        } while ($stillFullScreen -and (Get-Date) -lt $windowedDeadline)
        $desktopWindowed = Join-Path $results 'desktop-windowed-after-esc.png'
        Capture-Screen -Path $desktopWindowed
        $summary.DesktopShots++
        $summary.ScreensaverShots++
        if ($stillFullScreen) {
            throw "Desktop shell remained in fullscreen after ESC."
        }
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


