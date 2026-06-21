param(
    [ValidateRange(1, 10080)]
    [int]$ScreensaverDurationMinutes = 6,
    [ValidateRange(1, 3600)]
    [int]$CaptureIntervalSeconds = 5,
    [int]$DisplayWidth,
    [int]$DisplayHeight,
    [string]$DisplayProfile = 'default',
    [ValidateSet('Apply', 'Cancel')]
    [string]$ValidationCompletionMode = 'Apply',
    [ValidateSet('none', 'offline-at-start', 'offline-during-config-validation', 'offline-during-runtime', 'offline-then-recover-runtime', 'high-latency-yfinance', 'upstream-throttled', 'timeout')]
    [string]$FaultProfile = 'none',
    [string]$RootPath = (Join-Path $env:USERPROFILE 'Desktop\PortfolioVmUx'),
    [string]$ResultName = ('ux-deep-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string]$ResultRootPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'VmTraceQuoteEvidence.ps1')
if (-not (Test-YFinanceQuoteEvidenceParser)) {
    throw 'YFinance trace quote parser self-test failed.'
}

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
using System.Text;
public static class NativeWindowSearch {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public static IntPtr FindVisibleWindowByProcessAndTitleFragment(int processId, string titleFragment) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr hWnd, IntPtr lParam) {
            if (!IsWindowVisible(hWnd)) {
                return true;
            }

            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (processId > 0 && (int)pid != processId) {
                return true;
            }

            int length = GetWindowTextLength(hWnd);
            if (length <= 0) {
                return true;
            }

            var builder = new StringBuilder(length + 1);
            GetWindowText(hWnd, builder, builder.Capacity);
            string title = builder.ToString();
            if (title.IndexOf(titleFragment, StringComparison.OrdinalIgnoreCase) >= 0) {
                found = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }
}
"@
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeMouseInput {
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;

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
$screensaverExe = Join-Path $root 'publish\screensaver\PortfolioSaver.Screensaver.exe'
if ([string]::IsNullOrWhiteSpace($ResultRootPath)) {
    $ResultRootPath = Join-Path $root 'results'
}
$resultName = $ResultName
$results = Join-Path $ResultRootPath $resultName

New-Item -ItemType Directory -Force -Path $results | Out-Null
foreach ($orphanedSummaryTempPath in [System.IO.Directory]::EnumerateFiles($results, 'ux-deep-summary.json.*.tmp')) {
    [System.IO.File]::Delete($orphanedSummaryTempPath)
}
foreach ($orphanedSummaryTempPath in [System.IO.Directory]::EnumerateFiles($results, 'vm-ux-summary.json.*.tmp')) {
    [System.IO.File]::Delete($orphanedSummaryTempPath)
}

$isLongRunSoak = $ScreensaverDurationMinutes -ge 120
$script:vmBackgroundChangeSeconds = 120
$effectiveCaptureIntervalSeconds = if ($ScreensaverDurationMinutes -ge 120 -and $CaptureIntervalSeconds -lt 30) { 30 } else { $CaptureIntervalSeconds }
# Informational estimate for summaries/analyzers; the actual capture loop is
# wall-clock bounded so slow screenshots cannot extend the VM run indefinitely.
$targetFrames = [Math]::Max(1, [int][Math]::Ceiling(($ScreensaverDurationMinutes * 60.0) / $effectiveCaptureIntervalSeconds))

if ($ScreensaverDurationMinutes -le 0) {
    throw "ScreensaverDurationMinutes must be greater than zero."
}
$previousDisableInputExit = $null
$previousFaultProfilePath = $null
$previousFaultProfile = $null
Remove-Item Env:DNPPV_YFINANCE_FAULT_PROFILE_PATH -ErrorAction SilentlyContinue
Remove-Item Env:DNPPV_YFINANCE_FAULT_PROFILE -ErrorAction SilentlyContinue
$faultProfilePath = Join-Path $results 'yfinance-fault-profile.json'
$faultTimelinePath = Join-Path $results 'fault-injection-events.log'

$script:configWindowTracePath = Join-Path $results 'config-window-events.log'
$script:cachedDisplayModes = $null
$script:cachedDisplayModesTimestamp = $null
Set-Content -LiteralPath $script:configWindowTracePath -Value '' -Encoding UTF8
Set-Content -LiteralPath $faultTimelinePath -Value '' -Encoding UTF8

function Write-FaultInjectionTrace {
    param(
        [Parameter(Mandatory = $true)][string]$Event,
        [string]$Details = ''
    )

    $timestamp = (Get-Date).ToString('o')
    Add-Content -LiteralPath $faultTimelinePath -Value ("{0} event={1} details={2}" -f $timestamp, $Event, $Details) -Encoding UTF8
}

function Set-YFinanceFaultProfile {
    param([Parameter(Mandatory = $true)][string]$Profile)

    $profilePayload = [ordered]@{
        profile = $Profile
        operations = @('market-data')
    }
    $faultProfileTempPath = $faultProfilePath + ('.{0}.tmp' -f [guid]::NewGuid().ToString('N'))
    try {
        $profilePayload | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $faultProfileTempPath -Encoding UTF8
        Move-Item -LiteralPath $faultProfileTempPath -Destination $faultProfilePath -Force
    }
    finally {
        if (Test-Path -LiteralPath $faultProfileTempPath) {
            Remove-Item -LiteralPath $faultProfileTempPath -Force -ErrorAction SilentlyContinue
        }
    }
    $env:DNPPV_YFINANCE_FAULT_PROFILE_PATH = $faultProfilePath
    Remove-Item Env:DNPPV_YFINANCE_FAULT_PROFILE -ErrorAction SilentlyContinue
    Write-FaultInjectionTrace -Event 'FaultProfileSet' -Details ("profile={0}; path={1}" -f $Profile, $faultProfilePath)
}

function Clear-YFinanceFaultProfile {
    Set-YFinanceFaultProfile -Profile 'none'
}

function Write-ConfigWindowTrace {
    param(
        [Parameter(Mandatory = $true)][string]$Event,
        [string]$Details = ''
    )

    $timestamp = (Get-Date).ToString('o')
    $line = if ([string]::IsNullOrWhiteSpace($Details)) {
        "$timestamp event=$Event"
    }
    else {
        "$timestamp event=$Event details=$Details"
    }

    Add-Content -LiteralPath $script:configWindowTracePath -Value $line -Encoding UTF8
}

function Get-TopLevelWindowSnapshot {
    param([int]$ProcessId = 0)

    $parts = New-Object System.Collections.Generic.List[string]
    try {
        $children = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)

        for ($index = 0; $index -lt $children.Count; $index++) {
            try {
                $child = $children.Item($index)
                if ($null -eq $child) { continue }
                $childProcessId = [int]$child.Current.ProcessId
                if ($ProcessId -gt 0 -and $childProcessId -ne $ProcessId) { continue }
                $parts.Add(("{0}|{1}|{2}|{3}" -f
                        $childProcessId,
                        [string]$child.Current.AutomationId,
                        [string]$child.Current.ControlType.ProgrammaticName,
                        [string]$child.Current.Name))
            }
            catch {
                continue
            }
        }
    }
    catch {
        return 'snapshot-error'
    }

    if ($parts.Count -eq 0) {
        return 'snapshot-empty'
    }

    return ($parts -join ' || ')
}

function Test-ConfigPhaseBudget {
    param(
        [Parameter(Mandatory = $true)][datetime]$StartedAt,
        [Parameter(Mandatory = $true)][string]$Stage
    )

    if (((Get-Date) - $StartedAt).TotalSeconds -le 60) {
        return
    }

    Write-ConfigWindowTrace -Event 'BudgetExceeded' -Details ("stage={0}; windows={1}" -f $Stage, (Get-TopLevelWindowSnapshot))
    Capture-Screen -Path (Join-Path $results 'config-phase-timeout.png')
    throw "Config phase exceeded 60 seconds during stage '$Stage'."
}

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

function Apply-HarnessSettingsOverrides {
    $appDataRoot = if (-not [string]::IsNullOrWhiteSpace($env:DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT)) {
        $env:DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:PORTFOLIOSAVER_LOCALDATA_ROOT)) {
        $env:PORTFOLIOSAVER_LOCALDATA_ROOT
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:PORTFOLIOSAVER_APPDATA_ROOT)) {
        $env:PORTFOLIOSAVER_APPDATA_ROOT
    }
    else {
        Join-Path $env:LOCALAPPDATA 'DoNotPanicPortfolioVisualizer'
    }

    New-Item -ItemType Directory -Force -Path $appDataRoot | Out-Null
    if ([string]::IsNullOrWhiteSpace((Get-ScopedEnvironmentValue -Name 'DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT')) -and
        [string]::IsNullOrWhiteSpace((Get-ScopedEnvironmentValue -Name 'PORTFOLIOSAVER_LOCALDATA_ROOT')) -and
        [string]::IsNullOrWhiteSpace((Get-ScopedEnvironmentValue -Name 'PORTFOLIOSAVER_APPDATA_ROOT'))) {
        $legacyAppDataRoot = Join-Path $env:LOCALAPPDATA 'PortfolioSaver'
        $sentinelPath = Join-Path $appDataRoot '.portfolio-visualizer-migration-complete'
        if ((Test-Path $legacyAppDataRoot) -and -not (Test-Path $sentinelPath)) {
            Copy-Item -Path (Join-Path $legacyAppDataRoot '*') -Destination $appDataRoot -Recurse -ErrorAction SilentlyContinue
            Set-Content -LiteralPath $sentinelPath -Value (Get-Date).ToString('o') -Encoding UTF8
            Write-ConfigWindowTrace -Event 'HarnessAppDataMigrationApplied' -Details ("legacy_root={0}; product_root={1}" -f $legacyAppDataRoot, $appDataRoot)
        }
    }

    $settingsPath = Join-Path $appDataRoot 'settings.json'
    $settings = @{}
    if (Test-Path $settingsPath) {
        try {
            $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -AsHashtable
        }
        catch {
            $settings = @{}
        }
    }

    $settings['BackgroundChangeSeconds'] = $script:vmBackgroundChangeSeconds
    $settings['ShuffleBackgrounds'] = $true

    $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    Write-ConfigWindowTrace -Event 'HarnessSettingsOverrideApplied' -Details ("settings_path={0}; background_seconds={1}; shuffle={2}" -f $settingsPath, $script:vmBackgroundChangeSeconds, $true)
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

function Wait-ProcessWindowElementWithFallback {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][string]$InitialTraceEvent,
        [Parameter(Mandatory = $true)][string]$FallbackTraceEvent,
        [Parameter(Mandatory = $true)][string]$CompleteTraceEvent,
        [Parameter(Mandatory = $true)][string]$LookupErrorEvent,
        [Parameter(Mandatory = $true)][string]$ExitMessage,
        [Parameter(Mandatory = $true)][string]$NotFoundMessage
    )

    $startedAt = Get-Date
    $handleObserved = Wait-UIAutomationCondition -TimeoutSeconds 5 -PollMilliseconds 100 -TraceEvent $InitialTraceEvent -Condition {
        $Process.Refresh()
        return ($Process.MainWindowHandle -ne [IntPtr]::Zero)
    }

    # Cold VM launches can return from Start-Process long before WPF reaches OnStartup.
    # In ux-deep-ssh-20260620-054408, OnStartup arrived ~64s after process launch
    # and the owned YFinance server became ready after ~75s, so keep the fast handle
    # probe above but allow enough time for first-render UI Automation discovery.
    $window = Get-ProcessWindowElement -Process $Process -TimeoutSeconds 120
    if ($null -eq $window -and -not $Process.HasExited) {
        $fallbackState = [pscustomobject]@{
            Element = $null
            Exited = $false
        }

        [void](Wait-UIAutomationCondition -TimeoutSeconds 30 -PollMilliseconds 100 -TraceEvent $FallbackTraceEvent -Condition {
            $Process.Refresh()
            if ($Process.HasExited) {
                $fallbackState.Exited = $true
                return $true
            }

            try {
                $candidate = Get-ProcessWindowElement -Process $Process -TimeoutSeconds 1
                if ($null -ne $candidate) {
                    $fallbackState.Element = $candidate
                    return $true
                }
            }
            catch {
                Write-ConfigWindowTrace -Event $LookupErrorEvent -Details $_.Exception.Message
            }

            return $false
        })

        $window = $fallbackState.Element
        if ($fallbackState.Exited) {
            throw $ExitMessage
        }
    }

    $elapsedMs = [long]((Get-Date) - $startedAt).TotalMilliseconds
    Write-ConfigWindowTrace -Event $CompleteTraceEvent -Details ("elapsed_ms={0}; handle_observed={1}; found={2}" -f $elapsedMs, [bool]$handleObserved, ($null -ne $window))
    if ($Process.HasExited) {
        throw $ExitMessage
    }

    if ($null -eq $window) {
        throw $NotFoundMessage
    }

    return $window
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

    try {
        $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $TabName)
        $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::TabItem)
        $condition = New-Object System.Windows.Automation.AndCondition($nameCondition, $typeCondition)
        return $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    }
    catch {
        return $null
    }
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
        $scrollCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty,
            $true)
        $all = $Window.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            $scrollCondition)

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

function Invoke-MouseWheelScroll {
    param(
        [Parameter(Mandatory = $true)]$Element,
        [int]$Notches = 3,
        [int]$DelayMilliseconds = 120
    )

    try {
        $rect = $Element.Current.BoundingRectangle
        if ($rect.Width -le 0 -or $rect.Height -le 0) {
            return $false
        }

        $x = [int]([Math]::Round($rect.Left + ($rect.Width / 2.0)))
        $y = [int]([Math]::Round($rect.Top + ($rect.Height / 2.0)))
        [void][NativeMouseInput]::SetCursorPos($x, $y)
        Start-Sleep -Milliseconds 40

        for ($index = 0; $index -lt [Math]::Max(1, $Notches); $index++) {
            [NativeMouseInput]::mouse_event([NativeMouseInput]::MOUSEEVENTF_WHEEL, 0, 0, [uint32](-120), [UIntPtr]::Zero)
            Start-Sleep -Milliseconds $DelayMilliseconds
        }

        return $true
    }
    catch {
        return $false
    }
}

function Invoke-WindowViewportWheelScroll {
    param(
        [Parameter(Mandatory = $true)]$Window,
        [int]$Notches = 3,
        [int]$DelayMilliseconds = 90
    )

    try {
        $rect = $Window.Current.BoundingRectangle
        if ($rect.Width -le 0 -or $rect.Height -le 0) {
            return $false
        }

        $x = [int]([Math]::Round($rect.Right - 24))
        $y = [int]([Math]::Round($rect.Top + ($rect.Height / 2.0)))
        [void][NativeMouseInput]::SetCursorPos($x, $y)
        Start-Sleep -Milliseconds 30

        for ($index = 0; $index -lt [Math]::Max(1, $Notches); $index++) {
            [NativeMouseInput]::mouse_event([NativeMouseInput]::MOUSEEVENTF_WHEEL, 0, 0, [uint32](-120), [UIntPtr]::Zero)
            Start-Sleep -Milliseconds $DelayMilliseconds
        }

        return $true
    }
    catch {
        return $false
    }
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

function Perform-VisibleScrollSequence {
    param(
        [Parameter(Mandatory = $true)]$Window,
        [Parameter(Mandatory = $true)][string]$TabName,
        [int]$PageDownCount = 1
    )

    $didScroll = Invoke-WindowViewportWheelScroll -Window $Window -Notches ([Math]::Max(2, $PageDownCount + 1)) -DelayMilliseconds 45
    if (-not $didScroll) {
        $didScroll = Try-ScrollWindowContent -Window $Window -TabName $TabName -PageCount ([Math]::Max(1, [Math]::Min(2, $PageDownCount)))
    }

    return $didScroll
}

function Perform-VisibleConfigActivity {
    param(
        [Parameter(Mandatory = $true)]$Window,
        [Parameter(Mandatory = $true)][string]$TabName
    )

    try { $Window.SetFocus() } catch {}
    Start-Sleep -Milliseconds 40

    $pageDownCount = if ($TabName -eq 'Advanced') { 3 } else { 2 }
    return Perform-VisibleScrollSequence -Window $Window -TabName $TabName -PageDownCount $pageDownCount
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
    IsLongRunSoak = $isLongRunSoak
    RequestedCaptureIntervalSeconds = $CaptureIntervalSeconds
    EffectiveCaptureIntervalSeconds = $effectiveCaptureIntervalSeconds
    TargetCaptureFrames = $targetFrames
    RequestedDisplayProfile = $DisplayProfile
    RequestedDisplayWidth = if ($DisplayWidth -gt 0) { $DisplayWidth } else { $null }
    RequestedDisplayHeight = if ($DisplayHeight -gt 0) { $DisplayHeight } else { $null }
    SupportedDisplayModes = @()
}

if ($effectiveCaptureIntervalSeconds -ne $CaptureIntervalSeconds) {
    $summary.Notes += "Capture interval raised from $CaptureIntervalSeconds to $effectiveCaptureIntervalSeconds seconds for long-run soak stability."
}
if ($isLongRunSoak) {
    $summary.Notes += "Long-run soak mode enabled; fullscreen soak will switch to the legacy screensaver host after config apply."
}

$summaryPath = Join-Path $results 'ux-deep-summary.json'
$legacySummaryPath = Join-Path $results 'vm-ux-summary.json'
$logPath = Join-Path $results 'ux-deep-run.log'
$referenceSpotCheckPath = Join-Path $results 'reference-spot-checks.jsonl'
$referenceComparisonPath = Join-Path $results 'reference-spot-check-comparisons.jsonl'

function Write-SummaryFiles {
    $json = $summary | ConvertTo-Json -Depth 6
    $legacySummaryWritten = $false
    $primarySummaryWriteException = $null
    $legacySummaryWriteException = $null

    # Summary writes happen frequently during UX runs. Keep this call-site
    # budget intentionally small so transient contention fails fast instead of
    # blocking for seconds; at least one summary file must still update.
    $summaryWriteAttempts = 3
    $summaryWriteRetryDelayMilliseconds = 50

    try {
        Write-TextFileWithRetry -Path $summaryPath -Content $json -MaxAttempts $summaryWriteAttempts -RetryDelayMilliseconds $summaryWriteRetryDelayMilliseconds
    }
    catch {
        $primarySummaryWriteException = $_.Exception
        $summary.Notes += "Primary UX summary write failed after bounded retries: $($primarySummaryWriteException.Message)"
        Write-Warning ("Unable to update summary file '{0}' after bounded retries: {1}" -f $summaryPath, $_.Exception.Message)
    }

    try {
        Write-TextFileWithRetry -Path $legacySummaryPath -Content $json -MaxAttempts $summaryWriteAttempts -RetryDelayMilliseconds $summaryWriteRetryDelayMilliseconds
        $legacySummaryWritten = $true
    }
    catch {
        $legacySummaryWriteException = $_.Exception
        $summary.Notes += "Legacy UX summary write failed after bounded retries: $($legacySummaryWriteException.Message)"
        Write-Warning ("Unable to update legacy summary file '{0}' after bounded retries: {1}" -f $legacySummaryPath, $_.Exception.Message)
    }

    if ($null -ne $primarySummaryWriteException -and -not $legacySummaryWritten) {
        if ($null -ne $legacySummaryWriteException) {
            throw [System.AggregateException]::new(
                "Both UX summary writes failed after bounded retries.",
                [System.Exception[]]@($primarySummaryWriteException, $legacySummaryWriteException))
        }

        throw $primarySummaryWriteException
    }
}

function Write-TextFileWithRetry {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content,
        [ValidateRange(1, 20)]
        [int]$MaxAttempts = 20,
        [ValidateRange(1, 1000)]
        [int]$RetryDelayMilliseconds = 80
    )

    $attempts = 0
    while ($true) {
        $tempPath = $Path + ('.{0}.tmp' -f [guid]::NewGuid().ToString('N'))
        try {
            # The VM harness runs under PowerShell 7; Set-Content -Encoding UTF8 writes UTF-8 without BOM there.
            $encoding = [System.Text.UTF8Encoding]::new($false)
            [System.IO.File]::WriteAllText($tempPath, $Content, $encoding)
            try {
                [System.IO.File]::Replace($tempPath, $Path, [NullString]::Value)
            }
            catch [System.IO.FileNotFoundException] {
                [System.IO.File]::Move($tempPath, $Path)
            }
            return
        }
        catch {
            if ([System.IO.File]::Exists($tempPath)) {
                [System.IO.File]::Delete($tempPath)
            }

            $attempts++
            if ($attempts -ge $MaxAttempts) {
                throw
            }

            Write-Verbose ("Retrying text file replacement for {0}; attempt {1} of {2} failed: {3}" -f $Path, $attempts, $MaxAttempts, $_.Exception.Message)
            Start-Sleep -Milliseconds $RetryDelayMilliseconds
        }
    }
}

function Reset-PortfolioTraceRoot {
    $harnessAppDataRoot = Get-HarnessAppDataRoot
    if ([string]::IsNullOrWhiteSpace($harnessAppDataRoot)) {
        throw 'Harness app data root is empty; cannot reset trace root.'
    }

    $traceRoots = @(
        (Join-Path $harnessAppDataRoot 'Trace'),
        (Join-Path $env:LOCALAPPDATA 'PortfolioSaver\Trace')
    ) | Select-Object -Unique

    foreach ($traceRoot in $traceRoots) {
        if (Test-Path $traceRoot) {
            Remove-Item -LiteralPath $traceRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $harnessAppDataRoot 'Trace') | Out-Null
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

function Get-CachedDisplayModes {
    if ($null -ne $script:cachedDisplayModes -and
        $null -ne $script:cachedDisplayModesTimestamp -and
        (([datetime]::UtcNow - $script:cachedDisplayModesTimestamp).TotalSeconds -lt 300)) {
        return @($script:cachedDisplayModes)
    }

    $startedAt = [datetime]::UtcNow
    $modes = @(Get-CimSupportedDisplayModes)
    if ($modes.Count -eq 0) {
        $modes = @(Get-AvailableDisplayModes)
    }

    $elapsedMs = [int](([datetime]::UtcNow - $startedAt).TotalMilliseconds)
    if ($modes.Count -gt 0) {
        $script:cachedDisplayModes = @($modes)
        $script:cachedDisplayModesTimestamp = [datetime]::UtcNow
    }
    if ($elapsedMs -gt 2000) {
        Write-Warning ("Display mode enumeration cache initialization took {0} ms for {1} mode(s)." -f $elapsedMs, $modes.Count)
    }

    return @($modes)
}

function Clear-CachedDisplayModes {
    $script:cachedDisplayModes = $null
    $script:cachedDisplayModesTimestamp = $null
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

function Get-TopLevelWindowsForProcess {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process
    )

    try {
        $children = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)

        $windows = @()
        for ($index = 0; $index -lt $children.Count; $index++) {
            try {
                $child = $children.Item($index)
                if ($null -eq $child) { continue }
                if ($child.Current.ProcessId -ne $Process.Id) { continue }
                if ($child.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) { continue }
                $windows += $child
            }
            catch {
                continue
            }
        }

        return @($windows)
    }
    catch {
        return @()
    }
}

function Find-Win32TopLevelWindowLike {
    param(
        [int]$ProcessId = 0,
        [Parameter(Mandatory = $true)][string]$TitleFragment
    )

    try {
        $handle = [NativeWindowSearch]::FindVisibleWindowByProcessAndTitleFragment($ProcessId, $TitleFragment)
        if ($handle -eq [IntPtr]::Zero) {
            return @()
        }

        $window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
        if ($null -eq $window) {
            return @()
        }

        return @([pscustomobject]@{
            Handle = $handle
            ProcessId = [int]$window.Current.ProcessId
            Title = [string]$window.Current.Name
        })
    }
    catch {
        return @()
    }
}

function Test-AutomationElementAlive {
    param($Element)

    if ($null -eq $Element) {
        return $false
    }

    try {
        $null = $Element.Current.ProcessId
        return $true
    }
    catch {
        return $false
    }
}

function Wait-UIAutomationCondition {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Condition,
        [ValidateRange(1, 2147483647)]
        [int]$TimeoutSeconds = 10,
        [ValidateRange(1, 3600000)]
        [int]$PollMilliseconds = 100,
        [string]$TraceEvent = ''
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $pollCount = 0
    do {
        $pollCount++
        try {
            $result = & $Condition
            if ($result -is [bool]) {
                if ($result) {
                    return $true
                }
            }
            elseif ($null -ne $result) {
                return $result
            }
        }
        catch {
            if (-not [string]::IsNullOrWhiteSpace($TraceEvent) -and
                ($pollCount -eq 1 -or ($pollCount % 10) -eq 0)) {
                Write-ConfigWindowTrace -Event ($TraceEvent + 'Error') -Details $_.Exception.Message
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($TraceEvent) -and
            ($pollCount -eq 1 -or ($pollCount % 10) -eq 0)) {
            Write-ConfigWindowTrace -Event $TraceEvent -Details ("poll={0}; timeout_seconds={1}" -f $pollCount, $TimeoutSeconds)
        }

        Start-Sleep -Milliseconds $PollMilliseconds
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
                AvailableModes = @(Format-DisplayModeNames -Modes (Get-CachedDisplayModes))
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
                AvailableModes = @(Format-DisplayModeNames -Modes (Get-CachedDisplayModes))
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
            $availableModes = @(Format-DisplayModeNames -Modes (Get-CachedDisplayModes))
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
        if ($applied) {
            Clear-CachedDisplayModes
        }
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
            AvailableModes = @(Format-DisplayModeNames -Modes (Get-CachedDisplayModes))
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
    $availableModes = @(Get-CachedDisplayModes)

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
    if ($payload.Applied) {
        Clear-CachedDisplayModes
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
    $referenceWarning = $null
    $referenceStatus = 'unknown'

    try {
        $reference = Get-ReferenceSpotCheckResults -Symbols $symbols
        if ($null -ne $reference -and -not [string]::IsNullOrWhiteSpace([string]$reference.Source)) {
            $referenceSource = [string]$reference.Source
        }

        if ($null -ne $reference -and $null -ne $reference.Results) {
            $referenceResults = @($reference.Results)
        }

        if ($null -ne $reference -and -not [string]::IsNullOrWhiteSpace([string]$reference.Status)) {
            $referenceStatus = [string]$reference.Status
        }

        if ($null -ne $reference -and -not [string]::IsNullOrWhiteSpace([string]$reference.Warning)) {
            $referenceWarning = [string]$reference.Warning
        }
    }
    catch {
        $referenceError = $_.Exception.Message
    }

    $payload = [pscustomobject]@{
        ComparisonSchemaVersion = $ReferenceComparisonSchemaVersion
        CapturedAt = (Get-Date).ToString('o')
        CaptureIndex = $CaptureIndex
        Source = $referenceSource
        Symbols = $symbols
        DisplayedSample = @($displayedSample)
        Results = @($referenceResults)
        Status = $referenceStatus
        Error = $referenceError
        Warning = $referenceWarning
        YFinanceEvidenceStatus = if (Test-YFinanceTraceEvidencePresent) { 'present' } else { 'missing' }
    }

    Add-Content -LiteralPath $OutputPath -Value ($payload | ConvertTo-Json -Compress) -Encoding UTF8
    Write-ReferenceSpotCheckComparison -OutputPath $referenceComparisonPath -Payload $payload
}

function Test-YFinanceTraceEvidencePresent {
    return (Read-YFinanceTraceText) -match 'event=QuoteResponseObserved'
}

function Read-YFinanceTraceText {
    $tracePath = Get-HarnessTracePath -RelativePath 'Trace\yfinance.circular.log'
    if (-not (Test-Path $tracePath)) {
        return ''
    }

    return Read-TextFileTailShared -Path $tracePath -MaxBytes 2097152
}

function Get-HarnessAppDataRoot {
    $localRoot = Get-ScopedEnvironmentValue -Name 'DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT'
    if ([string]::IsNullOrWhiteSpace($localRoot)) {
        $localRoot = Get-ScopedEnvironmentValue -Name 'PORTFOLIOSAVER_LOCALDATA_ROOT'
    }
    if ([string]::IsNullOrWhiteSpace($localRoot)) {
        $localRoot = Get-ScopedEnvironmentValue -Name 'PORTFOLIOSAVER_APPDATA_ROOT'
    }

    if ([string]::IsNullOrWhiteSpace($localRoot)) {
        $localRoot = Join-Path $env:LOCALAPPDATA 'DoNotPanicPortfolioVisualizer'
    }

    return $localRoot
}

function Get-HarnessTracePath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $primaryPath = Join-Path (Get-HarnessAppDataRoot) $RelativePath
    if (Test-Path $primaryPath) {
        return $primaryPath
    }

    if ([string]::IsNullOrWhiteSpace((Get-ScopedEnvironmentValue -Name 'DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT')) -and
        [string]::IsNullOrWhiteSpace((Get-ScopedEnvironmentValue -Name 'PORTFOLIOSAVER_LOCALDATA_ROOT')) -and
        [string]::IsNullOrWhiteSpace((Get-ScopedEnvironmentValue -Name 'PORTFOLIOSAVER_APPDATA_ROOT'))) {
        $legacyPath = Join-Path (Join-Path $env:LOCALAPPDATA 'PortfolioSaver') $RelativePath
        if (Test-Path $legacyPath) {
            return $legacyPath
        }
    }

    return $primaryPath
}

function Get-ScopedEnvironmentValue {
    param([Parameter(Mandatory = $true)][string]$Name)

    foreach ($scope in 'Process', 'User', 'Machine') {
        $value = [Environment]::GetEnvironmentVariable($Name, $scope)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }

    return ''
}

function Get-LatestDisplayedTapeSample {
    $tracePath = Get-HarnessTracePath -RelativePath 'Trace\trace.circular.log'
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

function Get-CurrentYFinanceFaultProfile {
    param([Parameter(Mandatory = $true)][string]$ProfilePath)

    if ([string]::IsNullOrWhiteSpace($ProfilePath) -or -not (Test-Path -LiteralPath $ProfilePath)) {
        return 'none'
    }

    try {
        $stream = [System.IO.File]::Open($ProfilePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $reader = [System.IO.StreamReader]::new($stream)
            try {
                $faultProfileJson = $reader.ReadToEnd() | ConvertFrom-Json
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
        $profile = [string]$faultProfileJson.profile
        if ([string]::IsNullOrWhiteSpace($profile)) {
            return 'none'
        }

        return $profile
    }
    catch {
        Write-Warning ("Runtime freshness snapshot could not read fault profile '{0}': {1}" -f $ProfilePath, $_.Exception.Message)
        return 'unknown'
    }
}

function Get-KnownRuntimeFreshnessValues {
    return @(
        'LIVE quote feed',
        'OFFLINE - showing last values',
        'OFFLINE - waiting for data',
        'STALE - cached values present',
        'LOADING - waiting for data'
    )
}

function Get-VisibleRuntimeFreshnessText {
    param([Parameter(Mandatory = $true)][System.Diagnostics.Process]$DesktopProcess)

    # UI Automation is available only from the interactive desktop session. If it
    # cannot inspect the visual host, callers fall back to trace-derived state.
    $knownFreshnessValues = Get-KnownRuntimeFreshnessValues
    try {
        if ($DesktopProcess.HasExited) {
            return ''
        }

        $window = Get-ProcessWindowElement -Process $DesktopProcess -TimeoutSeconds 1
        if ($null -eq $window) {
            return ''
        }

        $freshnessElement = Find-DescendantByAutomationId -Root $window -AutomationId 'RuntimeDataFreshnessText'
        if ($null -ne $freshnessElement) {
            $freshnessText = ([string]$freshnessElement.Current.Name).Trim()
            if ($knownFreshnessValues -contains $freshnessText) {
                return $freshnessText
            }
        }

        $textCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)
        $textNodes = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
        if ($null -eq $textNodes) {
            return ''
        }

        $visibleMatches = @()
        for ($index = 0; $index -lt $textNodes.Count; $index++) {
            $node = $textNodes.Item($index)
            $text = ([string]$node.Current.Name).Trim()
            if ($knownFreshnessValues -notcontains $text) {
                continue
            }

            $isOffscreen = $true
            $hasBounds = $false
            try { $isOffscreen = [bool]$node.Current.IsOffscreen } catch {}
            try {
                $rect = $node.Current.BoundingRectangle
                $hasBounds = $rect.Width -gt 1 -and $rect.Height -gt 1
            }
            catch {}

            if (-not $isOffscreen -and $hasBounds) {
                $visibleMatches += $text
            }
        }

        if ($visibleMatches.Count -gt 0) {
            return [string]$visibleMatches[-1]
        }
    }
    catch {
        Write-Warning ("Visible runtime freshness lookup failed: {0}" -f $_.Exception.Message)
    }

    return ''
}

function Write-RuntimeFreshnessSnapshot {
    param(
        [Parameter(Mandatory = $true)][int]$CaptureIndex,
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][string]$ResultsDir,
        [Parameter(Mandatory = $true)][string]$RequestedFaultProfile,
        [Parameter(Mandatory = $true)][string]$FaultProfilePath,
        [System.Diagnostics.Process]$DesktopProcess = $null,
        [switch]$IncludeVisibleFreshness
    )

    try {
        $uiFreshnessText = ''
        if ($IncludeVisibleFreshness -and $null -ne $DesktopProcess -and -not $DesktopProcess.HasExited) {
            $uiFreshnessText = Get-VisibleRuntimeFreshnessText -DesktopProcess $DesktopProcess
        }

        $tracePath = Get-HarnessTracePath -RelativePath 'Trace\trace.circular.log'
        $latestFreshnessLine = ''
        if (Test-Path -LiteralPath $tracePath) {
            $tailText = Read-TextFileTailShared -Path $tracePath -MaxBytes 131072
            if (-not [string]::IsNullOrWhiteSpace($tailText)) {
                $freshnessLines = @(($tailText -split "`r?`n") |
                    Where-Object { $_ -like '*data_freshness_text=*' } |
                    Select-Object -Last 1)
                if ($freshnessLines.Count -gt 0) {
                    $latestFreshnessLine = $freshnessLines[0]
                }
            }
        }

        $traceFreshnessText = ''
        $traceFreshnessAgeSeconds = $null
        if (-not [string]::IsNullOrWhiteSpace($latestFreshnessLine)) {
            $knownFreshnessValues = Get-KnownRuntimeFreshnessValues
            foreach ($knownFreshnessValue in $knownFreshnessValues) {
                if ($latestFreshnessLine -like "*data_freshness_text=$knownFreshnessValue*") {
                    $traceFreshnessText = $knownFreshnessValue
                    break
                }
            }
            if ([string]::IsNullOrWhiteSpace($traceFreshnessText)) {
                $match = [regex]::Match($latestFreshnessLine, 'data_freshness_text=(.*?)(?: / |$)')
                if ($match.Success) {
                    $traceFreshnessText = $match.Groups[1].Value.Trim()
                }
            }

            $timestampMatch = [regex]::Match($latestFreshnessLine, '^(?<timestamp>\d{4}-\d{2}-\d{2}T\S+?)\s+\|')
            if ($timestampMatch.Success) {
                try {
                    $traceTimestamp = [DateTimeOffset]::Parse($timestampMatch.Groups['timestamp'].Value)
                    $traceFreshnessAgeSeconds = [Math]::Round(((Get-Date) - $traceTimestamp.LocalDateTime).TotalSeconds, 1)
                }
                catch {}
            }
        }

        $latestFreshnessText = $traceFreshnessText
        $freshnessSource = 'trace'
        if ([string]::IsNullOrWhiteSpace($latestFreshnessText)) {
            $latestFreshnessText = $uiFreshnessText
            $freshnessSource = 'ui'
        }
        # Trace is authoritative while it is moving. If the trace freshness line is
        # older than three expected capture intervals, prefer the currently visible
        # UI so a stalled trace writer does not falsely fail recovery validation.
        elseif ($null -ne $traceFreshnessAgeSeconds -and
            $traceFreshnessAgeSeconds -gt 90 -and
            -not [string]::IsNullOrWhiteSpace($uiFreshnessText)) {
            $latestFreshnessText = $uiFreshnessText
            $freshnessSource = 'ui-trace-stale'
        }

        $freshnessTracePath = Join-Path $ResultsDir 'runtime-freshness-events.log'
        $line = "timestamp={0} frame={1} phase={2} requested_fault_profile={3} effective_fault_profile={4} latest_freshness={5} latest_freshness_source={6} trace_age_seconds={7} ui_freshness={8}" -f `
            (Get-Date).ToString('o'),
            $CaptureIndex,
            $Phase,
            $RequestedFaultProfile,
            (Get-CurrentYFinanceFaultProfile -ProfilePath $FaultProfilePath),
            $(if ([string]::IsNullOrWhiteSpace($latestFreshnessText)) { 'unavailable' } else { $latestFreshnessText }),
            $freshnessSource,
            $(if ($null -eq $traceFreshnessAgeSeconds) { 'unknown' } else { $traceFreshnessAgeSeconds }),
            $(if ([string]::IsNullOrWhiteSpace($uiFreshnessText)) { 'unavailable' } else { $uiFreshnessText })
        Add-Content -LiteralPath $freshnessTracePath -Value $line -Encoding UTF8
    }
    catch {
        Write-Warning ("Runtime freshness snapshot failed for frame {0}, phase {1}: {2}" -f $CaptureIndex, $Phase, $_.Exception.Message)
    }
}

function Test-IsDisplayedSampleFullyLive {
    param([Parameter(Mandatory = $true)][object[]]$DisplayedSample)

    if ($DisplayedSample.Count -eq 0) {
        return $false
    }

    return -not ($DisplayedSample | Where-Object { [string]$_.State -ne 'live' } | Select-Object -First 1)
}

function Get-PreferredDisplayedTapeSample {
    $tracePath = Get-HarnessTracePath -RelativePath 'Trace\trace.circular.log'
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
        if ($MaxBytes -le 0) {
            return ''
        }

        $idxPath = [System.IO.Path]::ChangeExtension($Path, '.idx')
        if (Test-Path $idxPath) {
            $positionText = Get-Content -LiteralPath $idxPath -Raw -ErrorAction Stop
            $writePosition = 0
            if ([int]::TryParse($positionText.Trim(), [ref]$writePosition)) {
                $fileStream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
                try {
                    $length = $fileStream.Length
                    if ($length -le 0) {
                        return ''
                    }

                    $bytesToRead = [int][Math]::Min([int64]$MaxBytes, $length)
                    $position = [int64][Math]::Max(0, [Math]::Min([int64]$writePosition, $length))
                    $buffer = New-Object byte[] $bytesToRead
                    $offset = 0
                    $start = $position - $bytesToRead
                    if ($start -lt 0) {
                        # Preserve chronological order for circular logs by reading
                        # the older EOF chunk first, then the newer chunk ending at
                        # the current write cursor.
                        $firstChunkLength = [int](-$start)
                        $firstChunkStart = $length - $firstChunkLength
                        $null = $fileStream.Seek($firstChunkStart, [System.IO.SeekOrigin]::Begin)
                        while ($offset -lt $firstChunkLength) {
                            $read = $fileStream.Read($buffer, $offset, $firstChunkLength - $offset)
                            if ($read -le 0) { break }
                            $offset += $read
                        }

                        $start = 0
                    }

                    if ($offset -lt $bytesToRead) {
                        $remaining = $bytesToRead - $offset
                        $null = $fileStream.Seek($start, [System.IO.SeekOrigin]::Begin)
                        while ($offset -lt $bytesToRead) {
                            $read = $fileStream.Read($buffer, $offset, $bytesToRead - $offset)
                            if ($read -le 0) { break }
                            $offset += $read
                        }
                    }

                    return ([System.Text.Encoding]::UTF8.GetString($buffer, 0, $offset)).Replace("`0", '')
                }
                finally {
                    $fileStream.Dispose()
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

function Find-ConfigWindow {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    Write-ConfigWindowTrace -Event 'FindConfigWindowStart' -Details ("process_id={0}; timeout_seconds={1}" -f $Process.Id, $TimeoutSeconds)
    do {
        $win32Matches = @(Find-Win32TopLevelWindowLike -ProcessId $Process.Id -TitleFragment 'PORTFOLIO VISUALIZER Config')
        if ($win32Matches.Count -gt 0) {
            try {
                $match = $win32Matches[0]
                Write-ConfigWindowTrace -Event 'FindConfigWindowWin32Match' -Details ("process_id={0}; title={1}" -f $match.ProcessId, $match.Title)
                $window = [System.Windows.Automation.AutomationElement]::FromHandle($match.Handle)
                if ($null -ne $window) {
                    return $window
                }
            }
            catch {}
        }

        foreach ($window in @(Get-TopLevelWindowsForProcess -Process $Process)) {
            try {
                $title = [string]$window.Current.Name
                $automationId = [string]$window.Current.AutomationId
                if ($automationId -eq 'ConfigMainWindow' -or
                    $title -like '*PORTFOLIO VISUALIZER Config*') {
                    Write-ConfigWindowTrace -Event 'FindConfigWindowMatch' -Details ("automation_id={0}; title={1}" -f $automationId, $title)
                    return $window
                }
            }
            catch {
                continue
            }
        }

        $ownedWindow = Find-ConfigWindowOwned -Process $Process
        if ($null -ne $ownedWindow) {
            try {
                Write-ConfigWindowTrace -Event 'FindConfigWindowOwnedMatch' -Details ("automation_id={0}; title={1}" -f [string]$ownedWindow.Current.AutomationId, [string]$ownedWindow.Current.Name)
            }
            catch {}
            return $ownedWindow
        }

        $namedTopLevel = Find-TopLevelWindowByNameLike -NameLike '*PORTFOLIO VISUALIZER Config*' -TimeoutSeconds 1
        if ($null -ne $namedTopLevel) {
            try {
                Write-ConfigWindowTrace -Event 'FindConfigWindowFallbackMatch' -Details ("automation_id={0}; title={1}" -f [string]$namedTopLevel.Current.AutomationId, [string]$namedTopLevel.Current.Name)
            }
            catch {}
            return $namedTopLevel
        }

        Start-Sleep -Milliseconds 120
    } while ((Get-Date) -lt $deadline)

    Write-ConfigWindowTrace -Event 'FindConfigWindowTimeout' -Details ("process_id={0}; windows={1}" -f $Process.Id, (Get-TopLevelWindowSnapshot -ProcessId $Process.Id))
    return $null
}

function Find-ConfigWindowOwned {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process
    )

    $win32Matches = @(Find-Win32TopLevelWindowLike -ProcessId $Process.Id -TitleFragment 'PORTFOLIO VISUALIZER Config')
    if ($win32Matches.Count -gt 0) {
        try {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($win32Matches[0].Handle)
            if ($null -ne $window) {
                return $window
            }
        }
        catch {}
    }

    foreach ($window in @(Get-ProcessOwnedWindows -Process $Process)) {
        try {
            $title = [string]$window.Current.Name
            $automationId = [string]$window.Current.AutomationId
            if ($automationId -eq 'ConfigMainWindow' -or
                $title -like '*PORTFOLIO VISUALIZER Config*') {
                return $window
            }
        }
        catch {
            continue
        }
    }

    foreach ($window in @(Get-TopLevelWindowsForProcess -Process $Process)) {
        try {
            $title = [string]$window.Current.Name
            $automationId = [string]$window.Current.AutomationId
            if ($automationId -eq 'ConfigMainWindow' -or
                $title -like '*PORTFOLIO VISUALIZER Config*') {
                return $window
            }
        }
        catch {
            continue
        }
    }

    return $null
}

function Get-ProcessOwnedWindows {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process
    )

    try {
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
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

            Write-ConfigWindowTrace -Event 'BlockingDialogDetected' -Details ("title={0}; is_modal={1}; message={2}" -f $title, $isModal, $message)
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

            Start-Sleep -Milliseconds 150
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

function Click-ConfigFooterButtonFallback {
    param(
        [Parameter(Mandatory = $true)]$Window,
        [ValidateSet('Apply', 'Cancel')]
        [string]$CompletionMode
    )

    try {
        $bounds = $Window.Current.BoundingRectangle
        if ($null -eq $bounds -or $bounds.Width -le 1 -or $bounds.Height -le 1) {
            return $false
        }

        $targetX = if ($CompletionMode -eq 'Cancel') {
            $bounds.Right - 170
        }
        else {
            $bounds.Right - 62
        }
        $targetY = $bounds.Bottom - 34

        Write-ConfigWindowTrace -Event 'FooterButtonClickFallbackAttempt' -Details ("mode={0}; x={1}; y={2}" -f $CompletionMode, [int][Math]::Round($targetX), [int][Math]::Round($targetY))
        [void][NativeMouseInput]::SetCursorPos([int][Math]::Round($targetX), [int][Math]::Round($targetY))
        Start-Sleep -Milliseconds 80
        [NativeMouseInput]::mouse_event([NativeMouseInput]::MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 40
        [NativeMouseInput]::mouse_event([NativeMouseInput]::MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
        Write-ConfigWindowTrace -Event 'FooterButtonClickFallback' -Details ("mode={0}; x={1}; y={2}" -f $CompletionMode, [int][Math]::Round($targetX), [int][Math]::Round($targetY))
        return $true
    }
    catch {
        Write-ConfigWindowTrace -Event 'FooterButtonClickFallbackFailed' -Details $_.Exception.Message
        return $false
    }
}

function Click-ConfigCloseButtonFallback {
    param(
        [Parameter(Mandatory = $true)]$Window
    )

    try {
        $closeButton = Find-DescendantByNameAndControlType -Root $Window -Name 'Close' -ControlType ([System.Windows.Automation.ControlType]::Button)
        if ($null -eq $closeButton) {
            Write-ConfigWindowTrace -Event 'ConfigCloseButtonMissing'
            return $false
        }

        $invoked = Invoke-AutomationElement -Element $closeButton
        if (-not $invoked) {
            $invoked = Click-AutomationElementCenter -Element $closeButton
        }

        Write-ConfigWindowTrace -Event 'ConfigCloseButtonFallback' -Details ("result={0}" -f $invoked)
        return $invoked
    }
    catch {
        Write-ConfigWindowTrace -Event 'ConfigCloseButtonFallbackFailed' -Details $_.Exception.Message
        return $false
    }
}

function Close-ConfigWindowPatternFallback {
    param(
        [Parameter(Mandatory = $true)]$Window
    )

    if ($null -eq $Window) {
        Write-ConfigWindowTrace -Event 'ConfigWindowPatternCloseFallback' -Details 'result=NoWindow'
        return $false
    }

    try {
        $windowPattern = $Window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
        $windowPattern.Close()
        Write-ConfigWindowTrace -Event 'ConfigWindowPatternCloseFallback' -Details 'result=True'
        return $true
    }
    catch {
        Write-ConfigWindowTrace -Event 'ConfigWindowPatternCloseFallbackFailed' -Details $_.Exception.Message
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
            if ($text -like '*Validation passed. Saving and closing now.*' -or
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

function Get-WindowButtonSnapshot {
    param(
        [Parameter(Mandatory = $true)]$Window
    )

    try {
        $buttons = $Window.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)))

        $snapshot = @()
        for ($index = 0; $index -lt $buttons.Count; $index++) {
            try {
                $button = $buttons.Item($index)
                $snapshot += ('{0}|{1}|enabled={2}|offscreen={3}' -f
                    [string]$button.Current.AutomationId,
                    [string]$button.Current.Name,
                    $button.Current.IsEnabled,
                    $button.Current.IsOffscreen)
            }
            catch {
                continue
            }
        }

        return [string]::Join('; ', $snapshot)
    }
    catch {
        return ''
    }
}

function Wait-ConfigPrimaryButtonReady {
    param(
        [Parameter(Mandatory = $true)]$Window,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $pollCount = 0
    do {
        $pollCount++
        $primaryButton = Find-DescendantByAutomationId -Root $Window -AutomationId 'ConfigPrimaryButton'
        if ($null -ne $primaryButton) {
            try {
                if ($primaryButton.Current.IsEnabled -and -not $primaryButton.Current.IsOffscreen) {
                    return $primaryButton
                }
            }
            catch {
            }
        }

        if ($pollCount -eq 1 -or ($pollCount % 5) -eq 0) {
            Write-ConfigWindowTrace -Event 'PrimaryButtonNotReady' -Details ("poll={0}; timeout_seconds={1}" -f $pollCount, $TimeoutSeconds)
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    return $null
}

function Test-IsExpectedValidationUnavailableStatus {
    param([AllowNull()][string]$StatusText)

    if ([string]::IsNullOrWhiteSpace($StatusText)) {
        return $false
    }

    return $StatusText -match '(?i)(validation.*unavailable|throttl.*validation)'
}

function Test-ConfigExpectsValidationUnavailable {
    param([AllowNull()][string]$Profile)

    # Runtime-only profiles intentionally stay out of this list because their
    # fault is activated after the settings workflow has completed.
    return $Profile -in @('offline-at-start', 'offline-during-config-validation', 'upstream-throttled', 'timeout')
}

function Close-ConfigForExpectedValidationUnavailable {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)]$Window,
        [Parameter(Mandatory = $true)][string]$StatusText
    )

    # Validation-unavailable failures show a modal warning dialog before the
    # main window can close. This is intentionally idempotent with the attempt-
    # level child-window cleanup because the warning appears after Validate is
    # invoked, not before the attempt starts.
    Close-ConfigChildWindows -MainProcessId $Process.Id
    [void](Wait-UIAutomationCondition -TimeoutSeconds 3 -PollMilliseconds 100 -TraceEvent 'ExpectedValidationUnavailableChildDialogCloseWait' -Condition {
        $dialog = Get-ConfigBlockingDialog -Process $Process
        return ($null -eq $dialog)
    })
    $currentWindow = Find-ConfigWindowOwned -Process $Process
    if ($null -eq $currentWindow) {
        Write-ConfigWindowTrace -Event 'ExpectedValidationUnavailableClosed' -Details 'method=Close-ConfigChildWindows'
        return $true
    }

    $buttonSnapshot = Get-WindowButtonSnapshot -Window $currentWindow
    Write-ConfigWindowTrace -Event 'ExpectedValidationUnavailableObserved' -Details ("status={0}; buttons={1}" -f $StatusText, $buttonSnapshot)

    # Prefer UIA close first, then WindowPattern.Close for stale/lying button
    # elements, then the footer coordinate fallback as a final escape hatch.
    $methods = @(
        @{ Name = 'Click-ConfigCloseButtonFallback'; Invoke = { param($targetWindow) Click-ConfigCloseButtonFallback -Window $targetWindow } },
        @{ Name = 'Close-ConfigWindowPatternFallback'; Invoke = { param($targetWindow) Close-ConfigWindowPatternFallback -Window $targetWindow } },
        @{ Name = 'Click-ConfigFooterButtonFallback'; Invoke = { param($targetWindow) Click-ConfigFooterButtonFallback -Window $targetWindow -CompletionMode 'Cancel' } }
    )

    foreach ($method in $methods) {
        $currentWindow = Find-ConfigWindowOwned -Process $Process
        if ($null -eq $currentWindow) {
            Write-ConfigWindowTrace -Event 'ExpectedValidationUnavailableClosed'
            return $true
        }

        $invoked = $false
        try {
            Write-ConfigWindowTrace -Event 'ExpectedValidationUnavailableCloseAttempt' -Details ("method={0}" -f $method['Name'])
            $invokeCloseMethod = $method['Invoke']
            $invoked = & $invokeCloseMethod $currentWindow
        }
        catch {
            Write-ConfigWindowTrace -Event 'ExpectedValidationUnavailableCloseException' -Details ("method={0}; message={1}" -f $method['Name'], $_.Exception.Message)
            continue
        }

        if (-not $invoked) {
            Write-ConfigWindowTrace -Event 'ExpectedValidationUnavailableCloseMethodFailed' -Details ("method={0}; invoked=False" -f $method['Name'])
            continue
        }

        $closeObserved = Wait-UIAutomationCondition -TimeoutSeconds 10 -PollMilliseconds 200 -TraceEvent 'ExpectedValidationUnavailableCloseWait' -Condition {
            $Process.Refresh()
            $remaining = Find-ConfigWindowOwned -Process $Process
            return ($null -eq $remaining)
        }

        if ($closeObserved -eq $true) {
            Write-ConfigWindowTrace -Event 'ExpectedValidationUnavailableClosed' -Details ("method={0}" -f $method['Name'])
            return $true
        }

        Write-ConfigWindowTrace -Event 'ExpectedValidationUnavailableCloseMethodDidNotDismiss' -Details ("method={0}" -f $method['Name'])
    }

    Write-ConfigWindowTrace -Event 'ExpectedValidationUnavailableCloseFailed'
    return $false
}

function Validate-AndCloseConfigWindow {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        $Window,
        [ValidateSet('Apply', 'Cancel')]
        [string]$CompletionMode = 'Apply',
        [switch]$ExpectedValidationUnavailable
    )

    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        Write-ConfigWindowTrace -Event 'ValidateCloseAttempt' -Details ("attempt={0}" -f ($attempt + 1))
        Close-ConfigChildWindows -MainProcessId $Process.Id
        $Window = Find-ConfigWindow -Process $Process -TimeoutSeconds 2

        if ($null -eq $Window) {
            Write-ConfigWindowTrace -Event 'ValidateCloseNoWindow'
            return $true
        }

        $primaryButton = Wait-ConfigPrimaryButtonReady -Window $Window -TimeoutSeconds 30
        if ($null -eq $primaryButton) {
            Write-ConfigWindowTrace -Event 'PrimaryButtonMissingOrDisabled' -Details (Get-WindowButtonSnapshot -Window $Window)
            return $false
        }

        try {
            $invoked = Invoke-AutomationElement -Element $primaryButton
            if (-not $invoked) {
                $invoked = Click-AutomationElementCenter -Element $primaryButton
            }
            Write-ConfigWindowTrace -Event 'PrimaryButtonInvoked' -Details ("result={0}" -f $invoked)
            if (-not $invoked) {
                return $false
            }
        }
        catch {
            Write-ConfigWindowTrace -Event 'PrimaryButtonInvokeFailed' -Details $_.Exception.Message
            return $false
        }

        $deadline = (Get-Date).AddSeconds(45)
        $okReady = $false
        do {
            Start-Sleep -Milliseconds 250
            $Process.Refresh()
            $blockingDialog = Get-ConfigBlockingDialog -Process $Process
            if ($null -ne $blockingDialog) {
                $script:summary.Notes += "Config close dialog: $($blockingDialog.Title) - $($blockingDialog.Message)"
                return $false
            }

            $statusText = Get-ConfigStatusText -Window $Window
            if (-not [string]::IsNullOrWhiteSpace($statusText)) {
                Write-ConfigWindowTrace -Event 'ValidateStatus' -Details $statusText
            }

            if (-not (Test-AutomationElementAlive -Element $Window)) {
                $Window = Find-ConfigWindowOwned -Process $Process
                if ($null -eq $Window) {
                    $Window = Find-ConfigWindow -Process $Process -TimeoutSeconds 1
                }
            }

            if ($ExpectedValidationUnavailable -and (Test-IsExpectedValidationUnavailableStatus -StatusText $statusText)) {
                if (Close-ConfigForExpectedValidationUnavailable -Process $Process -Window $Window -StatusText $statusText) {
                    return $true
                }

                Write-ConfigWindowTrace -Event 'ExpectedValidationUnavailableRetryScheduled' -Details ("attempt={0}" -f ($attempt + 1))
                break
            }

            if ($null -ne $Window) {
                $validatedStatusReady = -not [string]::IsNullOrWhiteSpace($statusText) -and
                    $statusText -like '*Validation passed. Click OK to save/apply, or Cancel to discard.*'
                $okButton = Find-DescendantByAutomationId -Root $Window -AutomationId 'ConfigOkButton'
                if ($null -eq $okButton) {
                    $okButton = Find-DescendantByNameAndControlType -Root $Window -Name 'OK' -ControlType ([System.Windows.Automation.ControlType]::Button)
                }
                $cancelButton = Find-DescendantByAutomationId -Root $Window -AutomationId 'ConfigCancelButton'
                if ($null -eq $cancelButton) {
                    $cancelButton = Find-DescendantByNameAndControlType -Root $Window -Name 'Cancel' -ControlType ([System.Windows.Automation.ControlType]::Button)
                }
                $primaryLabel = if ($null -ne $okButton) { [string]$okButton.Current.Name } else { '' }
                $validatedButtonsReady = if ($CompletionMode -eq 'Apply') {
                    $null -ne $okButton
                }
                else {
                    $null -ne $okButton -and $null -ne $cancelButton
                }
                if (($validatedStatusReady -and $validatedButtonsReady) -or $validatedButtonsReady) {
                    $buttonSnapshot = Get-WindowButtonSnapshot -Window $Window
                    Write-ConfigWindowTrace -Event 'ValidateOkReady' -Details ("status={0}; primary_label={1}; cancel_present={2}; mode={3}; buttons={4}" -f $statusText, $primaryLabel, ($null -ne $cancelButton), $CompletionMode, $buttonSnapshot)
                    if ($null -ne $okButton) {
                        $primaryButton = $okButton
                    }
                    $okReady = $true
                    break
                }
            }
        } while ($null -ne $Window -and (Get-Date) -lt $deadline)

        if ($null -eq $Window) {
            Write-ConfigWindowTrace -Event 'ValidateClosedUnexpectedly'
            return $false
        }

        if (-not $okReady) {
            Write-ConfigWindowTrace -Event 'ValidateOkNotReached'
            continue
        }

        try {
            $invoked = $false
            $targetButton = $primaryButton
            $invokeEvent = 'OkButtonInvoked'
            $invokeFailedEvent = 'OkButtonInvokeFailed'
            if ($CompletionMode -eq 'Cancel') {
                $cancelButton = Find-DescendantByAutomationId -Root $Window -AutomationId 'ConfigCancelButton'
                if ($null -eq $cancelButton) {
                    $cancelButton = Find-DescendantByNameAndControlType -Root $Window -Name 'Cancel' -ControlType ([System.Windows.Automation.ControlType]::Button)
                }
                if ($null -eq $cancelButton) {
                    Write-ConfigWindowTrace -Event 'CancelButtonMissing'
                    $targetButton = $null
                }
                else {
                    $targetButton = $cancelButton
                }
                $invokeEvent = 'CancelButtonInvoked'
                $invokeFailedEvent = 'CancelButtonInvokeFailed'
            }

            if ($null -ne $targetButton) {
                try {
                    $targetButton.SetFocus()
                }
                catch {
                }
                Write-ConfigWindowTrace -Event 'ValidatedButtonTargetReady' -Details ("mode={0}; automation_id={1}; name={2}" -f $CompletionMode, [string]$targetButton.Current.AutomationId, [string]$targetButton.Current.Name)
            }

            if ($null -ne $targetButton) {
                $invoked = Invoke-AutomationElement -Element $targetButton
                if (-not $invoked) {
                    $invoked = Click-AutomationElementCenter -Element $targetButton
                }
            }

            if (-not $invoked -and $Window) {
                try {
                    $Window.SetFocus()
                }
                catch {
                }

                $keys = if ($CompletionMode -eq 'Cancel') { @('{ESC}') } else { @('{ENTER}') }
                Send-KeySequence -Keys $keys -DelayMilliseconds 80
                Write-ConfigWindowTrace -Event 'ValidatedKeyboardCloseAttempt' -Details ("mode={0}; key={1}" -f $CompletionMode, $keys[0])
                $keyboardClosed = Wait-UIAutomationCondition -TimeoutSeconds 3 -PollMilliseconds 100 -TraceEvent 'ValidatedKeyboardCloseWait' -Condition {
                    $Process.Refresh()
                    if ($Process.HasExited) { return $true }
                    $remaining = Find-ConfigWindow -Process $Process -TimeoutSeconds 1
                    return ($null -eq $remaining)
                }
                if ($keyboardClosed -eq $true -or $Process.HasExited) {
                    Write-ConfigWindowTrace -Event 'ValidatedKeyboardCloseSucceeded' -Details ("mode={0}" -f $CompletionMode)
                    return $true
                }
                $Window = Find-ConfigWindow -Process $Process -TimeoutSeconds 1
            }

            if (-not $invoked -and $CompletionMode -eq 'Cancel') {
                $invoked = Click-ConfigCloseButtonFallback -Window $Window
            }
            if (-not $invoked) {
                $invoked = Click-ConfigFooterButtonFallback -Window $Window -CompletionMode $CompletionMode
            }
            Write-ConfigWindowTrace -Event $invokeEvent -Details ("result={0}; mode={1}" -f $invoked, $CompletionMode)
            if (-not $invoked) {
                return $false
            }
        }
        catch {
            Write-ConfigWindowTrace -Event $invokeFailedEvent -Details $_.Exception.Message
            return $false
        }

        $closeObserved = Wait-UIAutomationCondition -TimeoutSeconds 15 -PollMilliseconds 200 -TraceEvent 'ValidateCloseWait' -Condition {
            $Process.Refresh()
            $remaining = Find-ConfigWindowOwned -Process $Process
            return ($null -eq $remaining)
        }
        if ($closeObserved -eq $true) {
            $Window = $null
        }
        else {
            $Window = Find-ConfigWindowOwned -Process $Process
        }

        if ($null -eq $Window) {
            Write-ConfigWindowTrace -Event 'ValidateCloseSucceeded'
            return $true
        }
    }

    Write-ConfigWindowTrace -Event 'ValidateCloseFailed'
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
        ComparisonSchemaVersion = $ReferenceComparisonSchemaVersion
        CapturedAt = $Payload.CapturedAt
        CaptureIndex = $Payload.CaptureIndex
        Source = 'DisplayedVsReferenceFeed'
        ReferenceSource = $Payload.Source
        Warning = $Payload.Warning
        YFinanceEvidenceStatus = $Payload.YFinanceEvidenceStatus
        Comparisons = @()
    }

    $resultMap = @{}
    foreach ($result in @($Payload.Results)) {
        if ($null -ne $result.Symbol) {
            $resultMap[[string]$result.Symbol] = $result
        }
    }

    foreach ($displayed in @($Payload.DisplayedSample)) {
        $symbol = [string]$displayed.Symbol
        $state = [string]$displayed.State
        if (-not $resultMap.ContainsKey($symbol)) {
            $comparison.Comparisons += [ordered]@{
                Symbol = $symbol
                State = $state
                Status = if ($Payload.YFinanceEvidenceStatus -eq 'present') { 'reference-missing' } else { 'yfinance-evidence-missing' }
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

    # Canonical rule: harness code must not call Yahoo quote APIs directly.
    # Reference values come from YFinance.NET circular trace quote response
    # events generated by the owned server.
    $traceText = Read-YFinanceTraceText
    $results = @(Parse-YFinanceQuoteEvidence -TraceText $traceText -Symbols $Symbols)
    return [pscustomobject]@{
        Source = 'YFinanceTrace'
        Results = @($results)
        Status = if ($results.Count -gt 0) { 'ok' } else { 'yfinance-evidence-missing' }
        Symbols = @($Symbols)
        Error = $null
        Warning = $ExternalReferenceDisabledWarning
    }
}

$summary.ExportMode = 'LocalWorkspace'
$summary.ResultName = $resultName
$summary.ResultPath = $results
$summary.FaultProfile = $FaultProfile
$summary.FaultProfilePath = $faultProfilePath
$summary.FaultTimelinePath = $faultTimelinePath
Write-SummaryFiles
Start-Transcript -Path $logPath -Force | Out-Null

try {
    $initialFaultProfile = if ($FaultProfile -in @('offline-during-config-validation', 'offline-during-runtime', 'offline-then-recover-runtime')) { 'none' } else { $FaultProfile }
    if ($initialFaultProfile -ne 'none') {
        Set-YFinanceFaultProfile -Profile $initialFaultProfile
    }
    else {
        Clear-YFinanceFaultProfile
    }

    Reset-PortfolioTraceRoot
    Apply-HarnessSettingsOverrides
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
        $configPhaseStartedAt = [datetime]::UtcNow
        $configInteractionStartedAt = $null
        $desktop = Start-Process -FilePath $desktopExe -PassThru
        $desktopWindow = Wait-ProcessWindowElementWithFallback `
            -Process $desktop `
            -InitialTraceEvent 'DesktopMainWindowWait' `
            -FallbackTraceEvent 'DesktopMainWindowFallbackWait' `
            -CompleteTraceEvent 'DesktopWindowDiscoveryComplete' `
            -LookupErrorEvent 'DesktopWindowElementLookupError' `
            -ExitMessage 'Desktop process exited before its window was discoverable.' `
            -NotFoundMessage 'Could not locate desktop shell window via UI Automation.'

        [void](Focus-ProcessWindow -Process $desktop)
        $configOpened = $false
        Write-ConfigWindowTrace -Event 'ConfigPhaseStart' -Details ("desktop_process_id={0}" -f $desktop.Id)
        $optionsMenuItem = Find-DescendantByAutomationId -Root $desktopWindow -AutomationId 'OptionsMenuRoot'
        if ($null -ne $optionsMenuItem) {
            [void](Expand-AutomationElement -Element $optionsMenuItem)
            Write-ConfigWindowTrace -Event 'OptionsMenuExpanded' -Details 'path=automation'
            $settingsMenuItem = Wait-UIAutomationCondition -TimeoutSeconds 3 -PollMilliseconds 40 -TraceEvent 'SettingsMenuItemWait' -Condition {
                return (Find-DescendantByAutomationId -Root $desktopWindow -AutomationId 'OptionsSettingsMenuItem')
            }
        }
        else {
            $settingsMenuItem = Find-DescendantByAutomationId -Root $desktopWindow -AutomationId 'OptionsSettingsMenuItem'
        }
        if ($null -ne $settingsMenuItem) {
            $configOpened = Invoke-AutomationElement -Element $settingsMenuItem
            Write-ConfigWindowTrace -Event 'SettingsMenuInvoked' -Details ("path=automation; result={0}" -f $configOpened)
            if (-not $configOpened) {
                try { $configOpened = Click-AutomationElementCenter -Element $settingsMenuItem } catch {}
                Write-ConfigWindowTrace -Event 'SettingsMenuClickFallback' -Details ("result={0}" -f $configOpened)
            }
        }

        if (-not $configOpened) {
            try {
                [void](Focus-ProcessWindow -Process $desktop)
                [System.Windows.Forms.SendKeys]::SendWait('%o')
                Write-ConfigWindowTrace -Event 'OptionsMenuExpanded' -Details 'path=keyboard-fallback'
                [void](Wait-UIAutomationCondition -TimeoutSeconds 2 -PollMilliseconds 20 -TraceEvent 'KeyboardSettingsMenuWait' -Condition {
                    return (Find-DescendantByAutomationId -Root $desktopWindow -AutomationId 'OptionsSettingsMenuItem')
                })
                [System.Windows.Forms.SendKeys]::SendWait('s')
                Write-ConfigWindowTrace -Event 'SettingsMenuInvoked' -Details 'path=keyboard-fallback'
                [void](Wait-UIAutomationCondition -TimeoutSeconds 4 -PollMilliseconds 50 -TraceEvent 'SettingsAcceleratorConfigWindowWait' -Condition {
                    $matches = @(Find-Win32TopLevelWindowLike -ProcessId $desktop.Id -TitleFragment 'PORTFOLIO VISUALIZER Config')
                    return ($matches.Count -gt 0)
                })
            }
            catch {}
        }

        Test-ConfigPhaseBudget -StartedAt $configPhaseStartedAt -Stage 'post-open-reacquire'
        $window = Find-ConfigWindow -Process $desktop -TimeoutSeconds 20
        if ($null -eq $window) { throw 'Could not locate config window via UI Automation.' }
        Write-ConfigWindowTrace -Event 'ConfigWindowReacquired' -Details ("title={0}; automation_id={1}" -f [string]$window.Current.Name, [string]$window.Current.AutomationId)
        $configInteractionStartedAt = [datetime]::UtcNow
        if ([string]$window.Current.Name -like '*BETA-7*' -or
            [string]$window.Current.HelpText -like '*0.9.0-beta7*') {
            $summary.ConfigVersionCheck = "Passed"
        }
        else {
            $summary.ConfigVersionCheck = "Failed"
            $summary.Notes += "Config window title missing expected BETA-7 marker: '$([string]$window.Current.Name)'"
        }

        $tabNames = @('General', 'Advanced')

        $shotIndex = 1
        foreach ($rawTabName in $tabNames) {
            Test-ConfigPhaseBudget -StartedAt $configInteractionStartedAt -Stage ("tab-{0}" -f $rawTabName)
            if (-not (Test-AutomationElementAlive -Element $window)) {
                $window = Find-ConfigWindow -Process $desktop -TimeoutSeconds 2
            }

            if ($null -eq $window -or -not (Test-AutomationElementAlive -Element $window)) {
                throw 'Config window disappeared during tab traversal.'
            }

            $tab = Find-TabItemByName -Window $window -TabName $rawTabName
            if ($null -eq $tab) {
                Write-ConfigWindowTrace -Event 'TabReacquireFailed' -Details ("tab={0}" -f $rawTabName)
                $summary.Notes += "Could not reacquire tab '$rawTabName'; skipping."
                continue
            }

            if ($shotIndex -gt 1) {
                [void](Select-TabItem -Tab $tab)
                [void](Wait-UIAutomationCondition -TimeoutSeconds 3 -PollMilliseconds 50 -TraceEvent 'TabSelectionWait' -Condition {
                    try {
                        $selectedPattern = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                        return $selectedPattern.Current.IsSelected
                    }
                    catch {
                        return $false
                    }
                })
            }
            Write-ConfigWindowTrace -Event 'TabSelected' -Details ("tab={0}" -f $rawTabName)
            $tabName = ($rawTabName -replace '[^A-Za-z0-9_-]','_')
            Capture-Screen -Path (Join-Path $results ("config-tab-{0:D3}-{1}.png" -f $shotIndex, $tabName))
            $summary.ConfigShots++
            $shotIndex++

            $scrolled = Perform-VisibleConfigActivity -Window $window -TabName $rawTabName
            Write-ConfigWindowTrace -Event 'TabActivityComplete' -Details ("tab={0}; scrolled={1}" -f $rawTabName, $scrolled)
            if ($scrolled) {
                Start-Sleep -Milliseconds 80
                Capture-Screen -Path (Join-Path $results ("config-tab-{0:D3}-{1}-scrolled.png" -f $shotIndex, $tabName))
                $summary.ConfigShots++
                $shotIndex++
            }
        }

        Test-ConfigPhaseBudget -StartedAt $configInteractionStartedAt -Stage 'validate-close'
        try {
            if ($FaultProfile -eq 'offline-during-config-validation') {
                Set-YFinanceFaultProfile -Profile 'offline'
            }
            $expectedValidationUnavailable = Test-ConfigExpectsValidationUnavailable -Profile $FaultProfile
            $configClosedNaturally = Validate-AndCloseConfigWindow -Process $desktop -Window $window -CompletionMode $ValidationCompletionMode -ExpectedValidationUnavailable:$expectedValidationUnavailable
            if ($configClosedNaturally) {
                $closeVerified = Wait-UIAutomationCondition -TimeoutSeconds 3 -PollMilliseconds 100 -TraceEvent 'ConfigClosedVerificationWait' -Condition {
                    $remaining = Find-ConfigWindowOwned -Process $desktop
                    return ($null -eq $remaining)
                }
                if ($closeVerified -eq $true) {
                    $window = $null
                }
                else {
                    $window = Find-ConfigWindowOwned -Process $desktop
                }
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
        }
        finally {
            if ($FaultProfile -eq 'offline-during-config-validation') {
                Clear-YFinanceFaultProfile
            }
        }

        $summary.ConfigPhaseStatus = "Completed"
        Write-SummaryFiles

        if ($isLongRunSoak) {
            try {
                if ($null -ne $desktop -and -not $desktop.HasExited) {
                    $desktop.CloseMainWindow() | Out-Null
                    [void](Wait-UIAutomationCondition -TimeoutSeconds 5 -PollMilliseconds 100 -TraceEvent 'DesktopCloseBeforeSoakWait' -Condition {
                        $desktop.Refresh()
                        return $desktop.HasExited
                    })
                    if (-not $desktop.HasExited) {
                        Stop-Process -Id $desktop.Id -Force -ErrorAction SilentlyContinue
                    }
                }
            }
            catch {}

            $previousDisableInputExit = $env:PORTFOLIOSAVER_DISABLE_INPUT_EXIT
            $env:PORTFOLIOSAVER_DISABLE_INPUT_EXIT = '1'
            if ($FaultProfile -in @('offline-during-runtime', 'offline-then-recover-runtime')) {
                Set-YFinanceFaultProfile -Profile 'offline'
            }
            $desktop = Start-Process -FilePath $screensaverExe -ArgumentList '/s' -PassThru
            [void](Wait-UIAutomationCondition -TimeoutSeconds 10 -PollMilliseconds 100 -TraceEvent 'ScreensaverWindowWait' -Condition {
                $desktop.Refresh()
                return ($desktop.MainWindowHandle -ne [IntPtr]::Zero)
            })
            $summary.ScreensaverPhaseStatus = "Running"
            $summary.Notes += "Fullscreen soak host launched from PortfolioSaver.Screensaver with input-exit disabled."
        }
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
            [void](Wait-UIAutomationCondition -TimeoutSeconds 2 -PollMilliseconds 90 -TraceEvent 'ForcedConfigCloseWait' -Condition {
                if ($null -eq $desktop) { return $true }
                return ($null -eq (Find-ConfigWindowOwned -Process $desktop))
            })
        }
    }

    try {
        if ($null -eq $desktop -or $desktop.HasExited) {
            throw 'Desktop process was not running after config phase.'
        }

        $desktopWindow = Wait-ProcessWindowElementWithFallback `
            -Process $desktop `
            -InitialTraceEvent 'PostConfigDesktopWindowWait' `
            -FallbackTraceEvent 'PostConfigDesktopWindowFallbackWait' `
            -CompleteTraceEvent 'PostConfigDesktopWindowDiscoveryComplete' `
            -LookupErrorEvent 'PostConfigDesktopWindowElementLookupError' `
            -ExitMessage 'Desktop process exited before its post-config window was discoverable.' `
            -NotFoundMessage 'Could not locate desktop shell window via UI Automation.'
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
            if ($isLongRunSoak) {
                $summary.ScreensaverVersionCheck = "Passed"
            }
            else {
                $summary.DesktopVersionCheck = "Passed"
            }
            $summary.Notes += ("Visual host version element observed: name='{0}' automation_id='{1}' help='{2}'" -f
                $versionMatch.Name,
                $versionMatch.AutomationId,
                $versionMatch.HelpText)
        }
        else {
            if ($isLongRunSoak) {
                $summary.ScreensaverVersionCheck = "SoftFailed"
                $summary.Notes += "Screensaver version element containing the expected beta marker was not detected during long-run soak; continuing."
            }
            else {
                $summary.DesktopVersionCheck = "Failed"
                $summary.Notes += "Desktop version element containing the expected beta marker was not detected."
            }
        }

        $summary.DesktopPhaseStatus = "Running"
        if ($isLongRunSoak) {
            Start-Sleep -Seconds 1
            [void](Focus-ProcessWindow -Process $desktop)
            $fullScreenDeadline = (Get-Date).AddSeconds(12)
            do {
                Start-Sleep -Milliseconds 350
                $enteredFullScreen = Test-IsTrueFullscreen -Process $desktop
            } while (-not $enteredFullScreen -and (Get-Date) -lt $fullScreenDeadline)
            $desktopFull = Join-Path $results 'desktop-fullscreen-entry.png'
            Capture-Screen -Path $desktopFull
            $summary.DesktopShots++
            $summary.ScreensaverShots++
            Write-RuntimeFreshnessSnapshot -CaptureIndex 0 -Phase 'fullscreen-entry' -ResultsDir $results -RequestedFaultProfile $FaultProfile -FaultProfilePath $faultProfilePath -DesktopProcess $desktop -IncludeVisibleFreshness
            if (-not $enteredFullScreen) {
                throw "Visual host did not enter true fullscreen after long-run soak relaunch."
            }
            $summary.FullScreenToggleStatus = "Completed"
            Write-SummaryFiles
        }
        else {
            Start-Sleep -Seconds 1
            [void](Focus-ProcessWindow -Process $desktop)
            if ($FaultProfile -in @('offline-during-runtime', 'offline-then-recover-runtime')) {
                Set-YFinanceFaultProfile -Profile 'offline'
            }
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
            Write-RuntimeFreshnessSnapshot -CaptureIndex 0 -Phase 'fullscreen-entry' -ResultsDir $results -RequestedFaultProfile $FaultProfile -FaultProfilePath $faultProfilePath -DesktopProcess $desktop -IncludeVisibleFreshness
            if (-not $enteredFullScreen) {
                throw "Desktop shell did not enter true fullscreen; taskbar/work-area chrome appears to remain visible."
            }
            Write-SummaryFiles

            [void](Focus-ProcessWindow -Process $desktop)
            $stillFullScreen = $true
            foreach ($exitAttempt in @(
                @{ Name = 'Escape'; Key = '{ESC}'; UseMenu = $false },
                @{ Name = 'F11'; Key = '{F11}'; UseMenu = $false },
                @{ Name = 'MenuToggle'; Key = $null; UseMenu = $true }
            )) {
                if (-not $stillFullScreen) { break }

                if ($exitAttempt.UseMenu) {
                    $desktopWindow = Find-DescendantByAutomationId -Root ([System.Windows.Automation.AutomationElement]::RootElement) -AutomationId 'DesktopMainWindow'
                    if ($null -ne $desktopWindow) {
                        $toggleMenuItem = Find-DescendantByAutomationId -Root $desktopWindow -AutomationId 'ViewFullScreenMenuItem'
                        if ($null -ne $toggleMenuItem) {
                            [void](Invoke-AutomationElement -Element $toggleMenuItem)
                        }
                    }
                }
                else {
                    try { [System.Windows.Forms.SendKeys]::SendWait([string]$exitAttempt.Key) } catch {}
                }

                $windowedDeadline = (Get-Date).AddSeconds(4)
                do {
                    Start-Sleep -Milliseconds 350
                    $stillFullScreen = Test-IsTrueFullscreen -Process $desktop
                } while ($stillFullScreen -and (Get-Date) -lt $windowedDeadline)
            }
            $desktopWindowed = Join-Path $results 'desktop-windowed-after-esc.png'
            Capture-Screen -Path $desktopWindowed
            $summary.DesktopShots++
            if ($stillFullScreen) {
                throw "Desktop shell remained in fullscreen after ESC."
            }
            $summary.FullScreenToggleStatus = "Completed"
            Write-SummaryFiles
        }

        if ($FaultProfile -eq 'offline-then-recover-runtime' -and $targetFrames -lt 2) {
            throw "Recovery fault profile requires at least two capture frames."
        }
        # The VM can spend variable time in screenshot capture. Keep the soak
        # duration wall-clock bounded and recover near the time midpoint.
        $captureLoopStartedAt = Get-Date
        $captureDeadline = $captureLoopStartedAt.AddMinutes($ScreensaverDurationMinutes)
        $recoveryAt = $captureLoopStartedAt.AddSeconds(($ScreensaverDurationMinutes * 60.0) / 2.0)
        $recoveryApplied = $false
        $i = 1
        $lastCaptureIndex = 0
        do {
            $frameStartedAt = Get-Date
            if ($desktop.HasExited) {
                throw "Desktop process exited early at frame $i (exit code: $($desktop.ExitCode))."
            }

            if ($FaultProfile -eq 'offline-then-recover-runtime' -and -not $recoveryApplied -and (Get-Date) -ge $recoveryAt) {
                Clear-YFinanceFaultProfile
                $recoveryApplied = $true
                Start-Sleep -Seconds 6
                $postRecoveryPath = Join-Path $results ("desktop-after-recovery-clear-{0:D3}.png" -f $i)
                Capture-Screen -Path $postRecoveryPath
                if ($isLongRunSoak) {
                    $summary.ScreensaverShots++
                }
                $summary.DesktopShots++
                Write-RuntimeFreshnessSnapshot -CaptureIndex $i -Phase 'after-recovery-clear' -ResultsDir $results -RequestedFaultProfile $FaultProfile -FaultProfilePath $faultProfilePath -DesktopProcess $desktop -IncludeVisibleFreshness
                if ([string]::IsNullOrWhiteSpace($referenceSpotCheckPath)) {
                    Write-Warning "Post-recovery reference spot-check skipped because referenceSpotCheckPath was empty."
                }
                else {
                    Write-ReferenceSpotCheck -OutputPath $referenceSpotCheckPath -CaptureIndex $i
                }
            }

            $path = Join-Path $results ("desktop-{0:D3}.png" -f $i)
            Capture-Screen -Path $path
            $includeVisibleFreshnessForCapture = $FaultProfile -in @('offline-at-start', 'offline-during-runtime') -or ($FaultProfile -eq 'offline-then-recover-runtime' -and $recoveryApplied)
            Write-RuntimeFreshnessSnapshot -CaptureIndex $i -Phase 'capture' -ResultsDir $results -RequestedFaultProfile $FaultProfile -FaultProfilePath $faultProfilePath -DesktopProcess $desktop -IncludeVisibleFreshness:$includeVisibleFreshnessForCapture
            if ($isLongRunSoak) {
                $summary.ScreensaverShots++
            }
            $summary.DesktopShots++
            $lastCaptureIndex = $i
            Write-SummaryFiles
            $nextCaptureAt = $frameStartedAt.AddSeconds($effectiveCaptureIntervalSeconds)
            $sleepSeconds = [int][Math]::Round(($nextCaptureAt - (Get-Date)).TotalSeconds)
            if ($sleepSeconds -gt 0 -and (Get-Date).AddSeconds($sleepSeconds) -lt $captureDeadline) {
                Start-Sleep -Seconds $sleepSeconds
            }
            elseif ((Get-Date) -lt $captureDeadline) {
                Start-Sleep -Milliseconds 100
            }
            $i++
        } while ((Get-Date) -lt $captureDeadline)

        if ($FaultProfile -eq 'offline-then-recover-runtime' -and -not $recoveryApplied) {
            Clear-YFinanceFaultProfile
            Start-Sleep -Seconds 6
            $postRecoveryPath = Join-Path $results ("desktop-after-recovery-clear-{0:D3}.png" -f $lastCaptureIndex)
            Capture-Screen -Path $postRecoveryPath
            if ($isLongRunSoak) {
                $summary.ScreensaverShots++
            }
            $summary.DesktopShots++
            Write-RuntimeFreshnessSnapshot -CaptureIndex $lastCaptureIndex -Phase 'after-recovery-clear' -ResultsDir $results -RequestedFaultProfile $FaultProfile -FaultProfilePath $faultProfilePath -DesktopProcess $desktop -IncludeVisibleFreshness
            if ([string]::IsNullOrWhiteSpace($referenceSpotCheckPath)) {
                Write-Warning "Post-recovery reference spot-check skipped because referenceSpotCheckPath was empty."
            }
            else {
                Write-ReferenceSpotCheck -OutputPath $referenceSpotCheckPath -CaptureIndex $lastCaptureIndex
            }
        }
        if ($lastCaptureIndex -lt [Math]::Floor($targetFrames * 0.8)) {
            $summary.Notes += "Desktop capture count $lastCaptureIndex was below 80 percent of estimated target $targetFrames; capture loop remained wall-clock bounded."
        }

        $summary.DesktopPhaseStatus = "Completed"
        if ($isLongRunSoak) {
            $summary.ScreensaverPhaseStatus = "Completed"
        }
        Write-SummaryFiles
    }
    catch {
        $summary.DesktopPhaseStatus = "Failed"
        if ($isLongRunSoak -and $summary.ScreensaverPhaseStatus -eq "Running") {
            $summary.ScreensaverPhaseStatus = "Failed"
        }
        $summary.Notes += "Desktop phase error: $($_.Exception.Message)"
        Write-SummaryFiles
    }
    finally {
        try { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } catch {}
        Start-Sleep -Seconds 1
        if ($isLongRunSoak) {
            if ($null -eq $previousDisableInputExit) {
                Remove-Item Env:PORTFOLIOSAVER_DISABLE_INPUT_EXIT -ErrorAction SilentlyContinue
            }
            else {
                $env:PORTFOLIOSAVER_DISABLE_INPUT_EXIT = $previousDisableInputExit
            }
        }
        Get-Process PortfolioSaver.Desktop,PortfolioSaver.Screensaver -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
finally {
    $summary.FinishedAt = (Get-Date).ToString('o')
    Write-SummaryFiles
    Stop-Transcript | Out-Null
    try {
        if ($FaultProfile -ne 'none') {
            Clear-YFinanceFaultProfile
        }
    } catch {}
    if ($null -eq $previousFaultProfilePath) {
        Remove-Item Env:DNPPV_YFINANCE_FAULT_PROFILE_PATH -ErrorAction SilentlyContinue
    }
    else {
        $env:DNPPV_YFINANCE_FAULT_PROFILE_PATH = $previousFaultProfilePath
    }
    if ($null -eq $previousFaultProfile) {
        Remove-Item Env:DNPPV_YFINANCE_FAULT_PROFILE -ErrorAction SilentlyContinue
    }
    else {
        $env:DNPPV_YFINANCE_FAULT_PROFILE = $previousFaultProfile
    }
    try {
        $localTraceTarget = Join-Path $results 'trace'
        New-Item -ItemType Directory -Force -Path $localTraceTarget | Out-Null
        foreach ($traceName in @("trace.circular.log", "trace.circular.idx", "yfinance.circular.log", "yfinance.circular.idx")) {
            $tracePath = Get-HarnessTracePath -RelativePath ("Trace\{0}" -f $traceName)
            if (Test-Path $tracePath) {
                Copy-Item -LiteralPath $tracePath -Destination (Join-Path $localTraceTarget $traceName) -Force
            }
        }
    }
    catch {
        Write-Output ("HOST_EXPORT_ERROR=" + $_.Exception.Message)
    }
    Write-Output "RESULTS=$results"
    Write-Output "SUMMARY=$summaryPath"
}


