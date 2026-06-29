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

public sealed class NullYFinanceTraceSink : IYFinanceTraceSink
{
    public static NullYFinanceTraceSink Instance { get; } = new();

    private NullYFinanceTraceSink()
    {
    }

    public void InfoState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
    {
    }

    public void WarnState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
    {
    }

    public void ErrorState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields, Exception? exception = null)
    {
    }
}
