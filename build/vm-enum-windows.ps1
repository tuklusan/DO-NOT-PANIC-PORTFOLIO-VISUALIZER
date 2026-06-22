# ============================================================================
# Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
# Proprietary rights reserved except as expressly licensed herein.
#
# DO NOT PANIC PORTFOLIO VIEWER
# This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
# personal, educational, or hobbyist use only. Commercial exploitation,
# corporate internal operations, or AI model training are strictly forbidden.
#
# ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
# which is licensed under the Apache License, Version 2.0. A copy of the Apache
# License is provided within the distribution environment.
#
# FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
# It does not provide financial, investment, legal, or tax advice. All data
# calculation and scraping outputs are provided 'AS IS' with zero guarantee
# of real-time accuracy or upstream availability.
#
# This file is subject to the terms and conditions defined in the LICENSE
# file located in the root directory of this source code repository.
# Removal or modification of this legal notice constitutes copyright infringement.
# ============================================================================
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
