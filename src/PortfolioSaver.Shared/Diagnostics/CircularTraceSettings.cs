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

namespace PortfolioSaver.Shared.Diagnostics;

public static class CircularTraceSettings
{
    public const string MaxTraceMegabytesEnvironmentVariable = "DONOTPANICPORTFOLIOVISUALIZER_TRACE_MAX_MB";
    public const int DefaultMaxTraceMegabytes = 32;
    public const int MinimumMaxTraceMegabytes = 4;
    public const int MaximumMaxTraceMegabytes = 256;

    public static int ResolveMaxTraceBytes()
    {
        string? configured = Environment.GetEnvironmentVariable(MaxTraceMegabytesEnvironmentVariable)?.Trim();
        if (!int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out int megabytes))
            megabytes = DefaultMaxTraceMegabytes;

        megabytes = Math.Clamp(megabytes, MinimumMaxTraceMegabytes, MaximumMaxTraceMegabytes);
        return megabytes * 1024 * 1024;
    }

    public static int ResolveCachedMaxTraceBytes(ref int cachedBytes)
    {
        int resolved = Volatile.Read(ref cachedBytes);
        if (resolved > 0)
            return resolved;

        resolved = ResolveMaxTraceBytes();
        int previous = Interlocked.CompareExchange(ref cachedBytes, resolved, 0);
        return previous > 0 ? previous : resolved;
    }
}
