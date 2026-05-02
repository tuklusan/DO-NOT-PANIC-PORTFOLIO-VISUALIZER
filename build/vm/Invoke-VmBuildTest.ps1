param(
    [string]$VmHost = '192.168.56.102',
    [int]$VmPort = 22,
    [string]$RootPath = 'C:\vmharness\portfolio-saver',
    [switch]$Bootstrap,
    [switch]$PushWorkspace,
    [switch]$RunUxDeep,
    [ValidateRange(1, 180)]
    [int]$GuestScreensaverDurationMinutes = 20,
    [ValidateRange(1, 60)]
    [int]$CaptureIntervalSeconds = 5,
    [int]$BuildTimeoutSeconds = 3600,
    [int]$UxTimeoutSeconds = 2400
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'VmSshCommon.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostArtifactsRoot = Join-Path $repoRoot 'build\vm\artifacts\ssh-runs'
$bundle = $null
$uxResultName = 'ux-deep-ssh-' + (Get-Date -Format 'yyyyMMdd-HHmmss')

if ($PushWorkspace) {
    & (Join-Path $PSScriptRoot 'Push-VmWorkspace.ps1') -VmHost $VmHost -VmPort $VmPort -RootPath $RootPath -Bootstrap:$Bootstrap
}

try {
    New-Item -ItemType Directory -Force -Path $hostArtifactsRoot | Out-Null
    $bundle = New-VmSshSessionBundle -HostName $VmHost -Port $VmPort

    if ($Bootstrap -and -not $PushWorkspace) {
        $bootstrapCommand = @"
& '$(Join-Path $RootPath 'scripts\Guest-BootstrapVmRemoteTools.ps1')' -RootPath '$RootPath'
"@
        Invoke-VmPwshCommand -Bundle $bundle -Command $bootstrapCommand -TimeOutSeconds 1800 | Out-Null
    }

    $buildStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $remoteBuildSummary = Join-Path $RootPath ("results\buildtest-$buildStamp.json")
    $buildCommand = @"
`$repoRoot = Join-Path '$RootPath' 'repo'
`$resultPath = '$remoteBuildSummary'
`$publishRoot = Join-Path `$repoRoot 'build\artifacts\publish-safe-temp'
`$stagedPublish = Join-Path '$RootPath' 'publish'
`$summary = [ordered]@{
    StartedAt = (Get-Date).ToString('o')
    RepoRoot = `$repoRoot
    Result = 'Pending'
}
Push-Location `$repoRoot
try {
    & dotnet restore .\PortfolioScreensaver.sln --disable-parallel --nologo
    if (`$LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    & dotnet build .\PortfolioScreensaver.sln -c Release --nologo --no-restore
    if (`$LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

    & dotnet test .\tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --nologo --no-build
    if (`$LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }

    & .\build\publish-safe-temp.ps1 -Configuration Release -TimeoutSeconds 900

    if (Test-Path `$stagedPublish) {
        Remove-Item -LiteralPath `$stagedPublish -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Force -Path `$stagedPublish | Out-Null
    foreach (`$item in Get-ChildItem -LiteralPath `$publishRoot -Force) {
        Copy-Item -LiteralPath `$item.FullName -Destination (Join-Path `$stagedPublish `$item.Name) -Recurse -Force
    }

    `$summary.Result = 'Passed'
}
catch {
    `$summary.Result = 'Failed'
    `$summary.Error = `$_.Exception.Message
    throw
}
finally {
    Pop-Location
    `$summary.FinishedAt = (Get-Date).ToString('o')
    `$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath `$resultPath -Encoding UTF8
}
Write-Output ('BUILD_SUMMARY=' + `$resultPath)
"@

    Write-VmSshStep "Running remote restore/build/test/publish"
    $buildOutput = Invoke-VmPwshCommand -Bundle $bundle -Command $buildCommand -TimeOutSeconds $BuildTimeoutSeconds
    $buildOutput.Output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { Write-Host $_ }

    if ($RunUxDeep) {
        $remoteUxScript = Join-Path $RootPath 'repo\build\vm\Guest-UxDeepExercise.ps1'
        $remoteUxSummary = Join-Path $RootPath ("results\$uxResultName\ux-deep-summary.json")
        $launchCommand = @"
`$psexec = 'C:\Program Files\SysinternalsSuite\PsExec.exe'
if (-not (Test-Path `$psexec)) {
    throw 'PsExec.exe is required for interactive desktop launch but was not found.'
}
`$scriptPath = '$remoteUxScript'
if (-not (Test-Path `$scriptPath)) {
    throw \"Missing guest UX script: $remoteUxScript\"
}
Start-Process -FilePath `$psexec -ArgumentList @(
    '-accepteula',
    '-i', '1',
    '-d',
    'pwsh.exe',
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', `$scriptPath,
    '-RootPath', '$RootPath',
    '-ResultName', '$uxResultName',
    '-ScreensaverDurationMinutes', '$GuestScreensaverDurationMinutes',
    '-CaptureIntervalSeconds', '$CaptureIntervalSeconds'
) | Out-Null
Write-Output 'UX_LAUNCHED'
"@
        Write-VmSshStep "Launching remote 20-minute UX run through PsExec"
        Invoke-VmPwshCommand -Bundle $bundle -Command $launchCommand -TimeOutSeconds 120 | Out-Null

        $deadline = (Get-Date).AddSeconds($UxTimeoutSeconds)
        do {
            Start-Sleep -Seconds 15
            $pollCommand = @"
if (Test-Path '$remoteUxSummary') {
    Get-Content -LiteralPath '$remoteUxSummary' -Raw
}
"@
            $poll = Invoke-VmPwshCommand -Bundle $bundle -Command $pollCommand -TimeOutSeconds 120
            $json = ($poll.Output -join [Environment]::NewLine).Trim()
            if (-not [string]::IsNullOrWhiteSpace($json)) {
                $summary = $json | ConvertFrom-Json
                if ($null -ne $summary.FinishedAt -and -not [string]::IsNullOrWhiteSpace([string]$summary.FinishedAt)) {
                    Write-VmSshStep "Remote UX run finished"
                    break
                }
            }
        } while ((Get-Date) -lt $deadline)

        if ((Get-Date) -ge $deadline) {
            throw "Timed out waiting for remote UX summary: $remoteUxSummary"
        }

        & (Join-Path $PSScriptRoot 'Pull-VmResults.ps1') -VmHost $VmHost -VmPort $VmPort -RootPath $RootPath -RemotePath (Join-Path $RootPath ("results\$uxResultName"))
    }
}
finally {
    if ($null -ne $bundle) {
        Remove-VmSshSessionBundle -Bundle $bundle
    }
}
