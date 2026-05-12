param(
    [ValidateRange(1, 10080)]
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
        Start-Sleep -Milliseconds 300
    }
    catch {}

    try {
        $window = [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
        if ($null -ne $window) {
            $window.SetFocus()
            Start-Sleep -Milliseconds 300
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

            Start-Sleep -Milliseconds 200
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

function Write-ReferenceSpotCheck {
    param(
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][int]$CaptureIndex
    )

    $displayedSample = @(Get-LatestDisplayedTapeSample)
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

    return Get-ProcessWindowElement -Process $Process -TimeoutSeconds $TimeoutSeconds
}

function Find-DescendantByAutomationId {
    param(
        [Parameter(Mandatory = $true)]$Root,
        [Parameter(Mandatory = $true)][string]$AutomationId
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)

    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
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
    Get-Process PortfolioSaver.Config,PortfolioSaver.Desktop,PortfolioSaver.Screensaver -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    try {
        $config = Start-Process -FilePath $configExe -PassThru
        Start-Sleep -Seconds 3
        $window = Find-ConfigWindow -Process $config -TimeoutSeconds 20
        if ($null -eq $window) { throw 'Could not locate config window via UI Automation.' }
        if ([string]$window.Current.Name -like '*BETA-5.5*' -or
            [string]$window.Current.HelpText -like '*0.9.0-beta5.5*') {
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

