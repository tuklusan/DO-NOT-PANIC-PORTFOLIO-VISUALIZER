# ============================================================================
# Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
# Proprietary rights reserved except as expressly licensed herein.
#
# DO NOT PANIC PORTFOLIO VISUALIZER
# This file is governed by the SANYALnet Labs Non-Commercial License in the
# root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
# for AI/ML model training are prohibited unless separately authorized.
#
# Attribution is required: "Based on original work by Supratim Sanyal of
# SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
# patent, trademark, and governing-law provisions.
# ============================================================================
param(
    [string]$RootPath = (Join-Path $env:USERPROFILE 'Desktop\PortfolioVmUx'),
    [string]$ResultName = ('vm-ux-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
)

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

$root = $RootPath
$configExe = Join-Path $root 'publish\config\PortfolioSaver.Config.exe'
$desktopExe = Join-Path $root 'publish\desktop\PortfolioSaver.Desktop.exe'
$results = Join-Path $root ('results\' + $ResultName)
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
if (-not (Test-Path $desktopExe)) { throw "Missing desktop executable: $desktopExe" }

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

$window = Get-WindowElement -WindowName 'DO NOT PANIC PORTFOLIO VISUALIZER Config - 1.0' -TimeoutSeconds 10
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

Get-Process PortfolioSaver.Config,PortfolioSaver.Desktop -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$desktop = Start-Process -FilePath $desktopExe -PassThru
Start-Sleep -Seconds 6
[void](Focus-Window -Process $desktop)
$windowed = Join-Path $results 'desktop-windowed.png'
Capture-Screen -Path $windowed
$summary.Captures += $windowed
$saver12Size = Get-ImageSize -Path $windowed
$summary.ResolutionChecks += @{
    Capture = $windowed
    ExpectedWidth = $summary.RuntimeDesktopResolution.Width
    ExpectedHeight = $summary.RuntimeDesktopResolution.Height
    ActualWidth = $saver12Size.Width
    ActualHeight = $saver12Size.Height
}

[void](Focus-Window -Process $desktop)
[System.Windows.Forms.SendKeys]::SendWait('{F11}')
Start-Sleep -Seconds 2
$fullscreen = Join-Path $results 'desktop-fullscreen.png'
Capture-Screen -Path $fullscreen
$summary.Captures += $fullscreen

[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Seconds 2
$windowedAfterEsc = Join-Path $results 'desktop-windowed-after-esc.png'
Capture-Screen -Path $windowedAfterEsc
$summary.Captures += $windowedAfterEsc

Start-Sleep -Seconds 24
$desktop24 = Join-Path $results 'desktop-24s.png'
Capture-Screen -Path $desktop24
$summary.Captures += $desktop24

Start-Sleep -Seconds 30
$desktop54 = Join-Path $results 'desktop-54s.png'
Capture-Screen -Path $desktop54
$summary.Captures += $desktop54

Get-Process PortfolioSaver.Desktop -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$summary.FinishedAt = (Get-Date).ToString('o')
$summaryPath = Join-Path $results 'vm-ux-summary.json'
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Output "RESULTS=$results"
Write-Output "SUMMARY=$summaryPath"

