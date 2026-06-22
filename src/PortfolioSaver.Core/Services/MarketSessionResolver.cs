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
using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Services;

public sealed class MarketSessionResolver
{
    public MarketSession Resolve(DateTimeOffset utcNow)
    {
        TimeZoneInfo eastern;
        try
        {
            eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
        catch
        {
            return MarketSession.Unknown;
        }

        DateTimeOffset easternNow = TimeZoneInfo.ConvertTime(utcNow, eastern);
        if (easternNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return MarketSession.Closed;

        TimeOnly time = TimeOnly.FromDateTime(easternNow.DateTime);
        if (time >= new TimeOnly(4, 0) && time < new TimeOnly(9, 30))
            return MarketSession.PreMarket;
        if (time >= new TimeOnly(9, 30) && time < new TimeOnly(16, 0))
            return MarketSession.Regular;
        if (time >= new TimeOnly(16, 0) && time < new TimeOnly(20, 0))
            return MarketSession.AfterHours;

        return MarketSession.Closed;
    }
}
