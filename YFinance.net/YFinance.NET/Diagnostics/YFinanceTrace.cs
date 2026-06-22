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
