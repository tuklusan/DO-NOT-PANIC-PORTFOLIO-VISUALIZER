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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared.Diagnostics;

namespace PortfolioSaver.Config.Services;

public interface IAiNewsAccessValidationService
{
    Task<AiNewsAccessValidationResult> ValidateAsync(
        AppSettings settings,
        bool networkAvailable,
        CancellationToken cancellationToken = default);
}

public sealed class AiNewsAccessValidationService : IAiNewsAccessValidationService
{
    private readonly Func<TimeSpan, HttpClient> _httpClientFactory;

    public AiNewsAccessValidationService()
        : this(HttpClientFactory.Create)
    {
    }

    public AiNewsAccessValidationService(Func<TimeSpan, HttpClient> httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<AiNewsAccessValidationResult> ValidateAsync(
        AppSettings settings,
        bool networkAvailable,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);

        if (settings.NewsScrollerMode != NewsScrollerMode.SummarizedFinancialNews)
            return AiNewsAccessValidationResult.Skipped("AI summarized news is not selected.");

        string apiKey = ResolveApiKey(settings.AiApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            return AiNewsAccessValidationResult.Failed("Enter an AI API key, or switch Finance News to RSS Feed.");

        string endpointUrl = NormalizeEndpointUrl(settings.AiEndpointUrl);
        if (string.IsNullOrWhiteSpace(endpointUrl))
            return AiNewsAccessValidationResult.Failed("Enter a valid AI endpoint URL.");

        string modelId = string.IsNullOrWhiteSpace(settings.AiModelId)
            ? Defaults.DefaultAiModelId
            : settings.AiModelId.Trim();
        if (string.IsNullOrWhiteSpace(modelId))
            return AiNewsAccessValidationResult.Failed("Enter a valid AI model ID.");

        if (!networkAvailable)
            return AiNewsAccessValidationResult.Failed("Connect to the internet before validating AI summarized financial news.");

        string operationId = Guid.NewGuid().ToString("N");
        using HttpClient httpClient = _httpClientFactory(
            TimeSpan.FromSeconds(Math.Max(3, settings.HttpTimeoutSeconds)));

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, BuildChatCompletionsUri(endpointUrl));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            AddOpenRouterAttributionHeaders(request, endpointUrl);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    model = modelId,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = "Reply with OK only. This is a DO NOT PANIC finance-news configuration validation probe."
                        }
                    },
                    max_tokens = 8,
                    temperature = 0
                }),
                Encoding.UTF8,
                "application/json");

            TraceAiValidation("AiAccessValidationStart", operationId, endpointUrl, modelId);
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string reason = $"AI access was rejected by the provider ({(int)response.StatusCode}). Check the API key, endpoint URL, and model ID.";
                TraceAiValidation("AiAccessValidationFailed", operationId, endpointUrl, modelId, $"http-{(int)response.StatusCode}");
                return AiNewsAccessValidationResult.Failed(reason);
            }

            TraceAiValidation("AiAccessValidationSucceeded", operationId, endpointUrl, modelId);
            return AiNewsAccessValidationResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TraceAiValidation("AiAccessValidationFailed", operationId, endpointUrl, modelId, "cancelled");
            throw;
        }
        catch (TaskCanceledException)
        {
            TraceAiValidation("AiAccessValidationFailed", operationId, endpointUrl, modelId, "timeout");
            return AiNewsAccessValidationResult.Failed("AI access validation timed out. Check the endpoint/model or switch Finance News to RSS Feed.");
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
                throw;

            // Do not log ex.Message or ex.ToString(); provider/client exceptions may include request details.
            TraceAiValidation("AiAccessValidationFailed", operationId, endpointUrl, modelId, ex.GetType().Name);
            return AiNewsAccessValidationResult.Failed("AI access validation failed. Check the API key, endpoint URL, and model ID.");
        }
    }

    private static string ResolveApiKey(string? explicitApiKey)
    {
        return string.IsNullOrWhiteSpace(explicitApiKey)
            ? string.Empty
            : explicitApiKey.Trim();
    }

    private static string NormalizeEndpointUrl(string? endpointUrl)
    {
        string candidate = string.IsNullOrWhiteSpace(endpointUrl)
            ? Defaults.DefaultAiEndpointUrl
            : endpointUrl.Trim();

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return string.Empty;
        }

        return candidate.TrimEnd('/');
    }

    private static Uri BuildChatCompletionsUri(string endpointUrl)
    {
        // Settings expect an OpenAI-compatible base endpoint; append the standard chat route when needed.
        string normalized = endpointUrl.TrimEnd('/');
        if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return new Uri(normalized);

        return new Uri(normalized + "/chat/completions");
    }

    private static void AddOpenRouterAttributionHeaders(HttpRequestMessage request, string endpointUrl)
    {
        if (!endpointUrl.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase))
            return;

        request.Headers.Remove("HTTP-Referer");
        request.Headers.Remove("X-OpenRouter-Title");
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER");
        request.Headers.TryAddWithoutValidation("X-OpenRouter-Title", "DO NOT PANIC PORTFOLIO VISUALIZER");
    }

    private static void TraceAiValidation(string eventName, string operationId, string endpointUrl, string modelId, string? reason = null)
    {
        List<KeyValuePair<string, object?>> fields =
        [
            new("operation_id", operationId),
            new("endpoint", BuildTraceEndpoint(endpointUrl)),
            new("model_id", modelId)
        ];
        if (!string.IsNullOrWhiteSpace(reason))
            fields.Add(new("reason", reason));

        TraceLog.InfoState("Config.Validation", eventName, fields);
    }

    private static string BuildTraceEndpoint(string endpointUrl)
    {
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? uri))
            return "invalid";

        return $"{uri.Scheme}://{uri.Host}";
    }
}

public sealed class AiNewsAccessValidationResult
{
    public bool IsValid { get; init; }
    public bool ValidationSkipped { get; init; }
    public string Message { get; init; } = string.Empty;

    public static AiNewsAccessValidationResult Success()
        => new() { IsValid = true };

    public static AiNewsAccessValidationResult Skipped(string message)
        => new() { IsValid = true, ValidationSkipped = true, Message = message };

    public static AiNewsAccessValidationResult Failed(string message)
        => new() { IsValid = false, Message = message };
}
