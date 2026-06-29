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
namespace YFinance.NET.Diagnostics;

public sealed class YFinanceTrace
{
    private readonly IYFinanceTraceSink _sink;

    public YFinanceTrace(IYFinanceTraceSink? sink = null)
    {
        _sink = sink ?? NullYFinanceTraceSink.Instance;
    }

    public void InfoState(string source, string eventName, params (string Key, object? Value)[] fields)
        => _sink.InfoState(source, eventName, Map(fields));

    public void WarnState(string source, string eventName, params (string Key, object? Value)[] fields)
        => _sink.WarnState(source, eventName, Map(fields));

    public void ErrorState(string source, string eventName, Exception? exception = null, params (string Key, object? Value)[] fields)
        => _sink.ErrorState(source, eventName, Map(fields), exception);

    private static IEnumerable<KeyValuePair<string, object?>> Map(IEnumerable<(string Key, object? Value)> fields)
        => fields.Select(static field => new KeyValuePair<string, object?>(field.Key, field.Value));
}
