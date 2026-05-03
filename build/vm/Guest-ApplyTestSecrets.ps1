param(
    [string]$RootPath = 'C:\vmharness\portfolio-saver',
    [string]$SecretsPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($SecretsPath)) {
    $SecretsPath = Join-Path $RootPath 'artifacts\test-secrets.json'
}

function Set-UserEnvironmentValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        [Environment]::SetEnvironmentVariable($Name, $null, 'User')
        return $false
    }

    [Environment]::SetEnvironmentVariable($Name, $Value.Trim(), 'User')
    return $true
}

$report = [ordered]@{
    GeneratedAt = (Get-Date).ToString('o')
    SecretsPath = $SecretsPath
    Applied = $false
    Keys = [ordered]@{
        PORTFOLIOSAVER_FINNHUB_API_KEY = $false
        PORTFOLIOSAVER_TWELVEDATA_API_KEY = $false
        PORTFOLIOSAVER_TIINGO_API_KEY = $false
        PORTFOLIOSAVER_FMP_API_KEY = $false
        PORTFOLIOSAVER_EODHD_API_KEY = $false
        DEEPSEEK_API_KEY = $false
        PORTFOLIOSAVER_DEEPSEEK_API_KEY = $false
    }
}

if (Test-Path $SecretsPath) {
    $secrets = Get-Content -LiteralPath $SecretsPath -Raw | ConvertFrom-Json

    $report.Keys.PORTFOLIOSAVER_FINNHUB_API_KEY = Set-UserEnvironmentValue 'PORTFOLIOSAVER_FINNHUB_API_KEY' ([string]$secrets.FinnhubApiKey)
    $report.Keys.PORTFOLIOSAVER_TWELVEDATA_API_KEY = Set-UserEnvironmentValue 'PORTFOLIOSAVER_TWELVEDATA_API_KEY' ([string]$secrets.TwelveDataApiKey)
    $report.Keys.PORTFOLIOSAVER_TIINGO_API_KEY = Set-UserEnvironmentValue 'PORTFOLIOSAVER_TIINGO_API_KEY' ([string]$secrets.TiingoApiKey)
    $report.Keys.PORTFOLIOSAVER_FMP_API_KEY = Set-UserEnvironmentValue 'PORTFOLIOSAVER_FMP_API_KEY' ([string]$secrets.FinancialModelingPrepApiKey)
    $report.Keys.PORTFOLIOSAVER_EODHD_API_KEY = Set-UserEnvironmentValue 'PORTFOLIOSAVER_EODHD_API_KEY' ([string]$secrets.EodhdApiKey)

    $deepSeekValue = [string]$secrets.DeepSeekApiKey
    $report.Keys.DEEPSEEK_API_KEY = Set-UserEnvironmentValue 'DEEPSEEK_API_KEY' $deepSeekValue
    $report.Keys.PORTFOLIOSAVER_DEEPSEEK_API_KEY = Set-UserEnvironmentValue 'PORTFOLIOSAVER_DEEPSEEK_API_KEY' $deepSeekValue
    $report.Applied = $true
}
else {
    foreach ($name in @(
        'PORTFOLIOSAVER_FINNHUB_API_KEY',
        'PORTFOLIOSAVER_TWELVEDATA_API_KEY',
        'PORTFOLIOSAVER_TIINGO_API_KEY',
        'PORTFOLIOSAVER_FMP_API_KEY',
        'PORTFOLIOSAVER_EODHD_API_KEY',
        'DEEPSEEK_API_KEY',
        'PORTFOLIOSAVER_DEEPSEEK_API_KEY'))
    {
        [Environment]::SetEnvironmentVariable($name, $null, 'User')
    }
}

$logsRoot = Join-Path $RootPath 'logs'
New-Item -ItemType Directory -Force -Path $logsRoot | Out-Null
$reportPath = Join-Path $logsRoot ("test-secrets-{0:yyyyMMdd-HHmmss}.json" -f (Get-Date))
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Output ("TEST_SECRETS_REPORT=" + $reportPath)
