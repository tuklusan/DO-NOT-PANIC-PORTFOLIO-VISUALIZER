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
using System;
using System.IO;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Core.Services;

public static class AppSettingsNormalizer
{
    private const string LegacyDefaultDeepSeekEndpointUrl = "https://api.deepseek.com";
    private const string LegacyDefaultDeepSeekModelId = "deepseek-v4-flash";
    public static AppSettings Normalize(AppSettings? settings)
    {
        AppSettings normalized = settings ?? Defaults.CreateSettings();

        normalized.Groups ??= [];
        normalized.Groups = normalized.Groups
            .Take(Defaults.MaxTapeCount)
            .Select((group, index) => NormalizeGroup(group, index))
            .ToList();
        if (normalized.Groups.Count == 0)
        {
            normalized.Groups = Defaults.CreateSettings().Groups
                .Take(Defaults.MaxTapeCount)
                .Select((group, index) => NormalizeGroup(CloneGroup(group), index))
                .ToList();
        }

        ApplyLegacyAlternatingDirectionFallback(normalized.Groups);
        ApplyLegacyDifferentiatedSpeedFallback(normalized.Groups);

        normalized.HistoricalCacheRootFolder = NormalizeHistoricalCachePath(
            normalized.HistoricalCacheRootFolder,
            Defaults.GetHistoricalCacheFolder());

        normalized.BackgroundImageFolder = NormalizePath(
            normalized.BackgroundImageFolder,
            Defaults.GetManagedBackgroundCacheFolder());

        normalized.CustomBackgroundImageFolder = NormalizePath(
            normalized.CustomBackgroundImageFolder,
            string.Empty);

        normalized.DeepSeekApiKey = NormalizeApiKey(
            normalized.DeepSeekApiKey,
            "DEEPSEEK_API_KEY",
            "PORTFOLIOSAVER_DEEPSEEK_API_KEY");
        normalized.DeepSeekEndpointUrl = NormalizeDeepSeekEndpointUrl(normalized.DeepSeekEndpointUrl);
        normalized.DeepSeekModelId = NormalizeDeepSeekModelId(normalized.DeepSeekModelId);

        normalized.MarketCalendarRefreshHours = Clamp(
            normalized.MarketCalendarRefreshHours,
            1,
            7 * 24,
            12);

        normalized.RefreshSecondsPortfolio = Defaults.DefaultDesktopRefreshSeconds;
        normalized.RefreshSecondsOffHours = Defaults.DefaultDesktopRefreshSeconds;

        normalized.BackgroundChangeSeconds = Clamp(
            normalized.BackgroundChangeSeconds,
            Defaults.MinBackgroundChangeSeconds,
            Defaults.MaxRefreshSeconds,
            300);

        normalized.NewsRefreshMinutes = Clamp(
            normalized.NewsRefreshMinutes,
            Defaults.MinNewsRefreshMinutes,
            Defaults.MaxNewsRefreshMinutes,
            15);

        normalized.NewsScrollerMode = NormalizeNewsScrollerMode(normalized.NewsScrollerMode);
        normalized.DeepSeekWritingStyle = NormalizeDeepSeekWritingStyle(normalized.DeepSeekWritingStyle);
        normalized.NewsFeedUrl = NormalizeNewsFeedUrl(normalized.NewsFeedUrl);

        return normalized;
    }


    private static void ApplyLegacyAlternatingDirectionFallback(IReadOnlyList<TickerGroup> groups)
    {
        if (groups.Count < 2)
            return;

        bool hasAnyRight = groups.Any(group => group.Direction == ScrollDirection.Right);
        if (hasAnyRight)
            return;

        for (int index = 0; index < groups.Count; index++)
            groups[index].Direction = index % 2 == 0 ? ScrollDirection.Left : ScrollDirection.Right;
    }

    private static void ApplyLegacyDifferentiatedSpeedFallback(IReadOnlyList<TickerGroup> groups)
    {
        if (groups.Count < 2)
            return;

        double baseline = groups[0].Speed;
        bool uniformBaselineSpeed = Math.Abs(baseline - Defaults.DefaultTapeBaseSpeed) < 0.0001d &&
                                    groups.All(group => Math.Abs(group.Speed - baseline) < 0.0001d);
        if (!uniformBaselineSpeed)
            return;

        for (int index = 0; index < groups.Count; index++)
            groups[index].Speed = Defaults.GetDefaultTapeSpeed(index);
    }

    private static TickerGroup NormalizeGroup(TickerGroup? group, int index)
    {
        TickerGroup normalized = group ?? Defaults.CreateEmptyTickerGroup(index);
        normalized.Name = NormalizeTapeName(normalized.Name, index);
        normalized.Speed = Math.Clamp(
            normalized.Speed <= 0 ? Defaults.GetDefaultTapeSpeed(index) : normalized.Speed,
            Defaults.MinTapeSpeed,
            Defaults.MaxTapeSpeed);
        normalized.RowHeight = normalized.RowHeight <= 0 ? 56.0 : normalized.RowHeight;
        normalized.Tickers ??= [];
        normalized.Tickers = normalized.Tickers
            .Where(item => item is not null)
            .Take(Defaults.MaxTickersPerTape)
            .Select(NormalizeTicker)
            .ToList();
        return normalized;
    }

    private static TickerItem NormalizeTicker(TickerItem item)
        => new()
        {
            Symbol = (item.Symbol ?? string.Empty).Trim(),
            DisplayName = (item.DisplayName ?? string.Empty).Trim(),
            Quantity = item.Quantity,
            CostBasis = item.CostBasis,
            Currency = string.IsNullOrWhiteSpace(item.Currency) ? "USD" : item.Currency.Trim(),
            Enabled = item.Enabled
        };

    private static TickerGroup CloneGroup(TickerGroup source)
        => new()
        {
            Name = source.Name,
            Speed = source.Speed,
            Direction = source.Direction,
            RenderMode = source.RenderMode,
            RowHeight = source.RowHeight,
            Enabled = source.Enabled,
            Tickers = source.Tickers.Select(CloneTicker).ToList()
        };

    private static TickerItem CloneTicker(TickerItem source)
        => new()
        {
            Symbol = source.Symbol,
            DisplayName = source.DisplayName,
            Quantity = source.Quantity,
            CostBasis = source.CostBasis,
            Currency = source.Currency,
            Enabled = source.Enabled
        };

    private static string NormalizeHistoricalCachePath(string currentValue, string fallbackValue)
    {
        string normalized = NormalizePath(currentValue, fallbackValue);
        string legacy = Environment.ExpandEnvironmentVariables(Defaults.GetLegacyHistoricalCacheFolder());
        if (PathsEqual(normalized, legacy))
            return fallbackValue;

        return normalized;
    }

    private static string NormalizePath(string currentValue, string fallbackValue)
    {
        string value = string.IsNullOrWhiteSpace(currentValue) ? fallbackValue : currentValue;
        if (IsHttpUri(value))
            value = fallbackValue;

        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Environment.ExpandEnvironmentVariables(value.Trim());
    }

    private static bool IsHttpUri(string value)
        => Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeApiKey(string currentValue, params string[] environmentVariableNames)
    {
        string environmentValue = GetFirstEnvironmentVariableValue(environmentVariableNames);
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return environmentValue;

        string trimmed = (currentValue ?? string.Empty).Trim();
        if (IsApiKeyPlaceholder(trimmed))
            return string.Empty;

        return trimmed;
    }

    private static string GetFirstEnvironmentVariableValue(IEnumerable<string> environmentVariableNames)
    {
        foreach (string name in environmentVariableNames)
        {
            string value = (Environment.GetEnvironmentVariable(name) ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static bool IsApiKeyPlaceholder(string value)
        => value switch
        {
            "" => false,
            "abcdefghijklmnopqrstuvwxyz01234567890abc" => true,
            "abcdefghijklmnopqrstuvwxyz012345" => true,
            "abcdefghijklmn.01234567" => true,
            _ => value.StartsWith("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase) ||
                 value.StartsWith("REDACTED", StringComparison.OrdinalIgnoreCase)
        };

    private static int Clamp(int value, int min, int max, int fallback)
    {
        int candidate = value <= 0 ? fallback : value;
        return Math.Clamp(candidate, min, max);
    }

    private static string NormalizeNewsFeedUrl(string currentValue)
    {
        string candidate = (currentValue ?? string.Empty).Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return uri.ToString();
        }

        return Defaults.DefaultNewsFeedUrl;
    }

    private static string NormalizeDeepSeekEndpointUrl(string currentValue)
    {
        string candidate = (currentValue ?? string.Empty).Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            string normalized = uri.ToString().TrimEnd('/');
            const string chatPath = "/chat/completions";
            if (normalized.EndsWith(chatPath, StringComparison.OrdinalIgnoreCase))
                normalized = normalized[..^chatPath.Length];

            if (string.Equals(normalized, LegacyDefaultDeepSeekEndpointUrl, StringComparison.OrdinalIgnoreCase))
                return Defaults.DefaultDeepSeekEndpointUrl;

            return normalized;
        }

        return Defaults.DefaultDeepSeekEndpointUrl;
    }

    private static string NormalizeDeepSeekModelId(string currentValue)
    {
        string candidate = (currentValue ?? string.Empty).Trim();
        if (string.Equals(candidate, LegacyDefaultDeepSeekModelId, StringComparison.OrdinalIgnoreCase))
            return Defaults.DefaultDeepSeekModelId;

        return string.IsNullOrWhiteSpace(candidate)
            ? Defaults.DefaultDeepSeekModelId
            : candidate;
    }

    private static NewsScrollerMode NormalizeNewsScrollerMode(NewsScrollerMode currentValue)
        => Enum.IsDefined(typeof(NewsScrollerMode), currentValue)
            ? currentValue
            : NewsScrollerMode.SummarizedFinancialNews;

    private static DeepSeekWritingStyle NormalizeDeepSeekWritingStyle(DeepSeekWritingStyle currentValue)
        => Enum.IsDefined(typeof(DeepSeekWritingStyle), currentValue)
            ? currentValue
            : DeepSeekWritingStyle.DouglasAdams;

    private static string NormalizeTapeName(string? currentValue, int index)
    {
        string candidate = (currentValue ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return Defaults.GetDefaultTapeName(index + 1);

        return candidate.Length > Defaults.MaxTapeNameLength
            ? candidate[..Defaults.MaxTapeNameLength]
            : candidate;
    }
}
