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
using System.Reflection;
using PortfolioSaver.Shared;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void RedactSensitivePatterns_RedactsSensitiveAssignments()
    {
        string sensitiveKey = string.Concat("pass", "word");
        string secretValue = "should-not-survive";

        string redacted = RedactSensitivePatterns($"{sensitiveKey}={secretValue}");

        Assert.Contains(sensitiveKey + "=", redacted, StringComparison.Ordinal);
        Assert.Contains("redacted", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secretValue, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSensitivePatterns_RedactsBearerValues()
    {
        string secretValue = string.Concat("ey", "J", new string('a', 24));

        string redacted = RedactSensitivePatterns($"Bearer {secretValue}");

        Assert.Contains("Bearer", redacted, StringComparison.Ordinal);
        Assert.Contains("redacted", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secretValue, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSensitivePatterns_RedactsProviderStyleKeys()
    {
        string secretValue = string.Concat("sk", "-", new string('a', 24));

        string redacted = RedactSensitivePatterns($"value {secretValue}");

        Assert.Contains("redacted", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secretValue, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSensitivePatterns_RedactsProviderStyleKeysWithUppercaseBodies()
    {
        string secretValue = string.Concat("sk", "-", new string('A', 24));

        string redacted = RedactSensitivePatterns($"value {secretValue}");

        Assert.Contains("redacted", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secretValue, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSensitivePatterns_RedactsUnderscoreProviderStyleKeys()
    {
        string secretValue = string.Concat("whsec", "_", new string('a', 24));

        string redacted = RedactSensitivePatterns($"value {secretValue}");

        Assert.Contains("redacted", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secretValue, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSensitivePatterns_RedactsCompoundProviderStyleKeys()
    {
        string secretValue = string.Concat("sk", "_", "live", "_", new string('A', 24));

        string redacted = RedactSensitivePatterns($"value {secretValue}");

        Assert.Contains("redacted", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secretValue, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSensitivePatterns_RedactsShortProviderStyleKeys()
    {
        string secretValue = string.Concat("sk", "-", new string('a', 8));

        string redacted = RedactSensitivePatterns($"value {secretValue}");

        Assert.Contains("redacted", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secretValue, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSensitivePatterns_DoesNotRedactOrdinaryWordsBeginningWithProviderPrefixes()
    {
        const string message = "The organization policy kept original display order.";

        string redacted = RedactSensitivePatterns(message);

        Assert.Equal(message, redacted);
    }

    [Fact]
    public void RedactSensitivePatterns_RedactsCommonProviderTokenPrefixes()
    {
        string secretValue = string.Concat("ghp", "_", new string('a', 24));

        string redacted = RedactSensitivePatterns($"value {secretValue}");

        Assert.Contains("redacted", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secretValue, redacted, StringComparison.Ordinal);
    }

    private static string RedactSensitivePatterns(string value)
    {
        // YFinance.NET links this same source file into its own assembly, so tests
        // anchor the reflection target to the product shared assembly deliberately.
        Type redactorType = typeof(AppIdentity).Assembly.GetType(
            "PortfolioSaver.Shared.Diagnostics.SensitiveDataRedactor",
            throwOnError: true)!;
        MethodInfo method = redactorType.GetMethod(
            "RedactSensitivePatterns",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(redactorType.FullName, "RedactSensitivePatterns");

        return (string)method.Invoke(null, [value])!;
    }
}
