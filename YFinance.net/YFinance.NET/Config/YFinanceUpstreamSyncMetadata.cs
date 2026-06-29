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
namespace YFinance.NET.Config;

public static class YFinanceUpstreamSyncMetadata
{
    // Keep these constants synchronized with YFinance.net/upstream-sync.json whenever an upstream review baseline changes.
    public const string UpstreamRepository = "https://github.com/ranaroussi/yfinance";
    public const string ForkRepository = "https://github.com/tuklusan/yfinance";
    public const string ReviewedCommit = "38c73ce33fb1ee77d37a0998c95c06e60356298e";
    public const string ReviewedCommitDate = "2026-06-28T19:12:48+01:00";
    public const string ReviewedVersion = "1.5.1";
    public const string ReviewedByCr = "CR-139";
}
