Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class VmUxInterop
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
"@

$root = Join-Path $env:USERPROFILE 'Desktop\PortfolioVmUx'
$configExe = Join-Path $root 'publish\config\PortfolioSaver.Config.exe'
$saverExe = Join-Path $root 'publish\screensaver\PortfolioSaver.Screensaver.exe'
$results = Join-Path $root ('results\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
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

function Wait-WindowHandle {
    param([Parameter(Mandatory=$true)]$Process,[int]$TimeoutSeconds=20)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try { $Process.Refresh() } catch {}
        if (-not $Process.HasExited -and $Process.MainWindowHandle -ne 0) {
            return [IntPtr]$Process.MainWindowHandle
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    return [IntPtr]::Zero
}

function Capture-Window {
    param([Parameter(Mandatory=$true)]$Process,[Parameter(Mandatory=$true)][string]$Path)

    $handle = Wait-WindowHandle -Process $Process -TimeoutSeconds 8
    if ($handle -eq [IntPtr]::Zero) {
        Capture-Screen -Path $Path
        return
    }

    $rect = New-Object VmUxInterop+RECT
    if (-not [VmUxInterop]::GetWindowRect($handle, [ref]$rect)) {
        Capture-Screen -Path $Path
        return
    }

    $width = [Math]::Max(1, $rect.Right - $rect.Left)
    $height = [Math]::Max(1, $rect.Bottom - $rect.Top)
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()

    $printed = $false
    try {
        $printed = [VmUxInterop]::PrintWindow($handle, $hdc, 0)
    }
    finally {
        $graphics.ReleaseHdc($hdc)
        $graphics.Dispose()
    }

    if ($printed) {
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        $bitmap.Dispose()
        Capture-Screen -Path $Path
        return
    }

    $bitmap.Dispose()
}

function Capture-WindowByScreenCrop {
    param([Parameter(Mandatory=$true)]$Process,[Parameter(Mandatory=$true)][string]$Path)

    $handle = Wait-WindowHandle -Process $Process -TimeoutSeconds 8
    if ($handle -eq [IntPtr]::Zero) {
        Capture-Screen -Path $Path
        return
    }

    $rect = New-Object VmUxInterop+RECT
    if (-not [VmUxInterop]::GetWindowRect($handle, [ref]$rect)) {
        Capture-Screen -Path $Path
        return
    }

    $width = [Math]::Max(1, $rect.Right - $rect.Left)
    $height = [Math]::Max(1, $rect.Bottom - $rect.Top)
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Get-ImageSize {
    param([Parameter(Mandatory=$true)][string]$Path)

    $image = [System.Drawing.Image]::FromFile($Path)
    try {
        return @{ Width = $image.Width; Height = $image.Height }
    }
    finally {
        $image.Dispose()
    }
}

function Focus-Window {
    param([Parameter(Mandatory=$true)]$Process)

    $handle = Wait-WindowHandle -Process $Process -TimeoutSeconds 8
    if ($handle -eq [IntPtr]::Zero) { return $false }

    [VmUxInterop]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Milliseconds 250
    return $true
}

function Resize-Window {
    param([Parameter(Mandatory=$true)]$Process,[int]$Width,[int]$Height)

    $handle = Wait-WindowHandle -Process $Process -TimeoutSeconds 8
    if ($handle -eq [IntPtr]::Zero) { return $false }

    [VmUxInterop]::MoveWindow($handle, 60, 60, $Width, $Height, $true) | Out-Null
    Start-Sleep -Milliseconds 350
    return $true
}

function Get-WindowElement {
    param([Parameter(Mandatory=$true)][string]$WindowName,[int]$TimeoutSeconds=10)

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $WindowName)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    do {
        $element = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            $condition)
        if ($element) { return $element }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)

    return $null
}

function Select-Tab {
    param([Parameter(Mandatory=$true)]$Window,[Parameter(Mandatory=$true)][string]$Name)

    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::TabItem)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name)))

    $tab = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $tab) { return $false }

    try {
        $pattern = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $pattern.Select()
        Start-Sleep -Milliseconds 500
        return $true
    }
    catch {
        return $false
    }
}

function Invoke-Button {
    param([Parameter(Mandatory=$true)]$Window,[Parameter(Mandatory=$true)][string]$Name)

    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name)))

    $button = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $button) { return $false }

    try {
        $invoke = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $invoke.Invoke()
        Start-Sleep -Milliseconds 500
        return $true
    }
    catch {
        return $false
    }
}

if (-not (Test-Path $configExe)) { throw "Missing config executable: $configExe" }
if (-not (Test-Path $saverExe)) { throw "Missing screensaver executable: $saverExe" }

$summary = [ordered]@{
    StartedAt = (Get-Date).ToString('o')
    ResultsPath = $results
    RuntimeDesktopResolution = @{
        Width = [System.Windows.Forms.SystemInformation]::VirtualScreen.Width
        Height = [System.Windows.Forms.SystemInformation]::VirtualScreen.Height
    }
    Captures = @()
    ResolutionChecks = @()
    Notes = @()
}

$config = Start-Process -FilePath $configExe -PassThru
Start-Sleep -Seconds 4
[void](Focus-Window -Process $config)

$window = Get-WindowElement -WindowName 'DO NOT PANIC PORTFOLIO VISUALIZER Config - BETA-5.5' -TimeoutSeconds 10
if ($null -eq $window) {
    $window = Get-WindowElement -WindowName 'DO NOT PANIC PORTFOLIO VISUALIZER Config' -TimeoutSeconds 5
}
if ($null -eq $window) {
    $window = Get-WindowElement -WindowName 'Portfolio Screensaver Config' -TimeoutSeconds 5
}

$profiles = @(
    @{ Name='compact'; Width=900; Height=620 },
    @{ Name='medium'; Width=1200; Height=760 },
    @{ Name='large'; Width=1600; Height=900 }
)

foreach ($profile in $profiles) {
    [void](Resize-Window -Process $config -Width $profile.Width -Height $profile.Height)
    [void](Focus-Window -Process $config)

    $mainCapture = Join-Path $results ("config-{0}-main.png" -f $profile.Name)
    Capture-Window -Process $config -Path $mainCapture
    $summary.Captures += $mainCapture
    $mainComposite = Join-Path $results ("config-{0}-main-composited.png" -f $profile.Name)
    Capture-WindowByScreenCrop -Process $config -Path $mainComposite
    $summary.Captures += $mainComposite

    $mainSize = Get-ImageSize -Path $mainCapture
    $summary.ResolutionChecks += @{
        Capture = $mainCapture
        ExpectedWidth = $profile.Width
        ExpectedHeight = $profile.Height
        ActualWidth = $mainSize.Width
        ActualHeight = $mainSize.Height
    }
    if (($mainSize.Width -lt ($profile.Width - 80)) -or ($mainSize.Height -lt ($profile.Height - 80))) {
        $summary.Notes += "Config capture '$($profile.Name)' dimension mismatch: expected approx $($profile.Width)x$($profile.Height), actual $($mainSize.Width)x$($mainSize.Height)."
    }

    if ($window -ne $null -and (Select-Tab -Window $window -Name 'Advanced')) {
        $advCapture = Join-Path $results ("config-{0}-advanced.png" -f $profile.Name)
        Capture-Window -Process $config -Path $advCapture
        $summary.Captures += $advCapture
        $advComposite = Join-Path $results ("config-{0}-advanced-composited.png" -f $profile.Name)
        Capture-WindowByScreenCrop -Process $config -Path $advComposite
        $summary.Captures += $advComposite
        [void](Select-Tab -Window $window -Name 'General')
    }
}

if ($window -ne $null -and (Invoke-Button -Window $window -Name 'Preview')) {
    Start-Sleep -Seconds 6
    $previewCapture = Join-Path $results 'config-preview.png'
    Capture-Screen -Path $previewCapture
    $summary.Captures += $previewCapture

    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Seconds 1
}

Get-Process PortfolioSaver.Config -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$saver = Start-Process -FilePath $saverExe -ArgumentList '/s' -PassThru
Start-Sleep -Seconds 12
$saver12 = Join-Path $results 'screensaver-12s.png'
Capture-Screen -Path $saver12
$summary.Captures += $saver12
$saver12Size = Get-ImageSize -Path $saver12
$summary.ResolutionChecks += @{
    Capture = $saver12
    ExpectedWidth = $summary.RuntimeDesktopResolution.Width
    ExpectedHeight = $summary.RuntimeDesktopResolution.Height
    ActualWidth = $saver12Size.Width
    ActualHeight = $saver12Size.Height
}

Start-Sleep -Seconds 24
$saver36 = Join-Path $results 'screensaver-36s.png'
Capture-Screen -Path $saver36
$summary.Captures += $saver36
$saver36Size = Get-ImageSize -Path $saver36
$summary.ResolutionChecks += @{
    Capture = $saver36
    ExpectedWidth = $summary.RuntimeDesktopResolution.Width
    ExpectedHeight = $summary.RuntimeDesktopResolution.Height
    ActualWidth = $saver36Size.Width
    ActualHeight = $saver36Size.Height
}

Start-Sleep -Seconds 30
$saver66 = Join-Path $results 'screensaver-66s.png'
Capture-Screen -Path $saver66
$summary.Captures += $saver66
$saver66Size = Get-ImageSize -Path $saver66
$summary.ResolutionChecks += @{
    Capture = $saver66
    ExpectedWidth = $summary.RuntimeDesktopResolution.Width
    ExpectedHeight = $summary.RuntimeDesktopResolution.Height
    ActualWidth = $saver66Size.Width
    ActualHeight = $saver66Size.Height
}

[void](Focus-Window -Process $saver)
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 1
Get-Process PortfolioSaver.Screensaver -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$summary.FinishedAt = (Get-Date).ToString('o')
$summaryPath = Join-Path $results 'vm-ux-summary.json'
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Output "RESULTS=$results"
Write-Output "SUMMARY=$summaryPath"

