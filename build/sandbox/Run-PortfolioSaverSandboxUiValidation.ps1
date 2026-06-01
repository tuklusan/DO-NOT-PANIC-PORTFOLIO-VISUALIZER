[CmdletBinding()]
param(
    [ValidateSet("Online", "Offline")]
    [string]$Mode = "Online"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$workspaceRoot = "C:\Users\WDAGUtilityAccount\Desktop\PortfolioSaverWorkspace"
$workspaceResultsRoot = Join-Path $workspaceRoot ("build\sandbox\results\" + $Mode.ToLowerInvariant())
$resultsRoot = Join-Path $env:TEMP ("PortfolioSaverSandboxResults\" + $Mode.ToLowerInvariant())
$logPath = Join-Path $resultsRoot "ui-validation.log"
$resultPath = Join-Path $resultsRoot "ui-validation.json"
$secretsPath = Join-Path $workspaceRoot "build\sandbox\test-secrets.json"
$settingsRoot = Join-Path $env:LOCALAPPDATA "PortfolioSaver"
$settingsPath = Join-Path $settingsRoot "settings.json"
$providerBudgetLedgerPath = Join-Path $env:LOCALAPPDATA "PortfolioSaver\provider-query-usage.json"
$managedCacheRoot = Join-Path $env:LOCALAPPDATA "PortfolioSaver\Backgrounds\ExchangePhotoCache"
$historyCacheRoot = Join-Path $env:LOCALAPPDATA "PortfolioSaver\Caches\History"
$customBackgroundRoot = Join-Path $resultsRoot "custom-backgrounds"
$script:InputInteropLoaded = $false
$runtimeLogPaths = @(
    (Join-Path $env:TEMP "PortfolioSaver.Screensaver.runtime.log"),
    (Join-Path $env:TEMP "PortfolioSaver.Screensaver.scene.log"),
    (Join-Path $env:TEMP "PortfolioSaver.Screensaver.graph.log")
)

New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null
Remove-Item -LiteralPath $logPath,$resultPath -Force -ErrorAction SilentlyContinue

function Export-ResultsToWorkspace {
    if (-not (Test-Path $resultsRoot)) {
        return
    }

    try {
        New-Item -ItemType Directory -Force -Path $workspaceResultsRoot | Out-Null
        Get-ChildItem -LiteralPath $resultsRoot -Force -ErrorAction Stop | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $workspaceResultsRoot -Recurse -Force -ErrorAction Stop
        }
    }
    catch {
        try {
            Add-Content -LiteralPath $logPath -Value ("[{0}] Failed to export sandbox results back to workspace: {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $_.Exception.Message) -Encoding UTF8
        }
        catch {
        }
    }
}

function Write-Log {
    param([string]$Message)

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Add-Content -LiteralPath $logPath -Value "[$timestamp] $Message" -Encoding UTF8
}

function Import-TestSecrets {
    if (-not (Test-Path $secretsPath)) {
        Write-Log "No test secrets file was found at $secretsPath"
        return
    }

    $secrets = Get-Content -LiteralPath $secretsPath -Raw | ConvertFrom-Json
    if (-not [string]::IsNullOrWhiteSpace($secrets.DeepSeekApiKey)) {
        [Environment]::SetEnvironmentVariable("DEEPSEEK_API_KEY", [string]$secrets.DeepSeekApiKey, "Process")
        [Environment]::SetEnvironmentVariable("PORTFOLIOSAVER_DEEPSEEK_API_KEY", [string]$secrets.DeepSeekApiKey, "Process")
    }

    Write-Log "Loaded sandbox test API keys into process environment."
}

function Write-TestSettings {
    param(
        [bool]$UseCustomBackgroundFolder,
        [string]$CustomBackgroundFolder = ""
    )

    New-Item -ItemType Directory -Force -Path $settingsRoot | Out-Null

    $settings = [ordered]@{
        RefreshSecondsPortfolio = 10
        RefreshSecondsBenchmarks = 10
        RefreshSecondsOffHours = 10
        HttpTimeoutSeconds = 10
        BackgroundImageFolder = $managedCacheRoot
        UseCustomBackgroundImageFolder = $UseCustomBackgroundFolder
        CustomBackgroundImageFolder = $CustomBackgroundFolder
        BackgroundChangeSeconds = 10
        ShuffleBackgrounds = $true
        DimOpacity = 0.55
        DeepSeekApiKey = ""
        EnableFloatingGraphs = $true
        HistoricalLookbackDays = 14
        HistoricalRefreshHours = 1
        MaxFloatingGraphsPerTape = 4
        HistoricalCacheRootFolder = $historyCacheRoot
        EnableBouncingGraphCards = $true
        FloatingGraphVelocityMin = 22
        FloatingGraphVelocityMax = 48
        EnableFloatingClock = $true
        ClockRefreshSeconds = 1
        BackgroundIncludeSubfolders = $false
        Groups = @(
            @{
                Name = "Tape 1"
                Direction = 0
                Speed = 1.0
                Enabled = $true
                Tickers = @(
                    @{ Symbol = "AAPL"; DisplayName = "Apple"; Enabled = $true },
                    @{ Symbol = "BRK.B"; DisplayName = "Berkshire Hathaway Class B"; Enabled = $true },
                    @{ Symbol = "TSM"; DisplayName = "Taiwan Semiconductor ADR"; Enabled = $true },
                    @{ Symbol = "SPY"; DisplayName = "SPDR S&P 500 ETF"; Enabled = $true },
                    @{ Symbol = "QQQ"; DisplayName = "Invesco QQQ Trust"; Enabled = $true }
                )
            },
            @{
                Name = "Tape 2"
                Direction = 1
                Speed = 1.10
                Enabled = $true
                Tickers = @(
                    @{ Symbol = "BTC-USD"; DisplayName = "Bitcoin / U.S. Dollar"; Enabled = $true },
                    @{ Symbol = "ETH-USD"; DisplayName = "Ether / U.S. Dollar"; Enabled = $true },
                    @{ Symbol = "EURUSD=X"; DisplayName = "Euro / U.S. Dollar"; Enabled = $true },
                    @{ Symbol = "JPY=X"; DisplayName = "U.S. Dollar / Japanese Yen"; Enabled = $true },
                    @{ Symbol = "VTSAX"; DisplayName = "Vanguard Total Stock Market Index Fund"; Enabled = $true },
                    @{ Symbol = "SWVXX"; DisplayName = "Schwab Prime Advantage Money Fund"; Enabled = $true }
                )
            },
            @{
                Name = "Tape 3"
                Direction = 0
                Speed = 1.20
                Enabled = $true
                Tickers = @(
                    @{ Symbol = "^GSPC"; DisplayName = "S&P 500 Index"; Enabled = $true },
                    @{ Symbol = "^VIX"; DisplayName = "Volatility Index"; Enabled = $true },
                    @{ Symbol = "^TNX"; DisplayName = "CBOE 10-Year Treasury Yield Index"; Enabled = $true },
                    @{ Symbol = "DX-Y.NYB"; DisplayName = "U.S. Dollar Index"; Enabled = $true },
                    @{ Symbol = "VNQ"; DisplayName = "Vanguard Real Estate ETF"; Enabled = $true },
                    @{ Symbol = "XLRE"; DisplayName = "Real Estate Select Sector SPDR Fund"; Enabled = $true }
                )
            },
            @{
                Name = "Tape 4"
                Direction = 1
                Speed = 1.35
                Enabled = $true
                Tickers = @(
                    @{ Symbol = "ES=F"; DisplayName = "E-mini S&P 500 Futures"; Enabled = $true },
                    @{ Symbol = "NQ=F"; DisplayName = "E-mini Nasdaq-100 Futures"; Enabled = $true },
                    @{ Symbol = "ZN=F"; DisplayName = "10-Year T-Note Futures"; Enabled = $true },
                    @{ Symbol = "BZ=F"; DisplayName = "Brent Crude"; Enabled = $true },
                    @{ Symbol = "GC=F"; DisplayName = "Gold Futures"; Enabled = $true }
                )
            }
        )
        Benchmarks = @(
            @{ Symbol = "SPY"; DisplayName = "S&P 500"; Enabled = $true },
            @{ Symbol = "^GSPC"; DisplayName = "S&P 500 Index"; Enabled = $true },
            @{ Symbol = "BTC-USD"; DisplayName = "Bitcoin"; Enabled = $true },
            @{ Symbol = "ES=F"; DisplayName = "E-mini S&P 500 Futures"; Enabled = $true }
        )
    }

    $settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    Write-Log "Wrote test settings to $settingsPath"
}

function Reset-ProviderBudgetLedger {
    if (Test-Path $providerBudgetLedgerPath) {
        Remove-Item -LiteralPath $providerBudgetLedgerPath -Force -ErrorAction Stop
        Write-Log "Removed provider budget ledger at $providerBudgetLedgerPath"
    }
}

function New-CustomBackgrounds {
    Add-Type -AssemblyName System.Drawing

    New-Item -ItemType Directory -Force -Path $customBackgroundRoot | Out-Null
    Remove-Item -LiteralPath (Join-Path $customBackgroundRoot "*.png") -Force -ErrorAction SilentlyContinue

    $definitions = @(
        @{ Name = "custom-amber.png"; Start = [System.Drawing.Color]::FromArgb(255, 29, 21); End = [System.Drawing.Color]::FromArgb(255, 196, 117); Label = "Custom Folder Test" },
        @{ Name = "custom-cyan.png"; Start = [System.Drawing.Color]::FromArgb(19, 63, 84); End = [System.Drawing.Color]::FromArgb(101, 214, 201); Label = "PortfolioSaver Override" }
    )

    foreach ($definition in $definitions) {
        $bitmap = New-Object System.Drawing.Bitmap 1920,1080
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $rectangle = New-Object System.Drawing.Rectangle 0,0,1920,1080
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rectangle,$definition.Start,$definition.End,35
        $graphics.FillRectangle($brush, $rectangle)

        $font = New-Object System.Drawing.Font "Segoe UI", 48, ([System.Drawing.FontStyle]::Bold)
        $subFont = New-Object System.Drawing.Font "Segoe UI", 24, ([System.Drawing.FontStyle]::Regular)
        $graphics.DrawString($definition.Label, $font, [System.Drawing.Brushes]::White, 120, 120)
        $graphics.DrawString("Using a custom image directory inside the sandbox.", $subFont, [System.Drawing.Brushes]::WhiteSmoke, 126, 210)

        $targetPath = Join-Path $customBackgroundRoot $definition.Name
        $bitmap.Save($targetPath, [System.Drawing.Imaging.ImageFormat]::Png)

        $brush.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
        $font.Dispose()
        $subFont.Dispose()
    }

    Write-Log "Created custom background images in $customBackgroundRoot"
    return $customBackgroundRoot
}

function Capture-Screen {
    param([Parameter(Mandatory = $true)][string]$Path)

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bitmap.Size)
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Capture-ProcessWindow {
    param(
        [Parameter(Mandatory = $true)]$Process,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ($null -eq $Process) {
        return $false
    }

    try {
        $Process.Refresh()
    }
    catch {
        return $false
    }

    if ($Process.HasExited) {
        return $false
    }

    Add-Type -AssemblyName System.Drawing
    Ensure-InputInteropLoaded

    $windowHandle = Wait-ForMainWindowHandle -Process $Process -TimeoutSeconds 3
    if ($windowHandle -eq [IntPtr]::Zero) {
        return $false
    }

    $rect = New-Object SandboxInputInterop+RECT
    if (-not [SandboxInputInterop]::GetWindowRect($windowHandle, [ref]$rect)) {
        return $false
    }

    $width = [Math]::Max(1, $rect.Right - $rect.Left)
    $height = [Math]::Max(1, $rect.Bottom - $rect.Top)
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()

    try {
        $printed = [SandboxInputInterop]::PrintWindow($windowHandle, $hdc, 0)
    }
    finally {
        $graphics.ReleaseHdc($hdc)
        $graphics.Dispose()
    }

    if (-not $printed) {
        $bitmap.Dispose()
        return $false
    }

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $true
}

function Set-CaptureEnvironment {
    param(
        [Parameter(Mandatory = $true)][string]$CaptureStem,
        [Parameter(Mandatory = $true)][int[]]$CaptureSeconds
    )

    [Environment]::SetEnvironmentVariable("PORTFOLIOSAVER_CAPTURE_DIR", $resultsRoot, "Process")
    [Environment]::SetEnvironmentVariable("PORTFOLIOSAVER_CAPTURE_STEM", $CaptureStem, "Process")
    [Environment]::SetEnvironmentVariable("PORTFOLIOSAVER_CAPTURE_DELAYS", ($CaptureSeconds -join ","), "Process")
}

function Clear-CaptureEnvironment {
    [Environment]::SetEnvironmentVariable("PORTFOLIOSAVER_CAPTURE_DIR", $null, "Process")
    [Environment]::SetEnvironmentVariable("PORTFOLIOSAVER_CAPTURE_STEM", $null, "Process")
    [Environment]::SetEnvironmentVariable("PORTFOLIOSAVER_CAPTURE_DELAYS", $null, "Process")
}

function Ensure-InputInteropLoaded {
    if ($script:InputInteropLoaded) {
        return
    }

    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class SandboxInputInterop
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;

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

    $script:InputInteropLoaded = $true
}

function Ensure-UiAutomationLoaded {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
}

function Get-WindowAutomationElement {
    param([Parameter(Mandatory = $true)][string]$WindowTitle)

    Ensure-UiAutomationLoaded
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $WindowTitle)

    $deadline = (Get-Date).AddSeconds(12)
    do {
        $element = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            $condition)
        if ($element) {
            return $element
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    return $null
}

function Find-EditElementByValue {
    param(
        [Parameter(Mandatory = $true)]$WindowElement,
        [Parameter(Mandatory = $true)][string]$CurrentValue
    )

    Ensure-UiAutomationLoaded
    $editCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    $edits = $WindowElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
    foreach ($edit in $edits) {
        try {
            $pattern = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            if ($pattern.Current.Value -eq $CurrentValue) {
                return $edit
            }
        }
        catch {
        }
    }

    return $null
}

function Set-EditElementValue {
    param(
        [Parameter(Mandatory = $true)]$Element,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $pattern = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($Value)
    Start-Sleep -Milliseconds 250
}

function Invoke-ButtonByName {
    param(
        [Parameter(Mandatory = $true)]$WindowElement,
        [Parameter(Mandatory = $true)][string]$ButtonName
    )

    Ensure-UiAutomationLoaded
    $buttonCondition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $ButtonName)))

    $button = $WindowElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
    if ($button -eq $null) {
        return $false
    }

    try {
        $invoke = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $invoke.Invoke()
        Start-Sleep -Milliseconds 400
        return $true
    }
    catch {
        return $false
    }
}

function Select-TabItemByName {
    param(
        [Parameter(Mandatory = $true)]$WindowElement,
        [Parameter(Mandatory = $true)][string]$TabName
    )

    Ensure-UiAutomationLoaded
    $tabCondition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::TabItem)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $TabName)))

    $tab = $WindowElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCondition)
    if ($tab -eq $null) {
        return $false
    }

    try {
        $selection = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.Select()
        Start-Sleep -Milliseconds 500
        return $true
    }
    catch {
        return $false
    }
}

function Wait-ForDialogAndDismiss {
    param(
        [Parameter(Mandatory = $true)][string]$DialogTitle,
        [string]$ButtonName = "OK"
    )

    $dialog = Get-WindowAutomationElement -WindowTitle $DialogTitle
    if ($dialog -eq $null) {
        return $false
    }

    [void](Invoke-ButtonByName -WindowElement $dialog -ButtonName $ButtonName)
    return $true
}

function Resize-ProcessWindow {
    param(
        [Parameter(Mandatory = $true)]$Process,
        [Parameter(Mandatory = $true)][int]$Width,
        [Parameter(Mandatory = $true)][int]$Height
    )

    Ensure-InputInteropLoaded
    [IntPtr]$windowHandle = Wait-ForMainWindowHandle -Process $Process -TimeoutSeconds 6
    if ($windowHandle -eq [IntPtr]::Zero) {
        return $false
    }

    $rect = New-Object SandboxInputInterop+RECT
    if (-not [SandboxInputInterop]::GetWindowRect($windowHandle, [ref]$rect)) {
        return $false
    }

    [void][SandboxInputInterop]::MoveWindow($windowHandle, $rect.Left, $rect.Top, $Width, $Height, $true)
    Start-Sleep -Milliseconds 500
    return $true
}

function Wait-ForMainWindowHandle {
    param(
        [Parameter(Mandatory = $true)]$Process,
        [int]$TimeoutSeconds = 10
    )

    if ($null -eq $Process) {
        return [IntPtr]::Zero
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $Process.Refresh()
        }
        catch {
            return [IntPtr]::Zero
        }

        if ($Process.HasExited) {
            return [IntPtr]::Zero
        }

        if ($Process.MainWindowHandle -ne 0) {
            return $Process.MainWindowHandle
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    return [IntPtr]::Zero
}

function Click-WindowPoint {
    param(
        [Parameter(Mandatory = $true)][IntPtr]$WindowHandle,
        [double]$RelativeX,
        [double]$RelativeY
    )

    Ensure-InputInteropLoaded
    $rect = New-Object SandboxInputInterop+RECT
    if (-not [SandboxInputInterop]::GetWindowRect($WindowHandle, [ref]$rect)) {
        return $false
    }

    [SandboxInputInterop]::SetForegroundWindow($WindowHandle) | Out-Null
    Start-Sleep -Milliseconds 250

    $targetX = [int]($rect.Left + $RelativeX)
    $targetY = [int]($rect.Top + $RelativeY)
    [SandboxInputInterop]::SetCursorPos($targetX, $targetY) | Out-Null
    Start-Sleep -Milliseconds 150
    [SandboxInputInterop]::mouse_event([SandboxInputInterop]::MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [SandboxInputInterop]::mouse_event([SandboxInputInterop]::MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
    return $true
}

function Focus-ProcessWindow {
    param([Parameter(Mandatory = $true)]$Process)

    if ($null -eq $Process) {
        return $false
    }

    try {
        $Process.Refresh()
    }
    catch {
        return $false
    }

    if ($Process.HasExited) {
        return $false
    }

    Ensure-InputInteropLoaded
    [IntPtr]$windowHandle = Wait-ForMainWindowHandle -Process $Process -TimeoutSeconds 8
    if ($windowHandle -eq [IntPtr]::Zero) {
        try {
            $shell = New-Object -ComObject WScript.Shell
            $null = $shell.AppActivate($Process.Id)
            Start-Sleep -Milliseconds 400
            $Process.Refresh()
            if (-not $Process.HasExited) {
                $windowHandle = Wait-ForMainWindowHandle -Process $Process -TimeoutSeconds 3
            }
        }
        catch {
        }
    }

    if ($windowHandle -ne [IntPtr]::Zero) {
        [SandboxInputInterop]::SetForegroundWindow($windowHandle) | Out-Null
        Start-Sleep -Milliseconds 450
        return $true
    }

    return $false
}

function Send-KeysToProcess {
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][string]$Keys
    )

    $shell = New-Object -ComObject WScript.Shell
    $null = $shell.AppActivate($ProcessId)
    Start-Sleep -Milliseconds 250
    $shell.SendKeys($Keys)
}

function Send-KeysToForeground {
    param([Parameter(Mandatory = $true)][string]$Keys)

    $shell = New-Object -ComObject WScript.Shell
    Start-Sleep -Milliseconds 250
    $shell.SendKeys($Keys)
}

function Exercise-ConfigWindow {
    param(
        [Parameter(Mandatory = $true)]$Process
    )

    $windowHandle = Wait-ForMainWindowHandle -Process $Process
    if ($windowHandle -eq [IntPtr]::Zero) {
        Write-Log "Config window did not expose a main window handle in time."
        return
    }

    [void](Resize-ProcessWindow -Process $Process -Width 1040 -Height 700)
    $compactCapturePath = Join-Path $resultsRoot "config-compact-online.png"
    Capture-ProcessWindow -Process $Process -Path $compactCapturePath | Out-Null
    Write-Log "Captured config-compact-online.png after resizing the config window to a smaller layout."

    if (Click-WindowPoint -WindowHandle $windowHandle -RelativeX 940 -RelativeY 640) {
        Start-Sleep -Milliseconds 300
        Send-KeysToForeground -Keys '{PGDN}{PGDN}{PGDN}{PGDN}'
        Start-Sleep -Milliseconds 500
        $groupsCapturePath = Join-Path $resultsRoot "config-groups-online.png"
        Capture-ProcessWindow -Process $Process -Path $groupsCapturePath | Out-Null
        Write-Log "Captured config-groups-online.png after paging down to the tape editor section."
        Send-KeysToForeground -Keys '^{HOME}'
        Start-Sleep -Milliseconds 400
    }

    [void](Resize-ProcessWindow -Process $Process -Width 1200 -Height 760)

    $windowElement = Get-WindowAutomationElement -WindowTitle "Portfolio Screensaver Config - BETA-1"
    if ($windowElement -ne $null) {
        $firstTicker = Find-EditElementByValue -WindowElement $windowElement -CurrentValue "AAPL"
        if ($firstTicker -ne $null) {
            Set-EditElementValue -Element $firstTicker -Value "BTC-USD"
            Write-Log "Updated the first tape symbol from AAPL to BTC-USD using UI Automation."
        }

        $benchmarkTicker = Find-EditElementByValue -WindowElement $windowElement -CurrentValue "^GSPC"
        if ($benchmarkTicker -ne $null) {
            Set-EditElementValue -Element $benchmarkTicker -Value "NOTREALZZZ"
            Write-Log "Updated one configured symbol to NOTREALZZZ to exercise Apply-time validation."
        }
    }

    # Click Preview, dismiss the preview with Escape, then click Save.
    if (Click-WindowPoint -WindowHandle $windowHandle -RelativeX 995 -RelativeY 722) {
        Write-Log "Clicked Preview in the config window."
        Start-Sleep -Seconds 3
        $previewProcess = Get-Process PortfolioSaver.Screensaver -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($previewProcess) {
            [void](Focus-ProcessWindow -Process $previewProcess)
        }
        $previewCapturePath = Join-Path $resultsRoot "config-preview-online.png"
        Capture-Screen -Path $previewCapturePath
        Write-Log "Captured config-preview-online.png while preview was active."
        Start-Sleep -Seconds 2
        Send-KeysToForeground -Keys '{ESC}'
        Write-Log "Sent Escape to dismiss preview from the config window."
        Start-Sleep -Seconds 1
    }

    if ($windowElement -ne $null -and (Invoke-ButtonByName -WindowElement $windowElement -ButtonName "Apply")) {
        Write-Log "Invoked Apply after introducing an invalid symbol."
        $invalidDialogCapturePath = Join-Path $resultsRoot "config-invalid-dialog.png"
        Capture-Screen -Path $invalidDialogCapturePath
        $dialogDismissed = Wait-ForDialogAndDismiss -DialogTitle "Ticker Validation Failed"
        if ($dialogDismissed) {
            Write-Log "Dismissed the ticker validation failure dialog."
        }
        else {
            Send-KeysToForeground -Keys '{ENTER}'
            Write-Log "Dismissed the validation dialog using Enter fallback."
        }

        $benchmarkTicker = Find-EditElementByValue -WindowElement $windowElement -CurrentValue "NOTREALZZZ"
        if ($benchmarkTicker -ne $null) {
            Set-EditElementValue -Element $benchmarkTicker -Value "^GSPC"
            Write-Log "Restored the invalid symbol back to ^GSPC through the config UI."
        }

        if (Invoke-ButtonByName -WindowElement $windowElement -ButtonName "Apply") {
            Write-Log "Invoked Apply after restoring a valid exotic symbol."
        }
    }
    elseif (Click-WindowPoint -WindowHandle $windowHandle -RelativeX 1125 -RelativeY 722) {
        Write-Log "Clicked Save in the config window."
    }

    if ($windowElement -ne $null -and (Select-TabItemByName -WindowElement $windowElement -TabName "Advanced")) {
        $advancedCapturePath = Join-Path $resultsRoot "config-advanced-online.png"
        Capture-ProcessWindow -Process $Process -Path $advancedCapturePath | Out-Null
        Write-Log "Captured config-advanced-online.png after selecting the Advanced tab."

        $perHourEditor = Find-EditElementByValue -WindowElement $windowElement -CurrentValue "3600"
        if ($perHourEditor -ne $null) {
            Set-EditElementValue -Element $perHourEditor -Value "1800"
            Write-Log "Updated one Advanced tab hourly provider limit from 3600 to 1800."
        }

        $perDayEditor = Find-EditElementByValue -WindowElement $windowElement -CurrentValue "86400"
        if ($perDayEditor -ne $null) {
            Set-EditElementValue -Element $perDayEditor -Value "24000"
            Write-Log "Updated one Advanced tab daily provider limit from 86400 to 24000."
        }

        if (Invoke-ButtonByName -WindowElement $windowElement -ButtonName "Apply") {
            Write-Log "Invoked Apply after editing Advanced tab provider limits."
        }
    }
}

function Stop-ScreensaverProcess {
    param([Parameter(Mandatory = $true)]$Process)

    if ($Process.HasExited) {
        return
    }

    try {
        Send-KeysToProcess -ProcessId $Process.Id -Keys '{ESC}'
        Start-Sleep -Seconds 1
        $Process.Refresh()
    }
    catch {
    }

    if (-not $Process.HasExited) {
        Get-Process -Id $Process.Id -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

function Start-And-CaptureSequence {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory = $true)][string]$ScreenshotBaseName,
        [int[]]$CaptureSeconds = @(8)
    )

    Set-CaptureEnvironment -CaptureStem $ScreenshotBaseName -CaptureSeconds $CaptureSeconds
    try {
        if ($ArgumentList.Count -gt 0) {
            $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -PassThru
        }
        else {
            $process = Start-Process -FilePath $FilePath -PassThru
        }
    }
    finally {
        Clear-CaptureEnvironment
    }

    $screenshots = @()
    $elapsedSeconds = 0
    for ($index = 0; $index -lt $CaptureSeconds.Count; $index++) {
        $delay = [Math]::Max(0, $CaptureSeconds[$index] - $elapsedSeconds)
        if ($delay -gt 0) {
            Start-Sleep -Seconds $delay
        }

        $elapsedSeconds = $CaptureSeconds[$index]
        $screenshotPath = Join-Path $resultsRoot ("{0}-{1}.png" -f $ScreenshotBaseName, ($index + 1))
        Start-Sleep -Milliseconds 600
        if (-not (Test-Path $screenshotPath)) {
            [void](Focus-ProcessWindow -Process $process)
            if (-not (Capture-ProcessWindow -Process $process -Path $screenshotPath)) {
                Capture-Screen -Path $screenshotPath
            }
        }
        elseif ((Get-Item $screenshotPath).Length -le 0) {
            [void](Focus-ProcessWindow -Process $process)
            Capture-Screen -Path $screenshotPath
        }
        $process.Refresh()
        Write-Log "Captured $([System.IO.Path]::GetFileName($screenshotPath)) from $FilePath (pid=$($process.Id), exited=$($process.HasExited))"
        $screenshots += $screenshotPath
    }

    Stop-ScreensaverProcess -Process $process
    return [pscustomobject]@{
        ScreenshotPaths = $screenshots
        ProcessId = $process.Id
        HasExited = $process.HasExited
    }
}

function Start-And-Capture {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory = $true)][string]$ScreenshotName,
        [int]$DelaySeconds = 6
    )

    Set-CaptureEnvironment -CaptureStem ([System.IO.Path]::GetFileNameWithoutExtension($ScreenshotName)) -CaptureSeconds @($DelaySeconds)
    try {
        if ($ArgumentList.Count -gt 0) {
            $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -PassThru
        }
        else {
            $process = Start-Process -FilePath $FilePath -PassThru
        }
    }
    finally {
        Clear-CaptureEnvironment
    }

    Start-Sleep -Seconds $DelaySeconds
    [void](Focus-ProcessWindow -Process $process)
    $screenshotPath = Join-Path $resultsRoot $ScreenshotName
    $capturedPath = Join-Path $resultsRoot ("{0}-1.png" -f [System.IO.Path]::GetFileNameWithoutExtension($ScreenshotName))
    if (Test-Path $capturedPath) {
        Copy-Item -LiteralPath $capturedPath -Destination $screenshotPath -Force
    }
    elseif (-not (Capture-ProcessWindow -Process $process -Path $screenshotPath)) {
        Capture-Screen -Path $screenshotPath
    }
    $process.Refresh()
    Write-Log "Captured $ScreenshotName from $FilePath (pid=$($process.Id), exited=$($process.HasExited))"
    return [pscustomobject]@{
        ScreenshotPath = $screenshotPath
        ProcessId = $process.Id
        HasExited = $process.HasExited
    }
}

function Get-ManagedCacheSnapshot {
    if (-not (Test-Path $managedCacheRoot)) {
        return [pscustomobject]@{
            Exists = $false
            Files = @()
        }
    }

    $files = Get-ChildItem -LiteralPath $managedCacheRoot -File | Select-Object Name, Length
    return [pscustomobject]@{
        Exists = $true
        Files = $files
    }
}

function Get-InstalledStateSnapshot {
    $scrPath = Join-Path $env:WINDIR "System32\PortfolioSaver.Screensaver.scr"
    $manifestPath = Join-Path $env:ProgramData "PortfolioSaverScreensaver\installed-files.txt"
    $uninstallKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PortfolioSaverScreensaver"

    return [pscustomobject]@{
        ScreensaverExists = Test-Path $scrPath
        ManifestExists = Test-Path $manifestPath
        UninstallKeyExists = Test-Path $uninstallKey
    }
}

function Test-InstalledState {
    param([bool]$ExpectedInstalled)

    $snapshot = Get-InstalledStateSnapshot
    $checks = @(
        $snapshot.ScreensaverExists,
        $snapshot.ManifestExists,
        $snapshot.UninstallKeyExists
    )

    return ($checks | Where-Object { $_ -eq $ExpectedInstalled }).Count -eq $checks.Count
}

$summary = [ordered]@{
    Mode = $Mode
    StartedAt = (Get-Date).ToString("o")
    OnlineConfigScreenshot = $null
    OnlineAdvancedScreenshot = $null
    OnlinePreviewScreenshot = $null
    OnlineManagedScreenshots = @()
    OnlineCustomScreenshot = $null
    OfflineScreenshot = $null
    ManagedCacheBeforeUninstall = $null
    ManagedCacheRemovedAfterUninstall = $null
    HistoryCacheRemovedAfterUninstall = $null
    InstallSucceeded = $false
    UninstallSucceeded = $false
    Success = $false
}

try {
    Write-Log "Starting sandbox UI validation in $Mode mode."
    Import-TestSecrets

    $stageRoot = Join-Path $workspaceRoot "build\artifacts\installer-stage"
    $installerScript = Join-Path $stageRoot "Install-PortfolioSaverScreensaver.ps1"
    $uninstallScript = Join-Path $stageRoot "Uninstall-PortfolioSaverScreensaver.ps1"

    Write-Log "Running installer script: $installerScript"
    $installOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerScript 2>&1
    foreach ($line in @($installOutput)) {
        Write-Log "[install] $line"
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Installer exited with code $LASTEXITCODE."
    }

    $postInstallState = Get-InstalledStateSnapshot
    Write-Log "Post-install state: scr=$($postInstallState.ScreensaverExists) manifest=$($postInstallState.ManifestExists) uninstallKey=$($postInstallState.UninstallKeyExists)"
    if (-not (Test-InstalledState -ExpectedInstalled $true)) {
        throw "Install validation failed."
    }

    $summary.InstallSucceeded = $true
    $screensaverPath = Join-Path $env:WINDIR "System32\PortfolioSaver.Screensaver.scr"
    $configPath = Join-Path $env:WINDIR "System32\PortfolioSaver.Config.exe"

    if ($Mode -eq "Online") {
        Reset-ProviderBudgetLedger
        Write-TestSettings -UseCustomBackgroundFolder $false

        $configCapture = Start-And-Capture -FilePath $configPath -ScreenshotName "config-online.png" -DelaySeconds 5
        $summary.OnlineConfigScreenshot = $configCapture.ScreenshotPath
        $configProcess = Get-Process PortfolioSaver.Config -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($configProcess) {
            Exercise-ConfigWindow -Process $configProcess
        }
        $summary.OnlineAdvancedScreenshot = Join-Path $resultsRoot "config-advanced-online.png"
        $summary.OnlinePreviewScreenshot = Join-Path $resultsRoot "config-preview-online.png"
        Get-Process PortfolioSaver.Config -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

        $managedCapture = Start-And-CaptureSequence -FilePath $screensaverPath -ArgumentList @("/s") -ScreenshotBaseName "managed-online" -CaptureSeconds @(10, 35, 70)
        $summary.OnlineManagedScreenshots = @($managedCapture.ScreenshotPaths)

        $summary.ManagedCacheBeforeUninstall = Get-ManagedCacheSnapshot
        Write-Log "Managed cache files: $((@($summary.ManagedCacheBeforeUninstall.Files)).Count)"

        $customFolder = New-CustomBackgrounds
        Reset-ProviderBudgetLedger
        Write-TestSettings -UseCustomBackgroundFolder $true -CustomBackgroundFolder $customFolder
        $customCapture = Start-And-CaptureSequence -FilePath $screensaverPath -ArgumentList @("/s") -ScreenshotBaseName "custom-folder-online" -CaptureSeconds @(18, 42)
        $summary.OnlineCustomScreenshot = $customCapture.ScreenshotPaths[0]
    }
    else {
        Reset-ProviderBudgetLedger
        Write-TestSettings -UseCustomBackgroundFolder $false
        $offlineCapture = Start-And-CaptureSequence -FilePath $screensaverPath -ArgumentList @("/s") -ScreenshotBaseName "offline-overlay" -CaptureSeconds @(12, 28)
        $summary.OfflineScreenshot = $offlineCapture.ScreenshotPaths[0]
        $summary.ManagedCacheBeforeUninstall = Get-ManagedCacheSnapshot
    }

    Write-Log "Running uninstall script: $uninstallScript"
    $uninstallOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $uninstallScript 2>&1
    foreach ($line in @($uninstallOutput)) {
        Write-Log "[uninstall] $line"
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Uninstall exited with code $LASTEXITCODE."
    }

    $postUninstallState = Get-InstalledStateSnapshot
    Write-Log "Post-uninstall state: scr=$($postUninstallState.ScreensaverExists) manifest=$($postUninstallState.ManifestExists) uninstallKey=$($postUninstallState.UninstallKeyExists)"
    if (-not (Test-InstalledState -ExpectedInstalled $false)) {
        throw "Uninstall validation failed."
    }

    $summary.UninstallSucceeded = $true
    $summary.ManagedCacheRemovedAfterUninstall = -not (Test-Path $managedCacheRoot)
    $summary.HistoryCacheRemovedAfterUninstall = -not (Test-Path $historyCacheRoot)
    $summary.Success = $summary.InstallSucceeded -and $summary.UninstallSucceeded
}
catch {
    Write-Log "UI validation failed: $($_.Exception.Message)"
    if ($_.InvocationInfo -ne $null) {
        Write-Log "Failure location: line $($_.InvocationInfo.ScriptLineNumber)"
        if (-not [string]::IsNullOrWhiteSpace($_.InvocationInfo.PositionMessage)) {
            Write-Log $_.InvocationInfo.PositionMessage
        }
    }
    $summary.Error = $_.Exception.ToString()
}
finally {
    foreach ($runtimeLogPath in $runtimeLogPaths) {
        if (Test-Path $runtimeLogPath) {
            Copy-Item -LiteralPath $runtimeLogPath -Destination (Join-Path $resultsRoot ([System.IO.Path]::GetFileName($runtimeLogPath))) -Force
        }
    }
    $summary.FinishedAt = (Get-Date).ToString("o")
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    Write-Log "UI validation finished. Results written to $resultPath"
    Export-ResultsToWorkspace
}
