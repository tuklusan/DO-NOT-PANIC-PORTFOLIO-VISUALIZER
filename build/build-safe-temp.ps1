param(
    [string]$Configuration = "Release",
    [int]$TimeoutSeconds = 300,
    [switch]$CopyOutputsBack = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-ProcessWithTimeout {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $Arguments
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $startInfo
    $stdoutLines = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
    $stderrLines = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
    $stdoutComplete = [System.Threading.ManualResetEventSlim]::new($false)
    $stderrComplete = [System.Threading.ManualResetEventSlim]::new($false)
    $started = $false
    $proc.add_OutputDataReceived({
        if ($null -eq $EventArgs.Data) {
            $stdoutComplete.Set()
        }
        else {
            $stdoutLines.Enqueue($EventArgs.Data)
        }
    })
    $proc.add_ErrorDataReceived({
        if ($null -eq $EventArgs.Data) {
            $stderrComplete.Set()
        }
        else {
            $stderrLines.Enqueue($EventArgs.Data)
        }
    })

    try {
        $null = $proc.Start()
        $started = $true
        $proc.BeginOutputReadLine()
        $proc.BeginErrorReadLine()

        if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
            try { $proc.Kill($true) } catch {}
            throw "Command timed out after $TimeoutSeconds seconds: $FilePath $Arguments"
        }

        $proc.WaitForExit()
    }
    finally {
        if ($null -ne $proc -and $started) {
            try {
                if (-not $proc.HasExited) {
                    try { $proc.CancelOutputRead() } catch {}
                    try { $proc.CancelErrorRead() } catch {}
                    $proc.Kill($true)
                }
            } catch {}
        }
    }
    [void]$stdoutComplete.Wait([TimeSpan]::FromSeconds(5))
    [void]$stderrComplete.Wait([TimeSpan]::FromSeconds(5))
    $stdout = [string]::Join([Environment]::NewLine, $stdoutLines.ToArray())
    $stderr = [string]::Join([Environment]::NewLine, $stderrLines.ToArray())
    if (-not [string]::IsNullOrWhiteSpace($stdout)) { Write-Host $stdout.TrimEnd() }
    if (-not [string]::IsNullOrWhiteSpace($stderr)) { Write-Host $stderr.TrimEnd() }
    if ($proc.ExitCode -ne 0) {
        throw "Command failed with exit code $($proc.ExitCode): $FilePath $Arguments"
    }
}

function Resolve-DotNetCli {
    $preferred = Join-Path $env:USERPROFILE ".dotnet10\dotnet.exe"
    if (Test-Path $preferred) {
        return $preferred
    }

    return "dotnet"
}

function Get-RelativePathLegacy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $normalizedBase = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $baseUri = New-Object System.Uri($normalizedBase)
    $targetUri = New-Object System.Uri([System.IO.Path]::GetFullPath($TargetPath))
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$tempRoot = Join-Path $env:TEMP "PortfolioSaverBuildWorkspace"
$dotnetCli = Resolve-DotNetCli

Write-Host "Preparing temp workspace: $tempRoot"
if (Test-Path $tempRoot) {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

Copy-Item -LiteralPath (Join-Path $repoRoot "PortfolioScreensaver.sln") -Destination $tempRoot -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "Directory.Build.props") -Destination $tempRoot -Force
if (Test-Path (Join-Path $repoRoot "NuGet.Config")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "NuGet.Config") -Destination $tempRoot -Force
}

$srcTarget = Join-Path $tempRoot "src"
$testsTarget = Join-Path $tempRoot "tests"
$null = robocopy (Join-Path $repoRoot "src") $srcTarget /E /XD bin obj
$srcCopyExit = $LASTEXITCODE
$null = robocopy (Join-Path $repoRoot "tests") $testsTarget /E /XD bin obj
$testsCopyExit = $LASTEXITCODE
if ($srcCopyExit -gt 7 -or $testsCopyExit -gt 7) {
    throw "Workspace mirror failed. robocopy exits: src=$srcCopyExit tests=$testsCopyExit"
}

Write-Host "Seeding NuGet restore assets from local obj caches..."
$assetPatterns = @(
    "project.assets.json",
    "project.nuget.cache",
    "*.nuget.dgspec.json",
    "*.nuget.g.props",
    "*.nuget.g.targets"
)

$assetFiles = Get-ChildItem -Path (Join-Path $repoRoot "src"),(Join-Path $repoRoot "tests") -Recurse -File | Where-Object {
    $name = $_.Name
    foreach ($pattern in $assetPatterns) {
        if ($name -like $pattern) { return $true }
    }
    return $false
}

foreach ($assetFile in $assetFiles) {
    $relativePath = Get-RelativePathLegacy -BasePath $repoRoot -TargetPath $assetFile.FullName
    $destinationPath = Join-Path $tempRoot $relativePath
    $destinationDir = Split-Path -Path $destinationPath -Parent
    New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null
    Copy-Item -LiteralPath $assetFile.FullName -Destination $destinationPath -Force
}

Write-Host "Building solution in temp workspace (timeout=$TimeoutSeconds sec)..."
Invoke-ProcessWithTimeout `
    -FilePath $dotnetCli `
    -Arguments "build .\PortfolioScreensaver.sln -c $Configuration --no-restore -m:1 -nodeReuse:false -v minimal" `
    -WorkingDirectory $tempRoot `
    -TimeoutSeconds $TimeoutSeconds

if ($CopyOutputsBack) {
    Write-Host "Copying build outputs back to repo..."
    foreach ($dirName in @("src", "tests")) {
        $fromRoot = Join-Path $tempRoot $dirName
        if (-not (Test-Path $fromRoot)) { continue }

        $binDirs = Get-ChildItem -Path $fromRoot -Recurse -Directory -Filter "bin" -ErrorAction SilentlyContinue
        foreach ($binDir in $binDirs) {
            $relativeBinPath = Get-RelativePathLegacy -BasePath $tempRoot -TargetPath $binDir.FullName
            $targetBinDir = Join-Path $repoRoot $relativeBinPath
            New-Item -ItemType Directory -Force -Path $targetBinDir | Out-Null
            $null = robocopy $binDir.FullName $targetBinDir /E
            $copyBackExit = $LASTEXITCODE
            if ($copyBackExit -gt 7) {
                throw "Copy-back failed for $($binDir.FullName) -> $targetBinDir (robocopy exit=$copyBackExit)"
            }
        }
    }
}

Write-Host "SAFE_TEMP_BUILD_OK workspace=$tempRoot configuration=$Configuration"
