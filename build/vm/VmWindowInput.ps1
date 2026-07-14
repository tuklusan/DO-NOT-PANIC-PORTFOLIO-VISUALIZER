# ============================================================================
# Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
# Proprietary rights reserved except as expressly licensed herein.
#
# DO NOT PANIC PORTFOLIO VISUALIZER
# This file is governed by the SANYALnet Labs Non-Commercial License in the
# root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
# for AI/ML model training are prohibited unless separately authorized.
#
# Attribution is required: "Based on original work by Supratim Sanyal of
# SANYALnet Labs." See LICENSE for full terms, warranty disclaimer,
# termination, patent, trademark, and governing-law provisions.
# ============================================================================

Set-StrictMode -Version Latest

if (-not ('NativeWindowMessaging' -as [type])) {
    try {
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeWindowMessaging {
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_F11 = 0x7A;
    public const uint MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);
}
"@
    }
    catch {
        if (-not ('NativeWindowMessaging' -as [type])) {
            throw
        }
    }
}

function New-KeyboardMessageLParam {
    param(
        [Parameter(Mandatory = $true)][int]$VirtualKey,
        [switch]$KeyUp
    )

    $scanCode = [NativeWindowMessaging]::MapVirtualKey([uint32]$VirtualKey, [NativeWindowMessaging]::MAPVK_VK_TO_VSC)
    # Win32 keyboard-message repeat count is one for a single synthetic keystroke.
    $value = [uint32](1 -bor ([uint32]$scanCode -shl 16))
    if (Test-IsExtendedVirtualKey -VirtualKey $VirtualKey) {
        $value = [uint32]($value -bor ([uint32]1 -shl 24))
    }

    if ($KeyUp) {
        $value = [uint32]($value -bor ([uint32]1 -shl 30) -bor ([uint32]1 -shl 31))
    }

    return [IntPtr]([int64][uint32]$value)
}

function Test-IsExtendedVirtualKey {
    param([Parameter(Mandatory = $true)][int]$VirtualKey)

    return $VirtualKey -in @(
        0x21, # VK_PRIOR
        0x22, # VK_NEXT
        0x23, # VK_END
        0x24, # VK_HOME
        0x25, # VK_LEFT
        0x26, # VK_UP
        0x27, # VK_RIGHT
        0x28, # VK_DOWN
        0x2D, # VK_INSERT
        0x2E, # VK_DELETE
        0x5B, # VK_LWIN
        0x5C, # VK_RWIN
        0x5D, # VK_APPS
        0x6F, # VK_DIVIDE
        0x90, # VK_NUMLOCK
        0x91, # VK_SCROLL
        0xA3, # VK_RCONTROL
        0xA5  # VK_RMENU
    )
}

function Throw-ProcessWindowKeyFailure {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][int]$VirtualKey,
        [Parameter(Mandatory = $true)][string]$Reason
    )

    throw ("Failed to send virtual key 0x{0:X2} to process {1} ({2}): {3}" -f $VirtualKey, $Process.Id, $Process.ProcessName, $Reason)
}

function Send-ProcessWindowKey {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNull()]
        [System.Diagnostics.Process]$Process,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 255)]
        [int]$VirtualKey
    )

    if ($Process.HasExited) {
        Throw-ProcessWindowKeyFailure -Process $Process -VirtualKey $VirtualKey -Reason 'process has exited'
    }

    $Process.Refresh()
    if ($Process.MainWindowHandle -eq [IntPtr]::Zero) {
        Throw-ProcessWindowKeyFailure -Process $Process -VirtualKey $VirtualKey -Reason 'main window handle is zero'
    }

    $handle = [IntPtr]$Process.MainWindowHandle
    if (-not [NativeWindowMessaging]::IsWindow($handle)) {
        Throw-ProcessWindowKeyFailure -Process $Process -VirtualKey $VirtualKey -Reason 'main window handle is not a valid window'
    }

    $downLParam = New-KeyboardMessageLParam -VirtualKey $VirtualKey
    $upLParam = New-KeyboardMessageLParam -VirtualKey $VirtualKey -KeyUp
    if (-not [NativeWindowMessaging]::IsWindow($handle)) {
        Throw-ProcessWindowKeyFailure -Process $Process -VirtualKey $VirtualKey -Reason 'window became invalid before key down'
    }

    if (-not [NativeWindowMessaging]::PostMessage($handle, [NativeWindowMessaging]::WM_KEYDOWN, [IntPtr]$VirtualKey, $downLParam)) {
        Throw-ProcessWindowKeyFailure -Process $Process -VirtualKey $VirtualKey -Reason 'WM_KEYDOWN PostMessage failed'
    }

    Start-Sleep -Milliseconds 75
    if (-not [NativeWindowMessaging]::IsWindow($handle)) {
        Throw-ProcessWindowKeyFailure -Process $Process -VirtualKey $VirtualKey -Reason 'window became invalid before key up'
    }

    if (-not [NativeWindowMessaging]::PostMessage($handle, [NativeWindowMessaging]::WM_KEYUP, [IntPtr]$VirtualKey, $upLParam)) {
        Throw-ProcessWindowKeyFailure -Process $Process -VirtualKey $VirtualKey -Reason 'WM_KEYUP PostMessage failed'
    }
}
