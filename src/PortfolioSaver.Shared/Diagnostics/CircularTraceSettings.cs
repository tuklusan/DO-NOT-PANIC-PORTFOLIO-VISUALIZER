// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
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
