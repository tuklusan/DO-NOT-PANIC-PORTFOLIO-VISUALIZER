[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Endpoint = "https://api.deepseek.com",
    # Project default verified against the configured DeepSeek endpoint on 2026-06-04.
    [string]$Model = "deepseek-v4-flash",
    [string]$OutputDirectory = "build/deepseek-review",
    [int]$MaxFileCharacters = 100000,
    [int]$MaxPacketCharacters = 600000,
    [int]$MaxRequestBytes = 1048576,
    [int]$MaxResponseCharacters = 1000000,
    [int]$MaxTokens = 4096,
    [int]$CleanupOlderThanDays = 7,
    [switch]$SelfTest,
    [switch]$SendForReview,
    [switch]$PacketOnly,
    [switch]$AcknowledgeSecretScan,
    [switch]$AcknowledgeEndpointOverride,
    [switch]$AllowMissingKeyWaiver,
    [switch]$IncludeUntracked
)

$ErrorActionPreference = 'Stop'
$script:OutputRootForAudit = $null
$script:ReviewGateLocationPushed = $false

trap {
    if ($script:ReviewGateLocationPushed) {
        Pop-Location
        $script:ReviewGateLocationPushed = $false
    }

    throw $_
}

# The default mode builds a local packet only. Passing -SendForReview sends that
# packet to the configured DeepSeek-compatible external API. Secret scanning is
# best-effort only; inspect the packet first when a change may contain confidential
# implementation details or sensitive local-only material.

function Get-RepoRoot {
    $root = & git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "git rev-parse --show-toplevel failed."
    }

    if ([string]::IsNullOrWhiteSpace($root)) {
        throw "Run this script from inside the git repository."
    }

    return $root.Trim()
}

function Invoke-GitLines([string[]]$Arguments) {
    $output = & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }

    return @($output)
}

function Complete-ReviewGate([int]$ExitCode) {
    if ($script:ReviewGateLocationPushed) {
        Pop-Location
        $script:ReviewGateLocationPushed = $false
    }

    exit $ExitCode
}

function Get-DeepSeekApiKey([string]$RepositoryRoot) {
    $key = [Environment]::GetEnvironmentVariable('DEEPSEEK_API_KEY')
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        return $key
    }

    $key = [Environment]::GetEnvironmentVariable('PORTFOLIOSAVER_DEEPSEEK_API_KEY')
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        return $key
    }

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

    if ($AllowMissingKeyWaiver) {
        Write-Warning "DeepSeek key was not found; review gate was explicitly waived for this run."
        Write-WaiverAudit "missing-key waiver"
        return $null
    }

    throw "DeepSeek code review is mandatory for code modifications, but no DeepSeek key was found in DEEPSEEK_API_KEY, PORTFOLIOSAVER_DEEPSEEK_API_KEY, or build\vm\test-secrets.json. Use -AllowMissingKeyWaiver only when the user explicitly waives the gate for this specific change."
}

function Test-ProbablyTextFile([string]$Path) {
    $extension = [IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($extension -in @('.png', '.jpg', '.jpeg', '.ico', '.gif', '.bmp', '.zip', '.7z', '.exe', '.dll', '.pdb', '.bin', '.scr')) {
        return $false
    }

    try {
        $stream = [IO.File]::OpenRead($Path)
        try {
            $length = [Math]::Min(4096, [int]$stream.Length)
            $buffer = New-Object byte[] $length
            [void]$stream.Read($buffer, 0, $length)
            return -not ($buffer -contains 0)
        }
        finally {
            $stream.Dispose()
        }
    }
    catch {
        return $false
    }
}

function Test-SecretLikePath([string]$Path) {
    $normalized = $Path.Replace('\', '/')
    return $normalized -match '(?i)(secret|credential|api[-_]?key|token|password|private)' -or
           $normalized.EndsWith('test-secrets.json', [StringComparison]::OrdinalIgnoreCase)
}

function Write-WaiverAudit([string]$Reason) {
    if ([string]::IsNullOrWhiteSpace($script:OutputRootForAudit)) {
        throw "Cannot write DeepSeek waiver audit before the ignored output directory is initialized."
    }

    try {
        $line = "{0}`tuser={1}`treason={2}`tbranch={3}" -f (Get-Date -Format o), $env:USERNAME, $Reason, (& git branch --show-current)
        Add-Content -LiteralPath (Join-Path $script:OutputRootForAudit 'waiver-audit.log') -Value $line -Encoding UTF8
    }
    catch {
        Write-Warning "Could not write DeepSeek waiver audit log: $($_.Exception.Message)"
    }
}

function Write-SendAudit([string]$PacketPath, [string]$EndpointValue, [string]$ModelValue) {
    if ([string]::IsNullOrWhiteSpace($script:OutputRootForAudit)) {
        throw "Cannot write DeepSeek send audit before the ignored output directory is initialized."
    }

    try {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $PacketPath).Hash
        $line = "{0}`tuser={1}`tbranch={2}`tendpoint={3}`tmodel={4}`tpacketSha256={5}" -f (Get-Date -Format o), $env:USERNAME, (& git branch --show-current), $EndpointValue, $ModelValue, $hash
        Add-Content -LiteralPath (Join-Path $script:OutputRootForAudit 'send-audit.log') -Value $line -Encoding UTF8
    }
    catch {
        Write-Warning "Could not write DeepSeek send audit log: $($_.Exception.Message)"
    }
}

function Assert-NoLikelySecrets([string]$Text) {
    $patterns = @(
        '(?im)(api[_-]?key|secret|token|password)\s*[:=]\s*[''"](?!(test|example|placeholder|dummy|sample))([A-Za-z0-9_\-+/=]{16,})[''"]',
        '(?im)(?:export\s+|set\s+)?(api[_-]?key|secret|token|password)\s*[:=]\s*(sk-[A-Za-z0-9_-]{20,}|[A-Za-z0-9_\-+/=]{32,})',
        '(?im)Authorization\s*[:=]\s*[''"]Bearer\s+(sk-[A-Za-z0-9_-]{20,}|[A-Za-z0-9_\-+/=]{32,})[''"]',
        '(?im)sk-(?!test|example|placeholder|dummy|sample)[A-Za-z0-9_-]{20,}',
        '(?m)AKIA[0-9A-Z]{16}',
        '(?m)ASIA[0-9A-Z]{16}',
        '(?m)AIza[0-9A-Za-z\-_]{35}',
        '(?m)ghp_[A-Za-z0-9_]{30,}',
        '(?m)eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}',
        '(?im)[a-z][a-z0-9+.-]{2,}://[^/\s:@]{2,}:[^/\s:@]{8,}@',
        '(?im)(connectionstrings?|machinekey|clientsecret|tenantsecret)\s*[:=]\s*[''"](?!(test|example|placeholder|dummy|sample))[^''"]*(password|pwd|secret|accesskey|api[_-]?key|validationkey|decryptionkey)[^''"]{8,}[''"]',
        '(?im)(password|pwd|user id|uid)\s*=\s*[^;]{8,};',
        '(?s)-----BEGIN [A-Z ]*PRIVATE KEY-----.*?-----END [A-Z ]*PRIVATE KEY-----'
    )

    foreach ($pattern in $patterns) {
        if ($Text -match $pattern) {
            throw "Potential secret material detected in the review packet. Inspect the pending changes and remove secrets before sending to DeepSeek."
        }
    }
}

function Assert-GitIgnored([string]$Path, [string]$FailureMessage) {
    & git check-ignore -q -- $Path
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

$repoRoot = Get-RepoRoot
Push-Location $repoRoot
$script:ReviewGateLocationPushed = $true

if (-not $PSBoundParameters.ContainsKey('Endpoint')) {
    $configuredEndpoint = [Environment]::GetEnvironmentVariable('DEEPSEEK_ENDPOINT')
    if (-not [string]::IsNullOrWhiteSpace($configuredEndpoint)) {
        $Endpoint = $configuredEndpoint
    }
}

if (-not $PSBoundParameters.ContainsKey('Model')) {
    $configuredModel = [Environment]::GetEnvironmentVariable('DEEPSEEK_MODEL')
    if (-not [string]::IsNullOrWhiteSpace($configuredModel)) {
        $Model = $configuredModel
    }
}

if ([string]::IsNullOrWhiteSpace($Endpoint)) {
    throw "DeepSeek review endpoint must not be empty."
}

if ([string]::IsNullOrWhiteSpace($Model)) {
    throw "DeepSeek review model must not be empty."
}

if ($SelfTest) {
    $null = Invoke-GitLines @('version')
    $scriptText = Get-Content -Raw -LiteralPath $PSCommandPath
    $null = [ScriptBlock]::Create($scriptText)
    foreach ($requiredToken in @('$SendForReview', '$AcknowledgeSecretScan', '$AcknowledgeEndpointOverride', 'Get-DeepSeekApiKey', 'Assert-NoLikelySecrets', 'Write-WaiverAudit', 'Write-SendAudit')) {
        if ($scriptText.IndexOf($requiredToken, [StringComparison]::Ordinal) -lt 0) {
            throw "DeepSeek review gate self-test failed; missing required token $requiredToken."
        }
    }

    try {
        $uriCredentialProbe = 'mongodb+srv://' + 'reviewer:realistic-secret@cluster.example.invalid/db'
        Assert-NoLikelySecrets $uriCredentialProbe
        throw "DeepSeek review gate self-test failed; known URI credential pattern was not detected."
    }
    catch {
        if ($_.Exception.Message -notlike 'Potential secret material detected*') {
            throw
        }
    }

    try {
        Assert-NoLikelySecrets ("API_KEY=`"" + "sk-" + "selftestsecretpattern1234567890`"")
        throw "DeepSeek review gate self-test failed; known secret pattern was not detected."
    }
    catch {
        if ($_.Exception.Message -notlike 'Potential secret material detected*') {
            throw
        }
    }

    try {
        $connectionProbe = 'ConnectionString="' + 'Server=db;User Id=prod;Pass' + 'word=realistic-secret;"'
        Assert-NoLikelySecrets $connectionProbe
        throw "DeepSeek review gate self-test failed; known connection string pattern was not detected."
    }
    catch {
        if ($_.Exception.Message -notlike 'Potential secret material detected*') {
            throw
        }
    }

    Write-Output "DeepSeek review gate self-test passed."
    Complete-ReviewGate 0
}

$statusLines = Invoke-GitLines @('status', '--porcelain')
$changedFiles = New-Object System.Collections.Generic.HashSet[string]
$untrackedFiles = New-Object System.Collections.Generic.HashSet[string]
$trackedPaths = @(Invoke-GitLines @('diff', '--name-only')) + @(Invoke-GitLines @('diff', '--cached', '--name-only'))
foreach ($path in $trackedPaths) {
    $normalizedPath = ([string]$path).Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($normalizedPath) -or
        $normalizedPath.StartsWith('build/deepseek-review/')) {
        continue
    }

    [void]$changedFiles.Add(([string]$path).Trim())
}

if ($IncludeUntracked) {
    foreach ($path in @(Invoke-GitLines @('ls-files', '--others', '--exclude-standard'))) {
        $normalizedPath = ([string]$path).Replace('\', '/')
        if ([string]::IsNullOrWhiteSpace($normalizedPath) -or
            $normalizedPath.StartsWith('build/deepseek-review/')) {
            continue
        }

        [void]$changedFiles.Add($path.Trim())
        [void]$untrackedFiles.Add($path.Trim())
    }
}

if ($changedFiles.Count -eq 0) {
    Write-Output "No tracked code/documentation changes found for DeepSeek review."
    Complete-ReviewGate 0
}

if ($WhatIfPreference) {
    Write-Output "WhatIf requested; no review packet was written and no DeepSeek API call was made."
    Complete-ReviewGate 0
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputRootCandidate = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $repoRoot $OutputDirectory }
$resolvedOutputRoot = [IO.Path]::GetFullPath($outputRootCandidate)
$resolvedRepoRoot = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\', '/')
$repoRootWithSeparator = $resolvedRepoRoot + [IO.Path]::DirectorySeparatorChar
if ($resolvedOutputRoot.Equals($resolvedRepoRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not $resolvedOutputRoot.StartsWith($repoRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must resolve under the repository root."
}

$relativeOutputRoot = $resolvedOutputRoot.Substring($repoRootWithSeparator.Length).Replace('\', '/')
$ignoreProbePath = ($relativeOutputRoot.TrimEnd('/') + '/.deepseek-review-ignore-probe').Replace('\', '/')
Assert-GitIgnored $ignoreProbePath "DeepSeek review output directory is not ignored by git. Add build/deepseek-review/ to .gitignore before continuing."

$outputRoot = $resolvedOutputRoot
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$script:OutputRootForAudit = $resolvedOutputRoot
Get-ChildItem -LiteralPath $outputRoot -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { -not $_.PSIsContainer -and $_.LastWriteTime -lt (Get-Date).AddDays(-1 * [Math]::Max(1, $CleanupOlderThanDays)) } |
    Remove-Item -Force -ErrorAction SilentlyContinue

$packetPath = Join-Path $outputRoot "deepseek-review-packet-$timestamp.txt"
$responsePath = Join-Path $outputRoot "deepseek-review-$timestamp.md"

$sections = New-Object System.Collections.Generic.List[string]
$sections.Add("# Mandatory DeepSeek code-review packet")
$sections.Add("Review the uncommitted changes in this repository before commit/push and before local or VM validation. Focus on correctness, regressions, security/privacy, reliability, UI behavior, test adequacy, and maintainability. Return findings first, ordered by severity, with exact file paths. If there are no actionable findings, say so explicitly.")
$sections.Add("# Git status")
$sections.Add(($statusLines | Out-String))
$sections.Add("# Unstaged diff")
$sections.Add(((Invoke-GitLines @('diff', '--no-ext-diff', '--unified=80')) -join "`n"))
$sections.Add("# Staged diff")
$sections.Add(((Invoke-GitLines @('diff', '--cached', '--no-ext-diff', '--unified=80')) -join "`n"))

foreach ($file in ($untrackedFiles | Sort-Object)) {
    $literalPath = Join-Path $repoRoot $file
    if (Test-SecretLikePath $file) {
        continue
    }

    if (-not (Test-Path -LiteralPath $literalPath) -or -not (Test-ProbablyTextFile $literalPath)) {
        continue
    }

    $content = Get-Content -Raw -LiteralPath $literalPath
    if ($content.Length -gt $MaxFileCharacters) {
        $truncatedCharacters = $content.Length - $MaxFileCharacters
        $content = $content.Substring(0, $MaxFileCharacters) + "`n...[truncated by Run-DeepSeekCodeReview.ps1; omitted $truncatedCharacters characters]..."
    }

    $sections.Add("# Untracked file: $file")
    $sections.Add($content)
}

$packet = $sections -join "`n`n"
if ($packet.Length -gt $MaxPacketCharacters) {
    throw "DeepSeek review packet is $($packet.Length) characters, exceeding MaxPacketCharacters=$MaxPacketCharacters. Split the change into smaller reviewable units or rerun with an explicit larger -MaxPacketCharacters value."
}

Assert-NoLikelySecrets $packet
Write-Warning "Writing local DeepSeek review packet to $packetPath. If it contains sensitive material, delete it immediately and do not use -SendForReview."
Set-Content -LiteralPath $packetPath -Value $packet -Encoding UTF8
$relativePacketPath = (Resolve-Path -LiteralPath $packetPath -Relative).TrimStart('.', '\', '/')
Assert-GitIgnored $relativePacketPath "DeepSeek review packet is not ignored by git: $relativePacketPath. Fix .gitignore before continuing."

if ($PacketOnly -or -not $SendForReview) {
    Write-Output "DEEPSEEK_REVIEW_PACKET=$packetPath"
    Write-Output "Packet-only mode; no DeepSeek API call was made. Rerun with -SendForReview to transmit the packet."
    Complete-ReviewGate 0
}

if (-not $AcknowledgeSecretScan) {
    throw "Before using -SendForReview, inspect/redact the generated packet and rerun with -AcknowledgeSecretScan to confirm no secrets or local-only credentials are being sent externally."
}

$apiKey = Get-DeepSeekApiKey $repoRoot
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    Write-Output "DEEPSEEK_REVIEW_PACKET=$packetPath"
    Write-Output "DeepSeek review skipped by explicit missing-key waiver."
    Complete-ReviewGate 0
}

$body = @{
    model = $Model
    messages = @(
        @{
            role = 'system'
            content = 'You are a senior principal engineer doing a mandatory pre-commit code review. Be adversarial but fair. Prioritize concrete bugs, regressions, security/privacy/legal risks, missing tests, and maintainability hazards. Avoid generic praise.'
        },
        @{
            role = 'user'
            content = $packet
        }
    )
    temperature = 0.1
    max_tokens = $MaxTokens
} | ConvertTo-Json -Depth 8

$Endpoint = $Endpoint.TrimEnd('/')
if ($SendForReview -and -not $Endpoint.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase)) {
    throw "DeepSeek review endpoint must use HTTPS when -SendForReview is used."
}

$trustedDefaultEndpoint = 'https://api.deepseek.com'
if ($SendForReview -and
    -not $Endpoint.Equals($trustedDefaultEndpoint, [StringComparison]::OrdinalIgnoreCase) -and
    -not $AcknowledgeEndpointOverride) {
    throw "DeepSeek review endpoint '$Endpoint' differs from the trusted default '$trustedDefaultEndpoint'. Rerun with -AcknowledgeEndpointOverride only if this destination is intentional."
}

$requestBytes = [Text.Encoding]::UTF8.GetByteCount($body)
if ($requestBytes -gt $MaxRequestBytes) {
    throw "DeepSeek review request body is $requestBytes bytes, exceeding MaxRequestBytes=$MaxRequestBytes. Split the change into smaller reviewable units or rerun with an explicit larger -MaxRequestBytes value."
}

try {
    if (-not $PSCmdlet.ShouldProcess("DeepSeek $Model at $Endpoint", "Send mandatory code-review packet")) {
        Write-Output "DEEPSEEK_REVIEW_PACKET=$packetPath"
        Write-Output "WhatIf requested; no DeepSeek API call was made."
        Complete-ReviewGate 0
    }

    Write-SendAudit $packetPath $Endpoint $Model
    Write-Warning "Sending code-review packet to external DeepSeek-compatible endpoint: $Endpoint"
    $retryDelaysSeconds = @(5, 10, 20)
    $response = $null
    for ($attempt = 1; $attempt -le ($retryDelaysSeconds.Count + 1); $attempt++) {
        try {
            $response = Invoke-RestMethod `
                -Method Post `
                -Uri "$Endpoint/chat/completions" `
                -Headers @{ Authorization = "Bearer $apiKey"; 'Content-Type' = 'application/json' } `
                -Body $body `
                -TimeoutSec 180
            break
        }
        catch {
            $status = $null
            if ($null -ne $_.Exception.Response -and $null -ne $_.Exception.Response.StatusCode) {
                $status = [int]$_.Exception.Response.StatusCode
            }

            $isTransient = $null -eq $status -or $status -eq 408 -or $status -eq 425 -or $status -eq 429 -or $status -ge 500
            if (-not $isTransient -or $attempt -gt $retryDelaysSeconds.Count) {
                throw
            }

            $delay = $retryDelaysSeconds[$attempt - 1]
            Write-Warning "DeepSeek review request attempt $attempt failed with transient HTTP status $status; retrying in $delay seconds."
            Start-Sleep -Seconds $delay
        }
    }
}
catch {
    $status = $null
    if ($null -ne $_.Exception.Response -and $null -ne $_.Exception.Response.StatusCode) {
        $status = [int]$_.Exception.Response.StatusCode
    }

    $statusText = if ($null -eq $status) { 'unavailable' } else { [string]$status }
    throw "DeepSeek review request failed. HTTP status: $statusText. Verify endpoint, model, API key, and network connectivity; raw response details are intentionally redacted."
}

if ($null -eq $response.choices -or
    $response.choices.Count -lt 1 -or
    $null -eq $response.choices[0].message -or
    [string]::IsNullOrWhiteSpace([string]$response.choices[0].message.content)) {
    if ($response.PSObject.Properties.Name -contains 'error') {
        $errorJson = $response.error | ConvertTo-Json -Depth 8 -Compress
        throw "DeepSeek review response returned an error body: $errorJson"
    }

    throw "DeepSeek review response did not contain choices[0].message.content."
}

if ($response.choices[0].PSObject.Properties.Name -contains 'finish_reason') {
    $finishReason = [string]$response.choices[0].finish_reason
    if ([string]::Equals($finishReason, 'length', [StringComparison]::OrdinalIgnoreCase)) {
        throw "DeepSeek review response was truncated because the model hit the max token limit. Increase -MaxTokens and rerun the review."
    }
}

$content = [string]$response.choices[0].message.content
if ($content.Length -gt $MaxResponseCharacters) {
    $content = $content.Substring(0, $MaxResponseCharacters) + "`n...[truncated by Run-DeepSeekCodeReview.ps1 because response exceeded MaxResponseCharacters=$MaxResponseCharacters]..."
}

Set-Content -LiteralPath $responsePath -Value $content -Encoding UTF8

Write-Output "DEEPSEEK_REVIEW_PACKET=$packetPath"
Write-Output "DEEPSEEK_REVIEW_RESPONSE=$responsePath"
Write-Output "---DEEPSEEK REVIEW---"
Write-Output $content
