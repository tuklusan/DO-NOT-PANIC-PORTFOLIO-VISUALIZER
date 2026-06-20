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

    $offlinePassRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000001'
    New-Item -ItemType Directory -Force -Path $offlinePassRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000001'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-during-runtime' } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $offlinePassRun 'ux-deep-summary.json') -Encoding UTF8
    '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=offline' |
        Set-Content -LiteralPath (Join-Path $offlinePassRun 'fault-injection-events.log') -Encoding UTF8
    'event=RuntimeQuoteRequestFailed / data_freshness_text=OFFLINE - showing last values' |
        Set-Content -LiteralPath (Join-Path $offlinePassRun 'combined-trace-tail.txt') -Encoding UTF8
    $offlinePassAnalysisPath = Join-Path $tempRoot 'offline-pass-analysis.json'
    $offlinePassOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $offlinePassRun -OutputPath $offlinePassAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($offlinePassOutput -match 'ANALYSIS_REPORT=')) { throw 'Offline-pass analysis did not emit ANALYSIS_REPORT.' }
    $offlinePassReport = Get-Content -Raw -LiteralPath $offlinePassAnalysisPath | ConvertFrom-Json
    if (-not $offlinePassReport.clean) { throw 'Analyze-VisualValidationArtifacts reported findings for the offline UX proof fixture.' }

    $offlineFailRun = Join-Path $tempRoot 'ux-deep-ssh-20990101-000002'
    New-Item -ItemType Directory -Force -Path $offlineFailRun | Out-Null
    @{ ResultName = 'ux-deep-ssh-20990101-000002'; ConfigPhaseStatus = 'Completed'; DesktopPhaseStatus = 'Completed'; FullScreenToggleStatus = 'Completed'; FaultProfile = 'offline-during-runtime' } |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $offlineFailRun 'ux-deep-summary.json') -Encoding UTF8
    '2026-01-01T00:00:00Z event=FaultProfileSet details=profile=offline' |
        Set-Content -LiteralPath (Join-Path $offlineFailRun 'fault-injection-events.log') -Encoding UTF8
    'event=RuntimeQuoteRequestFailed / data_freshness_text=LIVE quote feed' |
        Set-Content -LiteralPath (Join-Path $offlineFailRun 'combined-trace-tail.txt') -Encoding UTF8
    $offlineFailAnalysisPath = Join-Path $tempRoot 'offline-fail-analysis.json'
    $offlineFailOutput = & (Join-Path $repoRoot 'build\validation\Analyze-VisualValidationArtifacts.ps1') -ResultRoot $offlineFailRun -OutputPath $offlineFailAnalysisPath -MinimumScreenshots 0 -SkipDeepSeekArtifactReview
    if (-not ($offlineFailOutput -match 'ANALYSIS_FINDINGS=1')) { throw "Offline-fail analysis did not report exactly one finding. Output: $offlineFailOutput" }
    $offlineFailReport = Get-Content -Raw -LiteralPath $offlineFailAnalysisPath | ConvertFrom-Json
    $offlineFinding = @($offlineFailReport.findings | Where-Object { $_.code -eq 'offline-ux-state-unverified' })
    if ($offlineFinding.Count -ne 1) { throw 'Analyze-VisualValidationArtifacts did not flag missing offline UX proof.' }

    $auditFixture = Join-Path $tempRoot 'audit-state.json'
    @{
        pending_next_build_issues = @(
            @{ id = 'CR-091'; tracking_number = 'CR-091'; status = 'open'; title = 'Existing CR' }
        )
        current_priority_backlog = @(
            @{ id = 'CR-999'; tracking_number = 'CR-999'; status = 'open'; title = 'Unrelated umbrella must not affect allocation' }
        )
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $auditFixture -Encoding UTF8
    $crOutput = & (Join-Path $repoRoot 'build\validation\Add-AuditChangeRequest.ps1') `
        -AuditPath $auditFixture `
        -Title 'Smoke-created CR' `
        -Area 'validation_smoke' `
        -Severity 'Medium' `
        -Priority 1 `
        -Evidence @('Synthetic evidence')
    $expectedCurrentSchemaId = 'CR-092'
    if (-not ($crOutput -match "CHANGE_REQUEST_ID=$expectedCurrentSchemaId")) { throw "Add-AuditChangeRequest did not allocate $expectedCurrentSchemaId from current audit schema. Output: $crOutput" }
    $auditAfter = Get-Content -Raw -LiteralPath $auditFixture | ConvertFrom-Json
    $created = @($auditAfter.pending_next_build_issues | Where-Object { $_.id -eq $expectedCurrentSchemaId })
    if ($created.Count -ne 1) { throw 'Add-AuditChangeRequest did not append exactly one pending_next_build_issues entry.' }

    $legacyAuditFixture = Join-Path $tempRoot 'legacy-audit-state.json'
    @{
        change_requests = @(
            @{ id = 'CR-010'; tracking_number = 'CR-010'; status = 'open'; title = 'Legacy CR' }
        )
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $legacyAuditFixture -Encoding UTF8
    $legacyCrOutput = & (Join-Path $repoRoot 'build\validation\Add-AuditChangeRequest.ps1') `
        -AuditPath $legacyAuditFixture `
        -Title 'Smoke-created legacy CR' `
        -Area 'validation_smoke' `
        -Severity 'Medium' `
        -Priority 1 `
        -Evidence @('Synthetic evidence')
    if (-not ($legacyCrOutput -match 'CHANGE_REQUEST_ID=CR-011')) { throw "Add-AuditChangeRequest did not preserve legacy change_requests schema. Output: $legacyCrOutput" }
    $legacyAuditAfter = Get-Content -Raw -LiteralPath $legacyAuditFixture | ConvertFrom-Json
    $legacyCreated = @($legacyAuditAfter.change_requests | Where-Object { $_.id -eq 'CR-011' })
    if ($legacyCreated.Count -ne 1) { throw 'Add-AuditChangeRequest did not append exactly one legacy change_requests entry.' }

    $dualAuditFixture = Join-Path $tempRoot 'dual-audit-state.json'
    @{
        pending_next_build_issues = $null
        change_requests = @(
            @{ id = 'CR-020'; tracking_number = 'CR-020'; status = 'open'; title = 'Legacy CR' }
        )
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $dualAuditFixture -Encoding UTF8
    $dualCrOutput = & (Join-Path $repoRoot 'build\validation\Add-AuditChangeRequest.ps1') `
        -AuditPath $dualAuditFixture `
        -Title 'Smoke-created current-preferred CR' `
        -Area 'validation_smoke' `
        -Severity 'Medium' `
        -Priority 1 `
        -Evidence @('Synthetic evidence')
    if (-not ($dualCrOutput -match 'CHANGE_REQUEST_ID=CR-021')) { throw "Add-AuditChangeRequest did not allocate CR-021 from dual schema. Output: $dualCrOutput" }
    $dualAuditAfter = Get-Content -Raw -LiteralPath $dualAuditFixture | ConvertFrom-Json
    $dualCreated = @($dualAuditAfter.pending_next_build_issues | Where-Object { $_.id -eq 'CR-021' })
    if ($dualCreated.Count -ne 1) { throw 'Add-AuditChangeRequest did not prefer pending_next_build_issues in a dual-schema audit file.' }
    $legacyCount = @($dualAuditAfter.change_requests | Where-Object { $null -ne $_ }).Count
    if ($legacyCount -ne 1) { throw 'Add-AuditChangeRequest unexpectedly modified legacy change_requests in a dual-schema audit file.' }

    $invalidAuditFixture = Join-Path $tempRoot 'invalid-audit-state.json'
    @{ unrelated = @() } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $invalidAuditFixture -Encoding UTF8
    $invalidSucceeded = $false
    try {
        & (Join-Path $repoRoot 'build\validation\Add-AuditChangeRequest.ps1') `
            -AuditPath $invalidAuditFixture `
            -Title 'Should fail' `
            -Area 'validation_smoke' `
            -Severity 'Medium' `
            -Evidence @('Synthetic evidence') | Out-Null
        $invalidSucceeded = $true
    }
    catch {
        if ($_.Exception.Message -notmatch 'pending_next_build_issues or change_requests') {
            throw
        }
    }
    if ($invalidSucceeded) { throw 'Add-AuditChangeRequest accepted an invalid audit schema.' }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}

Write-Output 'VALIDATION_SCRIPT_SMOKE_TEST=Passed'
