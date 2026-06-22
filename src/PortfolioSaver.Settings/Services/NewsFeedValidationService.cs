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
using System.Net.Http;
using System.Xml.Linq;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Data.Services;

namespace PortfolioSaver.Config.Services;

public sealed class NewsFeedValidationService
{
    public async Task<NewsFeedValidationResult> ValidateAsync(
        string? feedUrl,
        int timeoutSeconds,
        bool networkAvailable,
        CancellationToken cancellationToken = default)
    {
        string candidate = (feedUrl ?? string.Empty).Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return NewsFeedValidationResult.ResetToDefault(
                "The RSS feed URL was not a valid http or https address, so it was reset to the default finance feed.");
        }

        if (!networkAvailable)
        {
            return new NewsFeedValidationResult
            {
                IsValid = true,
                ValidationSkipped = true,
                ResolvedFeedUrl = uri.ToString(),
                Message = "RSS feed validation was skipped because no network connection was detected."
            };
        }

        using HttpClient client = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, timeoutSeconds)));
        try
        {
            using HttpResponseMessage response = await client.GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
            string xml = await response.Content.ReadAsStringAsync(cancellationToken);

            XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            bool hasItemTitles = document.Descendants("item")
                .Elements("title")
                .Select(element => (element.Value ?? string.Empty).Trim())
                .Any(title => !string.IsNullOrWhiteSpace(title));
            bool hasAtomTitles = document.Descendants().Any(element =>
                string.Equals(element.Name.LocalName, "entry", StringComparison.OrdinalIgnoreCase)) &&
                document.Descendants().Any(element =>
                    string.Equals(element.Name.LocalName, "title", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace((element.Value ?? string.Empty).Trim()));

            if (!hasItemTitles && !hasAtomTitles)
            {
                return NewsFeedValidationResult.ResetToDefault(
                    "The RSS feed did not contain any readable headlines, so it was reset to the default finance feed.");
            }

            return new NewsFeedValidationResult
            {
                IsValid = true,
                ResolvedFeedUrl = uri.ToString()
            };
        }
        catch
        {
            return NewsFeedValidationResult.ResetToDefault(
                "The RSS feed could not be read as a valid news feed, so it was reset to the default finance feed.");
        }
    }
}

public sealed class NewsFeedValidationResult
{
    public bool IsValid { get; init; }
    public bool ValidationSkipped { get; init; }
    public bool WasResetToDefault { get; init; }
    public string ResolvedFeedUrl { get; init; } = Defaults.DefaultNewsFeedUrl;
    public string Message { get; init; } = string.Empty;

    public static NewsFeedValidationResult ResetToDefault(string message)
        => new()
        {
            IsValid = false,
            WasResetToDefault = true,
            ResolvedFeedUrl = Defaults.DefaultNewsFeedUrl,
            Message = message
        };
}
