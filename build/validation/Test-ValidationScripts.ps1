param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$scriptPaths = @(
    'build\validation\Add-AuditChangeRequest.ps1',
    'build\validation\Analyze-VisualValidationArtifacts.ps1',
    'build\validation\Invoke-DeepSeekArtifactReview.ps1',
    'build\validation\Invoke-AutonomousVisualValidation.ps1',
    'build\vm\Invoke-VmBuildTest.ps1'
)

foreach ($relativePath in $scriptPaths) {
    $path = Join-Path $repoRoot $relativePath
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors) {
        $messages = ($parseErrors | ForEach-Object { $_.Message }) -join '; '
        throw "PowerShell parser failed for ${relativePath}: $messages"
    }
}

$allowList = Join-Path $repoRoot 'build\validation\allowed-trace-patterns.txt'
if (-not (Test-Path -LiteralPath $allowList)) { throw 'Missing allowed-trace-patterns.txt.' }
if ([string]::IsNullOrWhiteSpace((Get-Content -Raw -LiteralPath $allowList))) { throw 'allowed-trace-patterns.txt is empty.' }

$gitignore = Join-Path $repoRoot '.gitignore'
if (Test-Path -LiteralPath $gitignore) {
    $ignored = Select-String -LiteralPath $gitignore -Pattern '^build/validation/artifacts/$' -Quiet
    if (-not $ignored) { throw 'Generated validation artifact directory is not ignored.' }
}

$autonomousScript = Join-Path $repoRoot 'build\validation\Invoke-AutonomousVisualValidation.ps1'
$autonomousText = Get-Content -Raw -LiteralPath $autonomousScript
if ($autonomousText -notmatch "'build/vm'") { throw 'Autonomous validation default CommitPaths does not include build/vm.' }

$vmScript = Join-Path $repoRoot 'build\vm\Invoke-VmBuildTest.ps1'
$vmText = Get-Content -Raw -LiteralPath $vmScript
$pathValidationIndex = $vmText.IndexOf('Test-Path $localResultDir', [StringComparison]::Ordinal)
$stdoutEmissionIndex = $vmText.IndexOf('Write-Output $localResultDirLine[0]', [StringComparison]::Ordinal)
if ($pathValidationIndex -lt 0 -or $stdoutEmissionIndex -lt 0 -or $stdoutEmissionIndex -lt $pathValidationIndex) { throw 'Invoke-VmBuildTest does not emit a validated LOCAL_RESULT_DIR on stdout.' }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('dnppv-validation-smoke-' + [Guid]::NewGuid().ToString('N'))
try {
    $singleRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000000'
    New-Item -ItemType Directory -Force -Path $singleRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000000'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed' } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $singleRun 'ux-deep-summary.json') -Encoding UTF8
    $analysisPath = Join-Path $tempRoot 'analysis.json'
    try {
        $analysisOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $singleRun -OutputPath $analysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    }
    catch {
        throw "Analyze-VisualValidationArtifacts failed for a single run directory: $($_.Exception.Message)"
    }
    if (-not ($analysisOutput -match 'ANALYSIS_REPORT=')) { throw 'Analyze-VisualValidationArtifacts did not emit ANALYSIS_REPORT.' }
    $report = Get-Content -Raw -LiteralPath $analysisPath | ConvertFrom-Json
    if (-not $report.clean) { throw 'Analyze-VisualValidationArtifacts reported findings for the clean single-run smoke fixture.' }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}

Write-Output 'VALIDATION_SCRIPT_SMOKE_TEST=Passed'
