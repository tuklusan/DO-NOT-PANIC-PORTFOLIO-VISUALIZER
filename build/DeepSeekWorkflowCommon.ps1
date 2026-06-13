Set-StrictMode -Version Latest

function Get-RepoRoot {
    $root = & git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
        throw 'git repository root could not be resolved.'
    }

    return $root.Trim()
}

function Get-DeepSeekApiKey {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $key = [Environment]::GetEnvironmentVariable('DEEPSEEK_API_KEY')
    if (-not [string]::IsNullOrWhiteSpace($key)) { return $key }

    $key = [Environment]::GetEnvironmentVariable('PORTFOLIOSAVER_DEEPSEEK_API_KEY')
    if (-not [string]::IsNullOrWhiteSpace($key)) { return $key }

    # Local-only ignored test secret overlay. This file must never be committed.
    $secretsPath = Join-Path $RepositoryRoot 'build\vm\test-secrets.json'
    if (Test-Path -LiteralPath $secretsPath) {
        try {
            $secrets = Get-Content -Raw -LiteralPath $secretsPath | ConvertFrom-Json
            if ($secrets.PSObject.Properties.Name -contains 'DeepSeekApiKey' -and
                -not [string]::IsNullOrWhiteSpace([string]$secrets.DeepSeekApiKey)) {
                return [string]$secrets.DeepSeekApiKey
            }
        }
        catch {
            Write-Warning "Invalid JSON in build\vm\test-secrets.json; fix or delete the file if DeepSeek key resolution needs it. $($_.Exception.Message)"
        }
    }

    throw "DeepSeek API access is mandatory for this project's workflow, but no DeepSeek key was found in DEEPSEEK_API_KEY, PORTFOLIOSAVER_DEEPSEEK_API_KEY, or build\vm\test-secrets.json. Hard stop: do not commit, push, or run local/VM validation until DeepSeek access is available."
}
