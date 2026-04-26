param(
    [string]$VmName = "Windows10Pro",
    [ValidateSet("publish-safe-temp", "publish-next", "publish", "auto")]
    [string]$PublishSource = "publish-safe-temp",
    [switch]$RunToolScan = $true,
    [switch]$RunDeepUx = $true,
    [int]$GuestScreensaverDurationMinutes = 6,
    [int]$GuestCaptureIntervalSeconds = 5,
    [int]$PrepWaitSeconds = 40,
    [int]$ResultTimeoutMinutes = 40
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-VBoxManagePath {
    $candidate = "D:\Program Files\Oracle\VirtualBox\VBoxManage.exe"
    if (Test-Path $candidate) { return $candidate }
    $cmd = Get-Command VBoxManage.exe -ErrorAction SilentlyContinue
    if ($null -ne $cmd) { return $cmd.Source }
    throw "VBoxManage.exe not found."
}

function Invoke-VBox {
    param([Parameter(Mandatory = $true)][string[]]$Args)

    $stderrPath = [System.IO.Path]::GetTempFileName()
    $stdoutPath = [System.IO.Path]::GetTempFileName()
    try {
        $process = Start-Process -FilePath $script:VBoxManage `
            -ArgumentList $Args `
            -NoNewWindow `
            -Wait `
            -PassThru `
            -RedirectStandardError $stderrPath `
            -RedirectStandardOutput $stdoutPath

        $stdout = if (Test-Path $stdoutPath) { [string](Get-Content -LiteralPath $stdoutPath -Raw) } else { "" }
        $stderr = if (Test-Path $stderrPath) { [string](Get-Content -LiteralPath $stderrPath -Raw) } else { "" }

        if ($process.ExitCode -ne 0) {
            $message = (@($stderr, $stdout) |
                ForEach-Object { if ($null -eq $_) { "" } else { $_.Trim() } } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join [Environment]::NewLine
            if ([string]::IsNullOrWhiteSpace($message)) {
                $message = "VBoxManage failed with exit code $($process.ExitCode)."
            }

            throw $message
        }

        return $stdout
    }
    finally {
        Remove-Item -LiteralPath $stderrPath, $stdoutPath -Force -ErrorAction SilentlyContinue
    }
}

function Send-RunDialogCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,
        [Parameter(Mandatory = $true)]
        [string]$Tag
    )

    Invoke-VBox -Args @("controlvm", $VmName, "keyboardputscancode", "e0", "5b", "13", "93", "e0", "db")
    Start-Sleep -Milliseconds 800
    Invoke-VBox -Args @("controlvm", $VmName, "keyboardputstring", $Command)
    Start-Sleep -Milliseconds 600
    Invoke-VBox -Args @("controlvm", $VmName, "keyboardputscancode", "1c", "9c")
    Start-Sleep -Seconds 10
    $shot = Join-Path $script:runDir ("launch-{0}-plus10s.png" -f $Tag)
    Invoke-VBox -Args @("controlvm", $VmName, "screenshotpng", $shot)
}

function Wait-ForNewUxResult {
    param(
        [Parameter(Mandatory = $true)]
        [datetime]$StartedAfter,
        [int]$TimeoutMinutes = 40
    )

    $resultRoot = Join-Path $script:repoRoot "build\vm\artifacts\vm-results"
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    do {
        $candidate = Get-ChildItem -LiteralPath $resultRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "ux-deep-*" -and $_.LastWriteTime -ge $StartedAfter } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1

        if ($null -ne $candidate) {
            $summaryPath = Join-Path $candidate.FullName "vm-ux-summary.json"
            if (Test-Path $summaryPath) {
                try {
                    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
                    if ($summary.ConfigPhaseStatus -ne "Pending" -and $summary.ScreensaverPhaseStatus -ne "Pending") {
                        return @{
                            ResultDir = $candidate.FullName
                            SummaryPath = $summaryPath
                        }
                    }
                }
                catch {
                    Start-Sleep -Milliseconds 500
                }
            }
        }

        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for ux-deep summary."
}

function Wait-ForToolInventory {
    param(
        [Parameter(Mandatory = $true)]
        [datetime]$StartedAfter,
        [int]$TimeoutMinutes = 10
    )

    $root = Join-Path $script:repoRoot "build\vm\tool-inventory"
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    do {
        $summary = Get-ChildItem -LiteralPath $root -Recurse -Filter "summary.json" -File -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -ge $StartedAfter } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($null -ne $summary) {
            return $summary.FullName
        }

        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for tool inventory summary."
}

$VBoxManage = Get-VBoxManagePath
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$runDir = Join-Path $repoRoot ("build\vm\artifacts\launcher-runs\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$info = Invoke-VBox -Args @("showvminfo", $VmName, "--machinereadable")
if (-not ($info | Select-String -Pattern 'VMState="running"')) {
    throw "VM '$VmName' is not running."
}
if (-not ($info | Select-String -Pattern 'SessionName="GUI/Qt"')) {
    throw "VM '$VmName' is not in GUI/Qt session."
}

try {
    Invoke-VBox -Args @("sharedfolder", "add", $VmName, "--name", "codexrepo", "--hostpath", $repoRoot, "--automount", "--transient")
}
catch {
    if ($_.Exception.Message -notmatch "already exists") {
        throw
    }
}

$prepStart = Get-Date
$prepCommand = "powershell -WindowStyle Minimized -NoProfile -ExecutionPolicy Bypass -File \\VBOXSVR\codexrepo\build\vm\Guest-PrepareVmUxFromShare.ps1 -PublishSource $PublishSource -ResetRuntimeData"
Send-RunDialogCommand -Command $prepCommand -Tag "prep"
Start-Sleep -Seconds $PrepWaitSeconds

$stageManifest = Get-ChildItem -LiteralPath (Join-Path $repoRoot "build\vm\artifacts\staged-builds") -Filter "staged-build-*.json" -File -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -ge $prepStart } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -ne $stageManifest) {
    Copy-Item -LiteralPath $stageManifest.FullName -Destination (Join-Path $runDir "staged-build.json") -Force
}

$toolSummaryPath = $null
if ($RunToolScan) {
    $toolStart = Get-Date
    $toolCommand = "powershell -WindowStyle Minimized -NoProfile -ExecutionPolicy Bypass -File \\VBOXSVR\codexrepo\build\vm\Guest-ScanVmToolset.ps1"
    Send-RunDialogCommand -Command $toolCommand -Tag "toolscan"
    $toolSummaryPath = Wait-ForToolInventory -StartedAfter $toolStart
}

$uxResult = $null
if ($RunDeepUx) {
    $uxStart = Get-Date
    $uxCommand = "powershell -WindowStyle Minimized -NoProfile -ExecutionPolicy Bypass -File \\VBOXSVR\codexrepo\build\vm\Guest-UxDeepExercise.ps1 -ScreensaverDurationMinutes $GuestScreensaverDurationMinutes -CaptureIntervalSeconds $GuestCaptureIntervalSeconds"
    Send-RunDialogCommand -Command $uxCommand -Tag "deepux"
    $uxResult = Wait-ForNewUxResult -StartedAfter $uxStart -TimeoutMinutes $ResultTimeoutMinutes
}

$report = [ordered]@{
    CompletedAt = (Get-Date).ToString("o")
    VmName = $VmName
    PublishSource = $PublishSource
    LauncherRunDir = $runDir
    StageManifest = if ($null -ne $stageManifest) { $stageManifest.FullName } else { "" }
    ToolSummary = if ($null -ne $toolSummaryPath) { $toolSummaryPath } else { "" }
    UxResultDir = if ($null -ne $uxResult) { $uxResult.ResultDir } else { "" }
    UxSummary = if ($null -ne $uxResult) { $uxResult.SummaryPath } else { "" }
}

$reportPath = Join-Path $runDir "launcher-report.json"
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
$report | ConvertTo-Json -Depth 5
