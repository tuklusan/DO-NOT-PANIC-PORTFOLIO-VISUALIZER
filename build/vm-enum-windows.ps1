Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Win32Enum
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
"@

$target = Get-Process PortfolioSaver.Screensaver -ErrorAction SilentlyContinue
if (-not $target) {
    Write-Output "NO_PROCESS"
    exit 0
}

$targetPids = @($target | ForEach-Object { [uint32]$_.Id })
$rows = New-Object System.Collections.Generic.List[string]

[Win32Enum]::EnumWindows({
    param($hWnd, $lParam)

    [uint32]$windowPid = 0
    [void][Win32Enum]::GetWindowThreadProcessId($hWnd, [ref]$windowPid)
    if ($targetPids -contains $windowPid) {
        $sb = New-Object System.Text.StringBuilder 512
        [void][Win32Enum]::GetWindowText($hWnd, $sb, $sb.Capacity)
        $visible = [Win32Enum]::IsWindowVisible($hWnd)
        $rows.Add(("PID={0} HWND=0x{1} Visible={2} Title={3}" -f $windowPid, $hWnd.ToString("X"), $visible, $sb.ToString()))
    }

    return $true
}, [IntPtr]::Zero) | Out-Null

if ($rows.Count -eq 0) {
    Write-Output "NO_WINDOWS"
} else {
    $rows
}
