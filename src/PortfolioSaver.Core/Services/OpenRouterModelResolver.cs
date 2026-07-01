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
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using PortfolioSaver.Core.Constants;

namespace PortfolioSaver.Core.Services;

public static class OpenRouterModelResolver
{
    public const string AutoModelId = "auto";
    public const string AttributionReferer = "https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER";
    public const string AttributionTitle = "DO NOT PANIC PORTFOLIO VISUALIZER";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, CachedResolvedModel> ResolvedModelCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<Task<OpenRouterResolvedModel>>> DiscoveryTasks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex ParameterSizeRegex = new(
        @"(?<![A-Za-z0-9])(?<value>\d+(?:\.\d+)?)\s*(?<unit>[bBmM])\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<OpenRouterResolvedModel> ResolveAsync(
        HttpClient httpClient,
        string endpointUrl,
        string configuredModelId,
        CancellationToken cancellationToken,
        Action<string, string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsOpenRouterEndpoint(endpointUrl) || !IsAutoModel(configuredModelId))
            return new(configuredModelId, "configured-model");

        string cacheKey = $"{endpointUrl.TrimEnd('/')}::{configuredModelId}".ToUpperInvariant();
        if (TryGetCachedModel(cacheKey, out string? cachedModelId) && cachedModelId is not null)
            return new(cachedModelId, "openrouter-auto-cached");

        // Share only the active discovery call. A faulted Lazy can affect concurrent
        // waiters, but the finally block removes it so the next caller retries cleanly.
        Lazy<Task<OpenRouterResolvedModel>> discoveryTask = DiscoveryTasks.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<OpenRouterResolvedModel>>(
                () => DiscoverWithTimeoutAsync(httpClient, endpointUrl, cancellationToken, trace),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenRouterResolvedModel resolved = await discoveryTask.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (string.Equals(resolved.Resolution, "openrouter-auto-discovered", StringComparison.Ordinal))
                ResolvedModelCache[cacheKey] = new(resolved.ModelId, DateTimeOffset.UtcNow);
            return resolved;
        }
        finally
        {
            DiscoveryTasks.TryRemove(cacheKey, out _);
        }
    }

    public static bool IsAutoModel(string? modelId)
        => string.Equals(modelId, Defaults.DefaultAiModelId, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(modelId, AutoModelId, StringComparison.OrdinalIgnoreCase);

    public static bool IsOpenRouterEndpoint(string endpointUrl)
        => Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? endpoint) &&
           (string.Equals(endpoint.Host, "openrouter.ai", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Host.EndsWith(".openrouter.ai", StringComparison.OrdinalIgnoreCase));

    public static void AddAttributionHeaders(HttpRequestMessage request, string endpointUrl)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsOpenRouterEndpoint(endpointUrl))
            return;

        request.Headers.Remove("HTTP-Referer");
        request.Headers.Remove("X-OpenRouter-Title");
        request.Headers.TryAddWithoutValidation("HTTP-Referer", AttributionReferer);
        request.Headers.TryAddWithoutValidation("X-OpenRouter-Title", AttributionTitle);
    }

    public static void ClearCache()
    {
        DiscoveryTasks.Clear();
        ResolvedModelCache.Clear();
    }

    public static IReadOnlyList<OpenRouterModelCandidate> ListFreeInstructModels(JsonElement root)
    {
        if (!TryGetProperty(root, "data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            return [];

        List<OpenRouterModelCandidate> candidates = [];
        foreach (JsonElement model in data.EnumerateArray())
        {
            string id = GetStringProperty(model, "id");
            if (string.IsNullOrWhiteSpace(id) ||
                !IsFreeModel(model, id) ||
                !LooksLikeInstructOrChatModel(model, id) ||
                HasMandatoryOrDefaultReasoning(model))
            {
                continue;
            }

            candidates.Add(new(
                id,
                GetModelParameterSizeScore(model, id),
                GetLongProperty(model, "context_length")));
        }

        return candidates
            .OrderByDescending(candidate => candidate.ParameterSizeScore)
            .ThenByDescending(candidate => candidate.ContextLength)
            .ThenBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryGetCachedModel(string cacheKey, out string? modelId)
    {
        modelId = null;
        if (!ResolvedModelCache.TryGetValue(cacheKey, out CachedResolvedModel? cached) || cached is null)
            return false;

        if (DateTimeOffset.UtcNow - cached.CapturedAtUtc > CacheTtl)
        {
            ResolvedModelCache.TryRemove(cacheKey, out _);
            return false;
        }

        if (string.IsNullOrWhiteSpace(cached.ModelId))
            return false;

        modelId = cached.ModelId;
        return true;
    }

    private static async Task<OpenRouterResolvedModel> DiscoverWithTimeoutAsync(
        HttpClient httpClient,
        string endpointUrl,
        CancellationToken cancellationToken,
        Action<string, string>? trace)
    {
        using CancellationTokenSource discoveryTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        discoveryTimeout.CancelAfter(DiscoveryTimeout);
        try
        {
            return await DiscoverAsync(httpClient, endpointUrl, discoveryTimeout.Token, trace).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            trace?.Invoke("OpenRouterAutoModelFallback", "discovery-timeout");
            return new(Defaults.DefaultAiModelId, "openrouter-auto-fallback");
        }
    }

    private static async Task<OpenRouterResolvedModel> DiscoverAsync(
        HttpClient httpClient,
        string endpointUrl,
        CancellationToken cancellationToken,
        Action<string, string>? trace)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, BuildModelsUri(endpointUrl));
            AddAttributionHeaders(request, endpointUrl);
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            string? selectedModelId = ListFreeInstructModels(document.RootElement).FirstOrDefault()?.Id;
            if (!string.IsNullOrWhiteSpace(selectedModelId))
            {
                trace?.Invoke("OpenRouterAutoModelSelected", selectedModelId);
                return new(selectedModelId, "openrouter-auto-discovered");
            }

            trace?.Invoke("OpenRouterAutoModelFallback", "no-free-instruct-model");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            trace?.Invoke("OpenRouterAutoModelFallback", ex.GetType().Name);
        }
        catch (Exception ex)
        {
            trace?.Invoke("OpenRouterAutoModelFallback", ex.GetType().Name);
        }

        return new(Defaults.DefaultAiModelId, "openrouter-auto-fallback");
    }

    private static Uri BuildModelsUri(string endpointUrl)
        => new(new Uri($"{endpointUrl.TrimEnd('/')}/", UriKind.Absolute), "models");

    private static bool IsFreeModel(JsonElement model, string id)
    {
        if (id.EndsWith(":free", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!TryGetProperty(model, "pricing", out JsonElement pricing) || pricing.ValueKind != JsonValueKind.Object)
            return false;

        return IsZeroPrice(pricing, "prompt") &&
               IsZeroPrice(pricing, "completion") &&
               IsZeroPrice(pricing, "request");
    }

    private static bool IsZeroPrice(JsonElement pricing, string propertyName)
    {
        if (!TryGetProperty(pricing, propertyName, out JsonElement value))
            return true;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out decimal number) && number == 0m,
            JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number) && number == 0m,
            JsonValueKind.Null => true,
            _ => false
        };
    }

    private static bool LooksLikeInstructOrChatModel(JsonElement model, string id)
    {
        string name = GetStringProperty(model, "name");
        string slug = $"{id} {name}".ToLowerInvariant();
        if (slug.Contains("instruct", StringComparison.Ordinal) ||
            slug.Contains("chat", StringComparison.Ordinal))
        {
            return true;
        }

        if (TryGetProperty(model, "architecture", out JsonElement architecture) &&
            architecture.ValueKind == JsonValueKind.Object)
        {
            string instructType = GetStringProperty(architecture, "instruct_type");
            return !string.IsNullOrWhiteSpace(instructType) &&
                   !string.Equals(instructType, "none", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool HasMandatoryOrDefaultReasoning(JsonElement model)
    {
        if (!TryGetProperty(model, "reasoning", out JsonElement reasoning) || reasoning.ValueKind != JsonValueKind.Object)
            return false;

        return GetBooleanProperty(reasoning, "mandatory") ||
               GetBooleanProperty(reasoning, "default_enabled");
    }

    private static long GetModelParameterSizeScore(JsonElement model, string id)
    {
        string candidate = $"{id} {GetStringProperty(model, "name")}";
        MatchCollection matches = ParameterSizeRegex.Matches(candidate);
        long best = 0;
        foreach (Match match in matches)
        {
            if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                continue;

            double multiplier = string.Equals(match.Groups["unit"].Value, "b", StringComparison.OrdinalIgnoreCase)
                ? 1_000_000_000L
                : 1_000_000L;
            double score = value * multiplier;
            long clampedScore = score >= long.MaxValue ? long.MaxValue : (long)score;
            best = Math.Max(best, clampedScore);
        }

        return best;
    }

    private static string GetStringProperty(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long GetLongProperty(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement value))
            return 0;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out long number) ? number : 0,
            JsonValueKind.String => long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : 0,
            _ => 0
        };
    }

    private static bool GetBooleanProperty(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out JsonElement value) &&
           value.ValueKind == JsonValueKind.True;

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}

public sealed record OpenRouterResolvedModel(
    string ModelId,
    string Resolution);

public sealed record OpenRouterModelCandidate(
    string Id,
    long ParameterSizeScore,
    long ContextLength);

internal sealed record CachedResolvedModel(
    string ModelId,
    DateTimeOffset CapturedAtUtc);
