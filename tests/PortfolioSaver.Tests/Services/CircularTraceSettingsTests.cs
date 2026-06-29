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
using PortfolioSaver.Shared.Diagnostics;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class CircularTraceSettingsTests
{
    private const string TraceMaxMegabytesEnvironmentVariable = "DONOTPANICPORTFOLIOVISUALIZER_TRACE_MAX_MB";

    [Theory]
    [InlineData(null, 32)]
    [InlineData(" 4 ", 4)]
    [InlineData("0", 4)]
    [InlineData("-1", 4)]
    [InlineData("300", 256)]
    [InlineData("abc", 32)]
    public void ResolveMaxTraceBytes_ParsesAndClampsEnvironmentValue(string? configured, int expectedMegabytes)
    {
        string? previous = Environment.GetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, configured);

            int bytes = InvokeResolveMaxTraceBytes(GetSharedSettingsType());

            Assert.Equal(expectedMegabytes * 1024 * 1024, bytes);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ResolveCachedMaxTraceBytes_ReturnsFirstResolvedValueUntilCacheReset()
    {
        string? previous = Environment.GetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable);
        try
        {
            int cachedBytes = 0;
            Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, "4");

            int first = InvokeResolveCachedMaxTraceBytes(GetSharedSettingsType(), ref cachedBytes);

            Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, "8");
            int second = InvokeResolveCachedMaxTraceBytes(GetSharedSettingsType(), ref cachedBytes);

            Assert.Equal(4 * 1024 * 1024, first);
            Assert.Equal(first, second);

            Interlocked.Exchange(ref cachedBytes, 0);
            int third = InvokeResolveCachedMaxTraceBytes(GetSharedSettingsType(), ref cachedBytes);

            Assert.Equal(8 * 1024 * 1024, third);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void YFinanceLinkedSettingsType_UsesSameEnvironmentContract()
    {
        string? previous = Environment.GetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, "4");
            int bytes = InvokeResolveMaxTraceBytes(GetYFinanceLinkedSettingsType());

            Assert.Equal(4 * 1024 * 1024, bytes);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TraceMaxMegabytesEnvironmentVariable, previous);
        }
    }

    private static Type GetSharedSettingsType()
        => typeof(TraceLog).Assembly.GetType("PortfolioSaver.Shared.Diagnostics.CircularTraceSettings")
           ?? throw new InvalidOperationException("PortfolioSaver.Shared CircularTraceSettings type was not found.");

    private static Type GetYFinanceLinkedSettingsType()
        => typeof(YFinance.NET.Diagnostics.YFinanceCircularTraceSink).Assembly.GetType("PortfolioSaver.Shared.Diagnostics.CircularTraceSettings")
           ?? throw new InvalidOperationException("YFinance.NET linked CircularTraceSettings type was not found.");

    private static int InvokeResolveMaxTraceBytes(Type settingsType)
    {
        MethodInfo method = settingsType.GetMethod("ResolveMaxTraceBytes", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{settingsType.Assembly.GetName().Name} CircularTraceSettings.ResolveMaxTraceBytes was not found.");
        return (int)(method.Invoke(null, []) ?? throw new InvalidOperationException("ResolveMaxTraceBytes returned null."));
    }

    private static int InvokeResolveCachedMaxTraceBytes(Type settingsType, ref int cachedBytes)
    {
        MethodInfo method = settingsType.GetMethod("ResolveCachedMaxTraceBytes", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{settingsType.Assembly.GetName().Name} CircularTraceSettings.ResolveCachedMaxTraceBytes was not found.");
        object?[] parameters = [cachedBytes];
        int resolved = (int)(method.Invoke(null, parameters) ?? throw new InvalidOperationException("ResolveCachedMaxTraceBytes returned null."));
        cachedBytes = (int)(parameters[0] ?? 0);
        return resolved;
    }
}
