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
namespace YFinance.NET.Config;

public static class YFinanceUpstreamSyncMetadata
{
    // Keep these constants synchronized with YFinance.net/upstream-sync.json whenever an upstream review baseline changes.
    public const string UpstreamRepository = "https://github.com/ranaroussi/yfinance";
    public const string ForkRepository = "https://github.com/tuklusan/yfinance";
    public const string ReviewedCommit = "125b12e058fe37971390e32333d2cf9edb2a8a50";
    public const string ReviewedCommitDate = "2026-05-28T21:01:28+01:00";
    public const string ReviewedVersion = "1.4.1";
    public const string ReviewedByCr = "CR-062";
}
