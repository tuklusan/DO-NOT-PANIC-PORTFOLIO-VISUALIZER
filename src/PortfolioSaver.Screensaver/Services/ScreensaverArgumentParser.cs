// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VIEWER
// This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
// personal, educational, or hobbyist use only. Commercial exploitation,
// corporate internal operations, or AI model training are strictly forbidden.
//
// ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
// which is licensed under the Apache License, Version 2.0. A copy of the Apache
// License is provided within the distribution environment.
//
// FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
// It does not provide financial, investment, legal, or tax advice. All data
// calculation and scraping outputs are provided 'AS IS' with zero guarantee
// of real-time accuracy or upstream availability.
//
// This file is subject to the terms and conditions defined in the LICENSE
// file located in the root directory of this source code repository.
// Removal or modification of this legal notice constitutes copyright infringement.
// ============================================================================
using System.Globalization;

namespace PortfolioSaver.Screensaver.Services;

public enum ScreensaverMode
{
    Configure,
    Preview,
    Fullscreen
}

public sealed class ScreensaverLaunchArguments
{
    public ScreensaverMode Mode { get; set; }
    public IntPtr PreviewHandle { get; set; }
}

public sealed class ScreensaverArgumentParser
{
    public ScreensaverLaunchArguments Parse(string[] args)
    {
        if (args.Length == 0)
            return new ScreensaverLaunchArguments { Mode = ScreensaverMode.Configure };

        string first = args[0].Trim();
        string normalized = first.ToLowerInvariant();
        string option = normalized;
        string? inlineValue = null;
        int separatorIndex = normalized.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex >= 0)
        {
            option = normalized[..separatorIndex];
            inlineValue = first[(separatorIndex + 1)..].Trim();
        }

        if (option is "/s" or "-s")
            return new ScreensaverLaunchArguments { Mode = ScreensaverMode.Fullscreen };

        if (option is "/p" or "-p")
        {
            string? handleText = !string.IsNullOrWhiteSpace(inlineValue)
                ? inlineValue
                : args.Length > 1 ? args[1].Trim() : null;

            return new ScreensaverLaunchArguments
            {
                Mode = ScreensaverMode.Preview,
                PreviewHandle = ParseHandle(handleText)
            };
        }

        if (option is "/c" or "-c" or "/showconfig" or "-showconfig" or "--showconfig")
            return new ScreensaverLaunchArguments { Mode = ScreensaverMode.Configure };

        return new ScreensaverLaunchArguments { Mode = ScreensaverMode.Configure };
    }

    private static IntPtr ParseHandle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return IntPtr.Zero;

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ParsePositiveHandle(value[2..], NumberStyles.HexNumber);
        }

        // HWND values are unsigned in practice; reject negative sentinels instead of passing them to native hosting.
        IntPtr decimalHandle = ParsePositiveHandle(value, NumberStyles.Integer);
        if (decimalHandle != IntPtr.Zero)
            return decimalHandle;

        // Windows normally supplies decimal HWND values; bare hex is kept for compatibility with older harnesses.
        return ParsePositiveHandle(value, NumberStyles.HexNumber);
    }

    private static IntPtr ParsePositiveHandle(string value, NumberStyles styles)
    {
        return long.TryParse(value, styles, CultureInfo.InvariantCulture, out long handle)
            && handle > 0
            && handle <= (long)IntPtr.MaxValue
            ? new IntPtr(handle)
            : IntPtr.Zero;
    }
}
