Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoShare = "\\VBOXSVR\codexrepo"
$desktopRoot = Join-Path $env:USERPROFILE "Desktop\PortfolioVmUx"
$captureStamp = Get-Date -Format "yyyy-MM-dd"
$captureRoot = Join-Path $desktopRoot ("tool-inventory\" + $captureStamp)
New-Item -ItemType Directory -Force -Path $captureRoot | Out-Null

function Get-ToolProbe {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string[]]$Candidates
    )

    foreach ($candidate in $Candidates) {
        try {
            $cmd = Get-Command $candidate -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($null -ne $cmd) {
                return [pscustomobject]@{
                    Tool = $Name
                    Found = $true
                    Candidate = $candidate
                    Source = $cmd.Source
                    Version = ($cmd.Version | Out-String).Trim()
                }
            }
        }
        catch {
        }
    }

    return [pscustomobject]@{
        Tool = $Name
        Found = $false
        Candidate = ""
        Source = ""
        Version = ""
    }
}

Write-Output "TOOLSCAN_BEGIN"

$pathCommands = Get-Command -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object Name, Source |
    Sort-Object Name -Unique
$pathCommands | Export-Csv -NoTypeInformation -Encoding UTF8 -Path (Join-Path $captureRoot "commands-in-path.csv")

$toolProbes = @(
    (Get-ToolProbe -Name "PowerShell" -Candidates @("powershell.exe")),
    (Get-ToolProbe -Name "Git" -Candidates @("git.exe", "git")),
    (Get-ToolProbe -Name "winget" -Candidates @("winget.exe", "winget")),
    (Get-ToolProbe -Name "Python" -Candidates @("python.exe", "python")),
    (Get-ToolProbe -Name "Node" -Candidates @("node.exe", "node")),
    (Get-ToolProbe -Name "npm" -Candidates @("npm.cmd", "npm")),
    (Get-ToolProbe -Name "7-Zip" -Candidates @("7z.exe")),
    (Get-ToolProbe -Name "VBoxControl" -Candidates @("VBoxControl.exe")),
    (Get-ToolProbe -Name "WinAppDriver" -Candidates @("WinAppDriver.exe")),
    (Get-ToolProbe -Name "WinDriver" -Candidates @("WinDriver.exe"))
)
$toolProbes | Export-Csv -NoTypeInformation -Encoding UTF8 -Path (Join-Path $captureRoot "tool-probes.csv")

$programFilesRoots = @(
    "C:\Program Files",
    "C:\Program Files (x86)"
) | Where-Object { Test-Path $_ }

$summaryRows = @()
foreach ($root in $programFilesRoots) {
    $allItems = Get-ChildItem -LiteralPath $root -Force -Recurse -ErrorAction SilentlyContinue
    $executables = $allItems | Where-Object {
        -not $_.PSIsContainer -and $_.Extension -in @(".exe", ".cmd", ".bat", ".ps1", ".psm1", ".msc", ".com")
    }

    $rootSlug = ($root -replace "[:\\ ]", "_").Trim("_")
    $allItems | Select-Object FullName, Name, Length, LastWriteTime |
        Export-Csv -NoTypeInformation -Encoding UTF8 -Path (Join-Path $captureRoot ("{0}-all-items.csv" -f $rootSlug))
    $executables | Select-Object FullName, Name, Extension, Length, LastWriteTime |
        Export-Csv -NoTypeInformation -Encoding UTF8 -Path (Join-Path $captureRoot ("{0}-executables-and-scripts.csv" -f $rootSlug))

    $summaryRows += [pscustomobject]@{
        Root = $root
        ItemCount = @($allItems).Count
        ExecutableOrScriptCount = @($executables).Count
    }
}
$summaryRows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path (Join-Path $captureRoot "program-files-scan-summary.csv")

$installedSoftware = @()
$uninstallPaths = @(
    "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
    "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*"
)
foreach ($path in $uninstallPaths) {
    $installedSoftware += Get-ItemProperty -Path $path -ErrorAction SilentlyContinue |
        Where-Object {
            $_.PSObject.Properties.Name -contains "DisplayName" -and
            -not [string]::IsNullOrWhiteSpace([string]$_.DisplayName)
        } |
        Select-Object DisplayName, DisplayVersion, Publisher, InstallLocation
}
$installedSoftware | Sort-Object DisplayName -Unique |
    Export-Csv -NoTypeInformation -Encoding UTF8 -Path (Join-Path $captureRoot "installed-software.csv")

$sessionInfo = (query user | Out-String).Trim()
$summary = [ordered]@{
    CapturedAt = (Get-Date).ToString("o")
    CommandCount = @($pathCommands).Count
    ProbeCount = @($toolProbes).Count
    ProbeFoundCount = @($toolProbes | Where-Object { $_.Found }).Count
    InstalledSoftwareCount = @($installedSoftware).Count
    SessionInfo = $sessionInfo
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $captureRoot "summary.json") -Encoding UTF8

$record = @"
# Windows10Pro VM Tool Inventory Record

Captured on: $captureStamp
Guest user/session: $sessionInfo

## Summary

- Commands in PATH discovered: $($summary.CommandCount)
- Tool probes attempted: $($summary.ProbeCount)
- Tool probes found: $($summary.ProbeFoundCount)
- Installed software entries: $($summary.InstalledSoftwareCount)

## Full Record Files

- commands-in-path.csv
- tool-probes.csv
- installed-software.csv
- program-files-scan-summary.csv
- summary.json
"@
$record | Set-Content -LiteralPath (Join-Path $captureRoot "VM_TOOL_RECORD.md") -Encoding UTF8

if (Test-Path $repoShare) {
    $hostRoot = Join-Path $repoShare ("build\vm\tool-inventory\" + $captureStamp)
    New-Item -ItemType Directory -Force -Path $hostRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $captureRoot "*") -Destination $hostRoot -Recurse -Force
    Write-Output ("HOST_TOOL_INVENTORY=" + $hostRoot)
}

Write-Output "TOOLSCAN_DONE"
