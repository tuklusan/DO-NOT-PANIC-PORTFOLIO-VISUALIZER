param(
    [int]$ScreensaverSeconds = 240,
    [int]$CaptureIntervalSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = Join-Path $env:USERPROFILE 'Desktop\PortfolioVmUx'
$configExe = Join-Path $root 'publish\config\PortfolioSaver.Config.exe'
$desktopExe = Join-Path $root 'publish\desktop\PortfolioSaver.Desktop.exe'
$results = Join-Path $root ('results\visual-confirm-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $results | Out-Null

function Capture-Screen {
    param([Parameter(Mandatory=$true)][string]$Name)
    $path = Join-Path $results $Name
    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bitmap.Size)
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    return $path
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
            if ($name -like '*PORTFOLIO VISUALIZER Config*') { return $child }
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Select-TabByName {
    param($Window, [string]$Name)
    $tabCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)
    $tabs = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabCondition)
    foreach ($tab in $tabs) {
        $tabName = [string]$tab.Current.Name
        if ($tabName -eq $Name) {
            try {
                $pattern = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                $pattern.Select()
                return $true
            } catch { return $false }
        }
    }
    return $false
}

function Find-TextByProcessId {
    param([int]$ProcessId, [string]$TextFragment, [int]$TimeoutSeconds = 10)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $all = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($item in $all) {
            if ($item.Current.ProcessId -ne $ProcessId) { continue }
            if ($item.Current.ControlType -ne [System.Windows.Automation.ControlType]::Text) { continue }
            $name = [string]$item.Current.Name
            if ($name -like "*$TextFragment*") { return $name }
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
    ConfigShots = @()
    ScreensaverShots = @()
    ConfigWindowTitle = ''
    ConfigGeneralSelected = $false
    ConfigAdvancedSelected = $false
    ScreensaverVersionText = ''
    ScreensaverProcessExitedEarly = $false
    Notes = @()
}

$summaryPath = Join-Path $results 'visual-confirm-summary.json'
$logPath = Join-Path $results 'visual-confirm-run.log'
Start-Transcript -Path $logPath -Force | Out-Null

try {
    Get-Process PortfolioSaver.Config,PortfolioSaver.Desktop,PortfolioSaver.Screensaver -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    $config = Start-Process -FilePath $configExe -PassThru
    Start-Sleep -Milliseconds 250
    $summary.ConfigShots += Capture-Screen 'config-001-launch-250ms-general.png'
    $window = Find-ConfigWindow -TimeoutSeconds 20
    if ($null -eq $window) { throw 'Could not locate config window.' }
    $summary.ConfigWindowTitle = [string]$window.Current.Name

    Start-Sleep -Seconds 2
    $summary.ConfigGeneralSelected = Select-TabByName -Window $window -Name 'General'
    Start-Sleep -Milliseconds 250
    $summary.ConfigShots += Capture-Screen 'config-002-general-after-first-paint.png'
    Start-Sleep -Seconds 2
    $summary.ConfigShots += Capture-Screen 'config-003-general-after-repaint.png'

    $summary.ConfigAdvancedSelected = Select-TabByName -Window $window -Name 'Advanced'
    Start-Sleep -Milliseconds 250
    $summary.ConfigShots += Capture-Screen 'config-004-advanced-immediate.png'
    Start-Sleep -Seconds 2
    $summary.ConfigShots += Capture-Screen 'config-005-advanced-after-first-paint.png'
    Start-Sleep -Seconds 2
    $summary.ConfigShots += Capture-Screen 'config-006-advanced-after-repaint.png'
}
finally {
    Get-Process PortfolioSaver.Config -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

try {
    $desktop = Start-Process -FilePath $desktopExe -PassThru
    Start-Sleep -Seconds 5
    $summary.ScreensaverVersionText = [string](Find-TextByProcessId -ProcessId $desktop.Id -TextFragment 'beta5' -TimeoutSeconds 8)

    $frames = [Math]::Max(1, [int][Math]::Ceiling($ScreensaverSeconds / [double]$CaptureIntervalSeconds))
    for ($i = 1; $i -le $frames; $i++) {
        if ($desktop.HasExited) {
            $summary.ScreensaverProcessExitedEarly = $true
            $summary.Notes += "Desktop app exited early at frame $i with exit code $($desktop.ExitCode)."
            break
        }
        $summary.ScreensaverShots += Capture-Screen ("desktop-global-{0:D3}.png" -f $i)
        Start-Sleep -Seconds $CaptureIntervalSeconds
    }
}
finally {
    try { [System.Windows.Forms.SendKeys]::SendWait('{ESC}') } catch {}
    Start-Sleep -Seconds 1
    Get-Process PortfolioSaver.Desktop,PortfolioSaver.Screensaver -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

$summary.FinishedAt = (Get-Date).ToString('o')
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Stop-Transcript | Out-Null

$repoShare = '\\VBOXSVR\codexrepo'
if (Test-Path $repoShare) {
    $hostRoot = Join-Path $repoShare 'build\vm\artifacts\visual-confirm-results'
    $hostTarget = Join-Path $hostRoot (Split-Path -Leaf $results)
    New-Item -ItemType Directory -Force -Path $hostRoot | Out-Null
    if (Test-Path $hostTarget) { Remove-Item -LiteralPath $hostTarget -Recurse -Force -ErrorAction SilentlyContinue }
    Copy-Item -LiteralPath $results -Destination $hostTarget -Recurse -Force
    Write-Output "HOST_RESULT_DIR=$hostTarget"
}
