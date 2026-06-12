param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$scriptPaths = @(
    'build\validation\Add-AuditChangeRequest.ps1',
    'build\validation\Analyze-VisualValidationArtifacts.ps1',
    'build\validation\Invoke-AutonomousVisualValidation.ps1'
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

Write-Output 'VALIDATION_SCRIPT_SMOKE_TEST=Passed'
