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
    private static readonly Regex ProviderKeyPattern = new(
        @"(?i)\b(?:sk|whsec|ghp|github_pat|xoxb|xoxp)[_-][a-zA-Z0-9_-]{8,}\b",
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

        redacted = BearerPattern.Replace(redacted, "Bearer " + RedactedValue);
        return ProviderKeyPattern.Replace(redacted, RedactedValue);
    }
}
