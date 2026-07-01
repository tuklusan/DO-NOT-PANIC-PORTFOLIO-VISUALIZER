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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class OpenRouterModelResolverTests
{
    private const string DiscoveredModelId = "nousresearch/hermes-3-llama-3.1-405b:free";

    public OpenRouterModelResolverTests()
    {
        OpenRouterModelResolver.ClearCache();
    }

    [Fact]
    public async Task ResolveAsync_NonOpenRouterEndpoint_ReturnsConfiguredModelWithoutHttp()
    {
        using HttpClient client = new(new ThrowingHandler());

        OpenRouterResolvedModel resolved = await OpenRouterModelResolver.ResolveAsync(
            client,
            "https://ai.example.test/v1",
            Defaults.DefaultAiModelId,
            CancellationToken.None);

        Assert.Equal(Defaults.DefaultAiModelId, resolved.ModelId);
        Assert.Equal("configured-model", resolved.Resolution);
    }

    [Fact]
    public async Task ResolveAsync_CustomOpenRouterModel_ReturnsConfiguredModelWithoutDiscovery()
    {
        using HttpClient client = new(new ThrowingHandler());

        OpenRouterResolvedModel resolved = await OpenRouterModelResolver.ResolveAsync(
            client,
            "https://openrouter.ai/api/v1",
            "meta-llama/llama-3.3-70b-instruct:free",
            CancellationToken.None);

        Assert.Equal("meta-llama/llama-3.3-70b-instruct:free", resolved.ModelId);
        Assert.Equal("configured-model", resolved.Resolution);
    }

    [Fact]
    public async Task ResolveAsync_CachesDiscoveredModelPerEndpoint()
    {
        int modelListRequests = 0;
        string endpointUrl = $"https://cache-{Guid.NewGuid():N}.openrouter.ai/api/v1";
        using HttpClient client = new(new DelegateHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.ToString() == $"{endpointUrl}/models")
            {
                modelListRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CreateModelsJson(), Encoding.UTF8, "application/json")
                };
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        }));

        OpenRouterResolvedModel first = await OpenRouterModelResolver.ResolveAsync(
            client,
            endpointUrl,
            Defaults.DefaultAiModelId,
            CancellationToken.None);
        OpenRouterResolvedModel second = await OpenRouterModelResolver.ResolveAsync(
            client,
            endpointUrl,
            Defaults.DefaultAiModelId,
            CancellationToken.None);

        Assert.Equal(DiscoveredModelId, first.ModelId);
        Assert.Equal(DiscoveredModelId, second.ModelId);
        Assert.Equal("openrouter-auto-discovered", first.Resolution);
        Assert.Equal("openrouter-auto-cached", second.Resolution);
        Assert.Equal(1, modelListRequests);
    }

    [Fact]
    public async Task ResolveAsync_ConcurrentAutoResolutionSharesSingleDiscoveryTask()
    {
        int modelListRequests = 0;
        string endpointUrl = $"https://concurrent-{Guid.NewGuid():N}.openrouter.ai/api/v1";
        TaskCompletionSource releaseDiscovery = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using HttpClient client = new(new AsyncDelegateHandler(async request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.ToString() == $"{endpointUrl}/models")
            {
                Interlocked.Increment(ref modelListRequests);
                await releaseDiscovery.Task;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CreateModelsJson(), Encoding.UTF8, "application/json")
                };
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        }));

        Task<OpenRouterResolvedModel>[] resolutionTasks = Enumerable.Range(0, 5).Select(_ => OpenRouterModelResolver.ResolveAsync(
                client,
                endpointUrl,
                Defaults.DefaultAiModelId,
                CancellationToken.None)).ToArray();

        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref modelListRequests) == 1, TimeSpan.FromSeconds(2)));
        releaseDiscovery.SetResult();
        OpenRouterResolvedModel[] resolvedModels = await Task.WhenAll(resolutionTasks);

        Assert.All(resolvedModels, resolved => Assert.Equal(DiscoveredModelId, resolved.ModelId));
        Assert.Equal(1, modelListRequests);
    }

    [Fact]
    public async Task ResolveAsync_PreCancelledTokenDoesNotStartDiscovery()
    {
        int modelListRequests = 0;
        using HttpClient client = new(new DelegateHandler(request =>
        {
            Interlocked.Increment(ref modelListRequests);
            throw new InvalidOperationException($"HTTP should not be called: {request.RequestUri}");
        }));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OpenRouterModelResolver.ResolveAsync(
            client,
            $"https://cancelled-{Guid.NewGuid():N}.openrouter.ai/api/v1",
            Defaults.DefaultAiModelId,
            cancellation.Token));

        Assert.Equal(0, modelListRequests);
    }

    [Fact]
    public async Task ResolveAsync_CancelsInFlightDiscoveryWhenCallerCancels()
    {
        int modelListRequests = 0;
        string endpointUrl = $"https://cancel-{Guid.NewGuid():N}.openrouter.ai/api/v1";
        using HttpClient client = new(new CancellableAsyncDelegateHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.ToString() == $"{endpointUrl}/models")
            {
                Interlocked.Increment(ref modelListRequests);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            throw new InvalidOperationException("Discovery request should be cancelled before returning.");
        }));
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OpenRouterModelResolver.ResolveAsync(
            client,
            endpointUrl,
            Defaults.DefaultAiModelId,
            cancellation.Token));

        Assert.Equal(1, modelListRequests);
    }



    [Fact]
    public void IsAutoModel_OnlyTreatsOpenRouterFreeOrAutoAsAuto()
    {
        Assert.True(OpenRouterModelResolver.IsAutoModel(Defaults.DefaultAiModelId));
        Assert.True(OpenRouterModelResolver.IsAutoModel("auto"));
        Assert.False(OpenRouterModelResolver.IsAutoModel(string.Empty));
        Assert.False(OpenRouterModelResolver.IsAutoModel(null!));
        Assert.False(OpenRouterModelResolver.IsAutoModel("meta-llama/llama-3.3-70b-instruct:free"));
    }

    [Fact]
    public void AddAttributionHeaders_NonOpenRouterEndpointDoesNotModifyRequest()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "https://ai.example.test/v1/chat/completions");

        OpenRouterModelResolver.AddAttributionHeaders(request, "https://ai.example.test/v1");

        Assert.False(request.Headers.Contains("HTTP-Referer"));
        Assert.False(request.Headers.Contains("X-OpenRouter-Title"));
    }

    [Fact]
    public void ListFreeInstructModels_MalformedOrEmptyPayloadReturnsEmpty()
    {
        using JsonDocument missingData = JsonDocument.Parse("""{"unexpected":[]}""");
        using JsonDocument nonArrayData = JsonDocument.Parse("""{"data":{}}""");

        Assert.Empty(OpenRouterModelResolver.ListFreeInstructModels(missingData.RootElement));
        Assert.Empty(OpenRouterModelResolver.ListFreeInstructModels(nonArrayData.RootElement));
    }

    [Fact]
    public void ListFreeInstructModels_ExcludesBaseModelsWithNoInstructType()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "data": [
                {
                  "id": "openrouter/small-base:free",
                  "name": "Small Base",
                  "context_length": 8192,
                  "pricing": { "prompt": "0", "completion": "0", "request": "0" },
                  "architecture": { "instruct_type": "none" }
                },
                {
                  "id": "meta-llama/llama-3.3-70b-instruct:free",
                  "name": "Llama 3.3 70B Instruct",
                  "context_length": 131072,
                  "pricing": { "prompt": "0", "completion": "0", "request": "0" },
                  "architecture": { "instruct_type": "chat" }
                }
              ]
            }
            """);

        IReadOnlyList<OpenRouterModelCandidate> candidates = OpenRouterModelResolver.ListFreeInstructModels(document.RootElement);

        Assert.DoesNotContain(candidates, candidate => candidate.Id == "openrouter/small-base:free");
        Assert.Contains(candidates, candidate => candidate.Id == "meta-llama/llama-3.3-70b-instruct:free");
    }

    [Fact]
    public void ListFreeInstructModels_IncludesNonDefaultReasoningAndSlugInstructModels()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "data": [
                {
                  "id": "vendor/plain-instruct-9b:free",
                  "name": "Plain 9B",
                  "context_length": 8192,
                  "pricing": { "prompt": "0", "completion": "0", "request": "0" }
                },
                {
                  "id": "vendor/chatty-13b:free",
                  "name": "Chatty 13B",
                  "context_length": 32768,
                  "pricing": { "prompt": "0", "completion": "0", "request": "0" },
                  "reasoning": { "mandatory": false, "default_enabled": false },
                  "architecture": { "instruct_type": "chat" }
                }
              ]
            }
            """);

        IReadOnlyList<OpenRouterModelCandidate> candidates = OpenRouterModelResolver.ListFreeInstructModels(document.RootElement);

        Assert.Contains(candidates, candidate => candidate.Id == "vendor/plain-instruct-9b:free");
        Assert.Contains(candidates, candidate => candidate.Id == "vendor/chatty-13b:free");
    }

    [Fact]
    public void ListFreeInstructModels_ClampsAbsurdParameterSizeScores()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "data": [
                {
                  "id": "vendor/overflow-999999999999999999999b-instruct:free",
                  "name": "Overflow Instruct",
                  "context_length": 8192,
                  "pricing": { "prompt": "0", "completion": "0", "request": "0" }
                }
              ]
            }
            """);

        OpenRouterModelCandidate candidate = Assert.Single(OpenRouterModelResolver.ListFreeInstructModels(document.RootElement));

        Assert.Equal(long.MaxValue, candidate.ParameterSizeScore);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackWhenModelsListHasNoSatisfyingCandidates()
    {
        string endpointUrl = $"https://empty-{Guid.NewGuid():N}.openrouter.ai/api/v1";
        using HttpClient client = new(new DelegateHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.ToString() == $"{endpointUrl}/models")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json")
                };
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        }));

        OpenRouterResolvedModel resolved = await OpenRouterModelResolver.ResolveAsync(
            client,
            endpointUrl,
            Defaults.DefaultAiModelId,
            CancellationToken.None);

        Assert.Equal(Defaults.DefaultAiModelId, resolved.ModelId);
        Assert.Equal("openrouter-auto-fallback", resolved.Resolution);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackWhenModelsEndpointReturnsNonSuccess()
    {
        string endpointUrl = $"https://unavailable-{Guid.NewGuid():N}.openrouter.ai/api/v1";
        using HttpClient client = new(new DelegateHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.ToString() == $"{endpointUrl}/models")
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        }));

        OpenRouterResolvedModel resolved = await OpenRouterModelResolver.ResolveAsync(
            client,
            endpointUrl,
            Defaults.DefaultAiModelId,
            CancellationToken.None);

        Assert.Equal(Defaults.DefaultAiModelId, resolved.ModelId);
        Assert.Equal("openrouter-auto-fallback", resolved.Resolution);
    }


    private static string CreateModelsJson()
        => $$"""
             {
               "data": [
                 {
                   "id": "reasoning/vendor-reasoner-700b:free",
                   "name": "Vendor Reasoner 700B",
                   "context_length": 32768,
                   "pricing": { "prompt": "0", "completion": "0", "request": "0" },
                   "reasoning": { "mandatory": true, "default_enabled": true },
                   "architecture": { "instruct_type": "chat" }
                 },
                 {
                   "id": "{{DiscoveredModelId}}",
                   "name": "Hermes 3 Llama 3.1 405B",
                   "context_length": 131072,
                   "pricing": { "prompt": "0", "completion": "0", "request": "0" },
                   "architecture": { "instruct_type": "chat" }
                 }
               ]
             }
             """;

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private sealed class AsyncDelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }

    private sealed class CancellableAsyncDelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request, cancellationToken);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException($"HTTP should not be called: {request.RequestUri}");
    }
}
