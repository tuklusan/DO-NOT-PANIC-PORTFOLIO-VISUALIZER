param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,
    [string]$ProgramName = "DO NOT PANIC PORTFOLIO VISUALIZER"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $normalizedBase = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $baseUri = New-Object System.Uri($normalizedBase)
    $targetUri = New-Object System.Uri([System.IO.Path]::GetFullPath($TargetPath))
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('\', '/')
}

$resolvedPublishDir = (Resolve-Path -LiteralPath $PublishDir).Path
if (-not (Test-Path -LiteralPath $resolvedPublishDir -PathType Container)) {
    throw "Publish directory not found: $PublishDir"
}

$manifestPath = Join-Path $resolvedPublishDir "release-manifest.json"
$files = Get-ChildItem -LiteralPath $resolvedPublishDir -Recurse -File | Where-Object {
    $_.FullName -ne $manifestPath
}

$primaryExe = Get-ChildItem -LiteralPath $resolvedPublishDir -File -Filter "*.exe" | Select-Object -First 1
$productVersion = if ($null -ne $primaryExe) { $primaryExe.VersionInfo.ProductVersion } else { "unknown" }

$entries = foreach ($file in ($files | Sort-Object FullName)) {
    $relativePath = Get-RelativePath -BasePath $resolvedPublishDir -TargetPath $file.FullName
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToLowerInvariant()
    [ordered]@{
        path = $relativePath
        sizeBytes = [int64]$file.Length
        sha256 = $hash
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    productName = $ProgramName
    productVersion = $productVersion
    generatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    files = @($entries)
}

$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Output ("Generated manifest: {0} ({1} files, version {2})" -f $manifestPath, @($entries).Count, $productVersion)
