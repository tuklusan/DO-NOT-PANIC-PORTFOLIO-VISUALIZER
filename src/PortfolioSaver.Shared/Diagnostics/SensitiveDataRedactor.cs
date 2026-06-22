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
using System.Text.RegularExpressions;

namespace PortfolioSaver.Shared.Diagnostics;

public static class SensitiveDataRedactor
{
    public const string RedactedValue = "<redacted>";

    private static readonly string[] SensitiveKeyFragments = ["key", "secret", "token", "password", "authorization", "credential"];
    private static readonly Regex SensitiveAssignmentPattern = new(
        @"(?i)\b(api[_-]?key|secret|token|password|authorization|credential)\s*[:=]\s*[^\s\|;]+",
        RegexOptions.Compiled);
    private static readonly Regex BearerPattern = new(
        @"(?i)\bbearer\s+[^\s\|;]+",
        RegexOptions.Compiled);

    public static bool IsSensitiveKey(string key)
        => SensitiveKeyFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public static string RedactSensitivePatterns(string value)
    {
        string redacted = SensitiveAssignmentPattern.Replace(value, match =>
        {
            int separator = match.Value.IndexOfAny([':', '=']);
            return separator < 0 ? RedactedValue : match.Value[..(separator + 1)] + RedactedValue;
        });

        return BearerPattern.Replace(redacted, "Bearer " + RedactedValue);
    }
}
