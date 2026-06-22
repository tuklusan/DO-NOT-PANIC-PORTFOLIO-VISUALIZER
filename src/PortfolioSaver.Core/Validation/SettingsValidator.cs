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
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Core.Validation;

public sealed class SettingsValidator
{
    public IReadOnlyList<string> Validate(AppSettings settings)
    {
        List<string> errors = [];

        if (settings.NewsRefreshMinutes is < Defaults.MinNewsRefreshMinutes or > Defaults.MaxNewsRefreshMinutes)
            errors.Add("News refresh interval must be between 5 minutes and 4 hours.");
        if (settings.BackgroundChangeSeconds is < Defaults.MinBackgroundChangeSeconds or > Defaults.MaxRefreshSeconds)
            errors.Add("Background change interval must be between 2 minutes and 4 hours.");
        if (settings.HttpTimeoutSeconds < 3)
            errors.Add("HTTP timeout must be at least 3 seconds.");
        if (settings.DimOpacity is < 0 or > 1)
            errors.Add("Dim opacity must be between 0 and 1.");

        if (settings.HistoricalLookbackDays is < 3 or > 14)
            errors.Add("Historical lookback days must be between 3 and 14.");

        if (settings.HistoricalRefreshHours is < 1 or > 24)
            errors.Add("Historical refresh hours must be between 1 and 24.");

        if (settings.MaxFloatingGraphsPerTape is < 0 or > 8)
            errors.Add("Max floating graphs per tape must be between 0 and 8.");

        if (settings.NewsScrollerMode == Core.Enums.NewsScrollerMode.RssFeed &&
            (!Uri.TryCreate(settings.NewsFeedUrl, UriKind.Absolute, out Uri? newsUri) ||
             (newsUri.Scheme != Uri.UriSchemeHttps && newsUri.Scheme != Uri.UriSchemeHttp)))
        {
            errors.Add("News feed URL must be a valid http or https URL.");
        }

        if (settings.Groups.Count > Defaults.MaxTapeCount)
            errors.Add($"No more than {Defaults.MaxTapeCount} tapes can be configured.");

        foreach ((TickerGroup group, int index) in settings.Groups.Select((group, index) => (group, index)))
        {
            if (string.IsNullOrWhiteSpace(group.Name))
                errors.Add("Each ticker group must have a name.");
            else if (group.Name.Trim().Length > Defaults.MaxTapeNameLength)
                errors.Add($"Tape names must be {Defaults.MaxTapeNameLength} characters or fewer.");

            if (group.Speed is < Defaults.MinTapeSpeed or > Defaults.MaxTapeSpeed)
                errors.Add($"'{group.Name}' speed must stay between {Defaults.MinTapeSpeed:0.00} and {Defaults.MaxTapeSpeed:0.00}.");

            if (group.Tickers.Count > Defaults.MaxTickersPerTape)
                errors.Add($"'{group.Name}' can contain at most {Defaults.MaxTickersPerTape} tickers.");

            foreach (TickerItem ticker in group.Tickers)
            {
                if (string.IsNullOrWhiteSpace(ticker.Symbol))
                    errors.Add($"Tape {index + 1} contains an empty ticker symbol.");
            }
        }

        return errors;
    }
}
