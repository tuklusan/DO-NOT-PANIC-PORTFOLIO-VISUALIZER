$ErrorActionPreference = 'Continue'

$outPath = 'C:\Temp\vm-tool-verify.txt'
New-Item -ItemType Directory -Path 'C:\Temp' -Force | Out-Null
Set-Content -LiteralPath $outPath -Value "VM Tool Verify - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

function Add-Line {
    param([string]$Text)
    Write-Host $Text
    Add-Content -LiteralPath $outPath -Value $Text
}

function Add-Section {
    param([string]$Title)
    Add-Line ""
    Add-Line ("=" * 80)
    Add-Line $Title
    Add-Line ("=" * 80)
}

Add-Section "Command Versions"
$checks = @(
    @{ Name = 'choco'; Args = '--version' },
    @{ Name = 'scoop'; Args = '--version' },
    @{ Name = 'git'; Args = '--version' },
    @{ Name = 'python'; Args = '--version' },
    @{ Name = 'py'; Args = '--version' },
    @{ Name = 'node'; Args = '--version' },
    @{ Name = 'npm'; Args = '--version' },
    @{ Name = 'appium'; Args = '--version' }
)
foreach ($c in $checks) {
    $cmd = Get-Command $c.Name -ErrorAction SilentlyContinue
    if ($cmd) {
        $value = & $c.Name $c.Args 2>&1
        Add-Line ("{0,-12} {1}" -f $c.Name, ($value | Select-Object -First 1))
    } else {
        Add-Line ("{0,-12} NOT FOUND" -f $c.Name)
    }
}

Add-Section "Choco Packages (selected)"
$selected = @('git', 'python', '7zip', 'jq', 'ripgrep', 'sysinternals', 'autohotkey.portable', 'nssm', 'nodejs-lts', 'winappdriver')
$choco = Get-Command choco -ErrorAction SilentlyContinue
if ($choco) {
    foreach ($pkg in $selected) {
        $line = choco list --local-only --limit-output $pkg 2>$null | Select-Object -First 1
        if ($line) { Add-Line $line } else { Add-Line "$pkg|NOT_INSTALLED" }
    }
} else {
    Add-Line "choco not present."
}

Add-Section "Python Modules"
$py = Get-Command py -ErrorAction SilentlyContinue
if ($py) {
    $mods = @('pywinauto', 'pywin32', 'pyautogui', 'pytest', 'requests', 'lxml', 'pytweening')
    foreach ($m in $mods) {
        $info = py -m pip show $m 2>$null | Select-String '^Version:' | Select-Object -First 1
        if ($info) {
            Add-Line ("{0,-12} {1}" -f $m, ($info.ToString().Replace('Version:', '').Trim()))
        } else {
            Add-Line ("{0,-12} NOT_INSTALLED" -f $m)
        }
    }
}

Add-Section "Key Paths"
$paths = @(
    'C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe',
    'C:\Program Files\AutoHotkey\AutoHotkey.exe',
    'C:\ProgramData\chocolatey\lib\autohotkey.portable',
    'C:\Program Files\SysinternalsSuite',
    'C:\Program Files\sysinternals',
    'C:\ProgramData\chocolatey\lib\sysinternals'
)
foreach ($p in $paths) {
    if (Test-Path -LiteralPath $p) { Add-Line "FOUND $p" } else { Add-Line "MISSING $p" }
}

Add-Section "File Search Hints"
$roots = @('C:\Program Files', 'C:\Program Files (x86)', $env:USERPROFILE)
$patterns = @('windriver', 'winappdriver', 'pywin', 'autohotkey', 'appium')
foreach ($root in $roots) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    Add-Line "-- $root"
    $files = Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue
    foreach ($pat in $patterns) {
        $hits = $files | Where-Object { $_.Name -match $pat } | Select-Object -First 5 -ExpandProperty FullName
        if ($hits) {
            Add-Line "Pattern [$pat]"
            $hits | ForEach-Object { Add-Line $_ }
        }
    }
}

Add-Line ""
Add-Line "Verification saved to $outPath"
