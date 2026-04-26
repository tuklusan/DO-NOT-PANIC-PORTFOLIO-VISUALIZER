param(
    [string]$OutputName = "trace-copy"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoShare = "\\VBOXSVR\codexrepo"
if (-not (Test-Path $repoShare)) {
    throw "Repo share not available: $repoShare"
}

$traceRoot = Join-Path $env:APPDATA "PortfolioSaver\Trace"
$outRoot = Join-Path $repoShare ("build\vm\artifacts\trace\" + $OutputName)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

$logPath = Join-Path $traceRoot "trace.circular.log"
$idxPath = Join-Path $traceRoot "trace.circular.idx"

if (-not (Test-Path $logPath)) {
    throw "Trace log not found: $logPath"
}

if (-not (Test-Path $idxPath)) {
    throw "Trace index not found: $idxPath"
}

Copy-Item -LiteralPath $logPath -Destination (Join-Path $outRoot "trace.circular.log") -Force
Copy-Item -LiteralPath $idxPath -Destination (Join-Path $outRoot "trace.circular.idx") -Force

$manifest = [ordered]@{
    CopiedAt = (Get-Date).ToString("o")
    GuestUser = $env:USERNAME
    TraceRoot = $traceRoot
    OutputRoot = $outRoot
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outRoot "trace-copy.json") -Encoding UTF8
Write-Output ("TRACE_COPY_DONE=" + $outRoot)
