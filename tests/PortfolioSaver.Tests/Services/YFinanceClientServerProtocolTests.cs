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
using System.IO;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using PortfolioSaver.Shared.Services;
using YFinance.NET.Client;
using YFinance.NET.Protocol.Constants;
using YFinance.NET.Protocol.Dtos;
using YFinance.NET.Protocol.Integrity;
using YFinance.NET.Protocol.Messages;
using YFinance.NET.Protocol.Transport;
using YFinance.NET.Server.Hosting;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class YFinanceClientServerProtocolTests
{
    private static readonly SemaphoreSlim FaultInjectionTestGate = new(1, 1);

    [Fact]
    public void ProtocolIntegrity_StampsChecksumAndLocalOffsetTimestamp()
    {
        ProtocolRequest<GetQuoteRequestDto> request = new()
        {
            RequestId = "req-00000001",
            Operation = "get_quote",
            Payload = new GetQuoteRequestDto("^IXIC")
        };

        ProtocolIntegrity.Stamp(request, request.Payload);

        Assert.False(string.IsNullOrWhiteSpace(request.PayloadChecksum));
        Assert.Equal(16, request.PayloadChecksum.Length);
        Assert.Equal(DateTimeOffset.Now.Offset, request.Timestamp.Offset);
        Assert.True(ProtocolIntegrity.Verify(request, request.Payload));
        Assert.False(ProtocolIntegrity.Verify(request, new GetQuoteRequestDto("AAPL")));
    }

    [Fact]
    public void ProtocolIntegrity_VerifiesLegacySha256ChecksumForRollingCompatibility()
    {
        GetQuoteRequestDto payload = new("^IXIC");
        ProtocolRequest<GetQuoteRequestDto> request = new()
        {
            RequestId = "req-legacy-sha",
            Operation = "get_quote",
            Payload = payload,
            PayloadChecksum = Convert.ToHexString(SHA256.HashData(ProtocolJson.Serialize(payload)))
        };

        Assert.Equal(64, request.PayloadChecksum.Length);
        Assert.True(ProtocolIntegrity.Verify(request, request.Payload));
        Assert.False(ProtocolIntegrity.Verify(request, new GetQuoteRequestDto("AAPL")));
    }

    [Fact]
    public void ProtocolIntegrity_VerifyReturnsFalseForMissingChecksum()
    {
        ProtocolRequest<GetQuoteRequestDto> request = new()
        {
            RequestId = "req-missing-checksum",
            Operation = "get_quote",
            Payload = new GetQuoteRequestDto("^IXIC"),
            PayloadChecksum = " "
        };

        Assert.False(ProtocolIntegrity.Verify(request, request.Payload));

        request.PayloadChecksum = null!;
        Assert.False(ProtocolIntegrity.Verify(request, request.Payload));
    }

    [Fact]
    public void ProtocolIntegrity_VerifyReturnsFalseForMalformedChecksums()
    {
        ProtocolRequest<GetQuoteRequestDto> request = new()
        {
            RequestId = "req-malformed-checksum",
            Operation = "get_quote",
            Payload = new GetQuoteRequestDto("^IXIC"),
            PayloadChecksum = new string('Z', 64)
        };

        Assert.False(ProtocolIntegrity.Verify(request, request.Payload));

        request.PayloadChecksum = "NOTACHECKSUMBADD";
        Assert.False(ProtocolIntegrity.Verify(request, request.Payload));
    }

    [Fact]
    public void ProtocolJson_RoundTrip_PreservesTimestampAndChecksum()
    {
        ProtocolResponse<QuoteDto> response = new()
        {
            RequestId = "req-00000002",
            Operation = "get_quote",
            Payload = new QuoteDto(
                "AAPL",
                "Apple",
                "Apple Inc.",
                "Apple",
                "USD",
                "NMS",
                "America/New_York",
                "EDT",
                "EQUITY",
                "REGULAR",
                191.25m,
                190.02m,
                190.50m,
                191.40m,
                189.80m,
                1.23m,
                0.65m,
                3000000000000,
                123456789,
                DateTimeOffset.UtcNow,
                new CacheMetadataDto("live", 0, false))
        };
        ProtocolIntegrity.Stamp(response, response.Payload);

        byte[] json = ProtocolJson.Serialize(response);
        ProtocolResponse<JsonElement>? roundTrip = ProtocolJson.Deserialize<ProtocolResponse<JsonElement>>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(response.RequestId, roundTrip!.RequestId);
        Assert.Equal(response.Timestamp.Offset, roundTrip.Timestamp.Offset);
        Assert.Equal(response.PayloadChecksum, roundTrip.PayloadChecksum);
        Assert.True(ProtocolIntegrity.Verify(roundTrip, roundTrip.Payload));
    }

    [Fact]
    public async Task LengthPrefixedProtocolStream_RoundTripsStampedRequest()
    {
        ProtocolRequest<GetQuotesRequestDto> request = new()
        {
            RequestId = "req-00000003",
            Operation = "get_quotes",
            Payload = new GetQuotesRequestDto(["VOO", "IJH"])
        };
        ProtocolIntegrity.Stamp(request, request.Payload);

        byte[] bytes = ProtocolJson.Serialize(request);
        await using MemoryStream stream = new();
        await LengthPrefixedProtocolStream.WriteAsync(stream, bytes);
        stream.Position = 0;

        byte[]? received = await LengthPrefixedProtocolStream.ReadAsync(stream);
        Assert.NotNull(received);

        ProtocolRequest<JsonElement>? roundTrip = ProtocolJson.Deserialize<ProtocolRequest<JsonElement>>(received);
        Assert.NotNull(roundTrip);
        Assert.Equal(request.PayloadChecksum, roundTrip!.PayloadChecksum);
        Assert.True(ProtocolIntegrity.Verify(roundTrip, roundTrip.Payload));
    }

    [Fact]
    public async Task LengthPrefixedProtocolStream_ReadPooled_RoundTripsAndDisposesPayload()
    {
        ProtocolRequest<GetQuotesRequestDto> request = new()
        {
            RequestId = "req-pooled-0001",
            Operation = "get_quotes",
            Payload = new GetQuotesRequestDto(["VOO", "IJH"])
        };
        ProtocolIntegrity.Stamp(request, request.Payload);

        byte[] bytes = ProtocolJson.Serialize(request);
        await using MemoryStream stream = new();
        await LengthPrefixedProtocolStream.WriteAsync(stream, bytes);
        stream.Position = 0;

        PooledProtocolPayload? payload = await LengthPrefixedProtocolStream.ReadPooledAsync(stream);

        Assert.NotNull(payload);
        using (payload)
        {
            ProtocolRequest<JsonElement>? roundTrip = ProtocolJson.Deserialize<ProtocolRequest<JsonElement>>(payload!.Memory.Span);
            Assert.NotNull(roundTrip);
            Assert.Equal(request.PayloadChecksum, roundTrip!.PayloadChecksum);
            Assert.True(ProtocolIntegrity.Verify(roundTrip, roundTrip.Payload));
        }

        Assert.Throws<ObjectDisposedException>(() => _ = payload!.Memory);
    }

    [Fact]
    public async Task LengthPrefixedProtocolStream_ReadPooled_ReturnsNullForEmptyStream()
    {
        await using MemoryStream stream = new();

        PooledProtocolPayload? payload = await LengthPrefixedProtocolStream.ReadPooledAsync(stream);

        Assert.Null(payload);
    }

    [Fact]
    public async Task LengthPrefixedProtocolStream_ReadPooled_ThrowsForTruncatedPayload()
    {
        await using MemoryStream stream = CreateFramedStream(length: 8, "short"u8.ToArray());

        await Assert.ThrowsAsync<EndOfStreamException>(async () => await LengthPrefixedProtocolStream.ReadPooledAsync(stream));
    }

    [Theory]
    [InlineData(-1)]
    public async Task LengthPrefixedProtocolStream_ReadPooled_ThrowsForInvalidLength(int length)
    {
        await using MemoryStream stream = CreateFramedStream(length);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await LengthPrefixedProtocolStream.ReadPooledAsync(stream));
    }

    [Fact]
    public async Task LengthPrefixedProtocolStream_ReadAsync_AllowsZeroLength()
    {
        await using MemoryStream stream = CreateFramedStream(length: 0);

        byte[]? payload = await LengthPrefixedProtocolStream.ReadAsync(stream);

        Assert.NotNull(payload);
        Assert.Empty(payload);
    }

    [Fact]
    public async Task LengthPrefixedProtocolStream_ReadPooled_AllowsZeroLength()
    {
        await using MemoryStream stream = CreateFramedStream(length: 0);

        PooledProtocolPayload? payload = await LengthPrefixedProtocolStream.ReadPooledAsync(stream);

        Assert.NotNull(payload);
        using (payload)
        {
            Assert.True(payload!.Memory.IsEmpty);
        }
    }

    [Fact]
    public async Task LengthPrefixedProtocolStream_ReadPooled_ThrowsForOversizedLength()
    {
        await using MemoryStream stream = CreateFramedStream(ProtocolConstants.MaxMessageBytes + 1);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await LengthPrefixedProtocolStream.ReadPooledAsync(stream));
    }

    [Fact]
    public async Task LengthPrefixedProtocolStream_ReadPooled_DisposeIsIdempotent()
    {
        await using MemoryStream stream = CreateFramedStream(length: 5, "hello"u8.ToArray());
        PooledProtocolPayload? payload = await LengthPrefixedProtocolStream.ReadPooledAsync(stream);

        Assert.NotNull(payload);
        payload!.Dispose();
        payload.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = payload.Memory);
    }

    [Fact]
    public void OwnedModeStartup_IsWiredIntoInteractiveApps()
    {
        string repoRoot = GetRepoRoot();
        string desktopApp = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Desktop", "App.xaml.cs"));
        string configApp = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Config", "App.xaml.cs"));
        string shutdownQueue = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Shared", "Services", "OwnedServerShutdownQueue.cs"));

        Assert.Contains("YFinanceServerProcessManager.EnsureOwnedServerAsync(\"PortfolioSaver.Desktop\")", desktopApp, StringComparison.Ordinal);
        Assert.Contains("OwnedServerShutdownQueue.QueueShutdown(\"Desktop.App\")", desktopApp, StringComparison.Ordinal);
        Assert.DoesNotContain("StopOwnedServerAsync().GetAwaiter().GetResult()", desktopApp, StringComparison.Ordinal);
        Assert.Contains("YFinanceServerProcessManager.EnsureOwnedServerAsync(\"PortfolioSaver.Config\")", configApp, StringComparison.Ordinal);
        Assert.Contains("OwnedServerShutdownQueue.QueueShutdown(\"Config.App\")", configApp, StringComparison.Ordinal);
        Assert.DoesNotContain("StopOwnedServerAsync().GetAwaiter().GetResult()", configApp, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(repoRoot, "src", "PortfolioSaver.Screensaver")));
        Assert.Contains("YFinanceServerProcessManager.StopOwnedServerAsync(timeout.Token)", shutdownQueue, StringComparison.Ordinal);
        Assert.Contains("IsBackground = false", shutdownQueue, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", shutdownQueue, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientAndServer_TraceEveryMessageAtTransportBoundary()
    {
        string repoRoot = GetRepoRoot();
        string clientSource = File.ReadAllText(Path.Combine(repoRoot, "YFinance.net", "YFinance.NET.Client", "YFinanceServerClient.cs"));
        string serverSource = File.ReadAllText(Path.Combine(repoRoot, "YFinance.net", "YFinance.NET.Server", "Hosting", "YFinanceServerProgram.cs"));

        Assert.Contains("ClientRequestSend", clientSource, StringComparison.Ordinal);
        Assert.Contains("ClientResponseReceive", clientSource, StringComparison.Ordinal);
        Assert.Contains("ClientEventReceive", clientSource, StringComparison.Ordinal);
        Assert.Contains("RequestReceived", serverSource, StringComparison.Ordinal);
        Assert.Contains("ResponseSent", serverSource, StringComparison.Ordinal);
        Assert.Contains("RequestIntegrityRejected", serverSource, StringComparison.Ordinal);
        Assert.Contains("QuoteResponseObserved", serverSource, StringComparison.Ordinal);
        Assert.Contains("new(\"price\"", serverSource, StringComparison.Ordinal);
        Assert.Contains("new(\"change_percent\"", serverSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PooledTransport_ClonesJsonPayloadsBeforeAsyncEscape()
    {
        string repoRoot = GetRepoRoot();
        string clientSource = File.ReadAllText(Path.Combine(repoRoot, "YFinance.net", "YFinance.NET.Client", "YFinanceServerClient.cs"));
        string serverSource = File.ReadAllText(Path.Combine(repoRoot, "YFinance.net", "YFinance.NET.Server", "Hosting", "YFinanceServerProgram.cs"));

        // These source guards pin the exact async escape boundaries that can
        // reintroduce pooled-buffer lifetime bugs if refactored carelessly.
        Assert.Contains("pending.TrySetPayload(response.Payload.Clone())", clientSource, StringComparison.Ordinal);
        Assert.Contains("protocolEvent = protocolEvent with { Payload = protocolEvent.Payload.Clone() }", clientSource, StringComparison.Ordinal);
        Assert.Contains("dispatchRequest = request with { Payload = request.Payload.Clone() }", serverSource, StringComparison.Ordinal);
        Assert.Contains("capturedRequest = dispatchRequest", serverSource, StringComparison.Ordinal);
        Assert.Contains("DispatchAsync(capturedRequest", serverSource, StringComparison.Ordinal);
        Assert.Contains("WriteResponseAsync(stream, writeGate, response, remote, capturedRequest", serverSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerOptions_DefaultsToLoopbackBinding()
    {
        ServerOptions options = ServerOptions.Parse([]);

        Assert.Equal(IPAddress.Loopback, options.BindAddress);
        Assert.True(options.EnableUpstreamSyncCheck);
        Assert.Equal(8, options.MaxConcurrentRequestsPerClient);
        Assert.Equal(ServerOptions.DefaultClientIdleTimeout, options.ClientIdleTimeout);
    }

    [Fact]
    public void ServerOptions_CanConfigurePerClientRequestLimit()
    {
        ServerOptions options = ServerOptions.Parse(["--max-requests-per-client", "3"]);
        ServerOptions clamped = ServerOptions.Parse(["--max-requests-per-client", "0"]);

        Assert.Equal(3, options.MaxConcurrentRequestsPerClient);
        Assert.Equal(1, clamped.MaxConcurrentRequestsPerClient);
    }

    [Fact]
    public void ServerOptions_CanConfigureClientIdleTimeout()
    {
        ServerOptions options = ServerOptions.Parse(["--client-idle-timeout-seconds", "2"]);
        ServerOptions clamped = ServerOptions.Parse(["--client-idle-timeout-seconds", "0"]);

        Assert.Equal(TimeSpan.FromSeconds(2), options.ClientIdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), clamped.ClientIdleTimeout);
    }

    [Fact]
    public void ServerOptions_NegativeClientIdleTimeoutIsClamped()
    {
        ServerOptions options = ServerOptions.Parse(["--client-idle-timeout-seconds", "-5"]);

        Assert.Equal(TimeSpan.FromSeconds(1), options.ClientIdleTimeout);
    }

    [Fact]
    public void ServerOptions_LargeClientIdleTimeoutIsClamped()
    {
        ServerOptions options = ServerOptions.Parse(["--client-idle-timeout-seconds", int.MaxValue.ToString()]);

        Assert.Equal(ServerOptions.MaxClientIdleTimeout, options.ClientIdleTimeout);
    }

    [Fact]
    public void ServerOptions_CanDisableUpstreamSyncCheck()
    {
        ServerOptions options = ServerOptions.Parse(["--no-upstream-sync"]);

        Assert.False(options.EnableUpstreamSyncCheck);
    }

    [Fact]
    public async Task ServerFaultInjection_ProfileFileCanForceMarketDataOfflineResponse()
    {
        await FaultInjectionTestGate.WaitAsync();
        using TempDirectory temp = TempDirectory.Create();
        string profilePath = Path.Combine(temp.Path, "fault-profile.json");
        await File.WriteAllTextAsync(profilePath, """
            {
              "profile": "offline",
              "operations": [ "market-data" ]
            }
            """);

        string? previousPath = Environment.GetEnvironmentVariable(YFinanceServerFaultInjection.ProfilePathEnvironmentVariable);
        string? previousProfile = Environment.GetEnvironmentVariable(YFinanceServerFaultInjection.ProfileEnvironmentVariable);
        try
        {
            YFinanceServerFaultInjection.ResetCacheForTests();
            Environment.SetEnvironmentVariable(YFinanceServerFaultInjection.ProfilePathEnvironmentVariable, profilePath);
            Environment.SetEnvironmentVariable(YFinanceServerFaultInjection.ProfileEnvironmentVariable, null);

            ProtocolRequest<JsonElement> request = new()
            {
                RequestId = "req-fault-0001",
                Operation = YFinance.NET.Protocol.Constants.ProtocolOperations.GetQuote,
                Payload = JsonSerializer.SerializeToElement(new GetQuoteRequestDto("VOO"))
            };

            ProtocolResponse<EmptyPayload>? response = await YFinanceServerFaultInjection.TryApplyAsync(request, CancellationToken.None);

            Assert.NotNull(response);
            Assert.Equal(YFinance.NET.Protocol.Constants.ProtocolResponseStatuses.Error, response!.Status);
            Assert.Equal(YFinance.NET.Protocol.Constants.ProtocolErrorCodes.NetworkLost, response.Error?.Code);
            Assert.True(response.Error?.Retryable);
            Assert.True(ProtocolIntegrity.Verify(response, response.Payload));
        }
        finally
        {
            Environment.SetEnvironmentVariable(YFinanceServerFaultInjection.ProfilePathEnvironmentVariable, previousPath);
            Environment.SetEnvironmentVariable(YFinanceServerFaultInjection.ProfileEnvironmentVariable, previousProfile);
            YFinanceServerFaultInjection.ResetCacheForTests();
            FaultInjectionTestGate.Release();
        }
    }

    [Fact]
    public async Task ServerFaultInjection_DoesNotFaultHealthRequests()
    {
        await FaultInjectionTestGate.WaitAsync();
        string? previousPath = Environment.GetEnvironmentVariable(YFinanceServerFaultInjection.ProfilePathEnvironmentVariable);
        string? previousProfile = Environment.GetEnvironmentVariable(YFinanceServerFaultInjection.ProfileEnvironmentVariable);
        try
        {
            YFinanceServerFaultInjection.ResetCacheForTests();
            Environment.SetEnvironmentVariable(YFinanceServerFaultInjection.ProfilePathEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(YFinanceServerFaultInjection.ProfileEnvironmentVariable, "offline");

            ProtocolRequest<JsonElement> request = new()
            {
                RequestId = "req-fault-0002",
                Operation = YFinance.NET.Protocol.Constants.ProtocolOperations.Health,
                Payload = JsonSerializer.SerializeToElement(new EmptyPayload())
            };

            ProtocolResponse<EmptyPayload>? response = await YFinanceServerFaultInjection.TryApplyAsync(request, CancellationToken.None);

            Assert.Null(response);
        }
        finally
        {
            Environment.SetEnvironmentVariable(YFinanceServerFaultInjection.ProfilePathEnvironmentVariable, previousPath);
            Environment.SetEnvironmentVariable(YFinanceServerFaultInjection.ProfileEnvironmentVariable, previousProfile);
            YFinanceServerFaultInjection.ResetCacheForTests();
            FaultInjectionTestGate.Release();
        }
    }

    [Fact]
    public async Task ServerFaultInjection_DelayOnlyProfileReturnsNoSyntheticError()
    {
        await FaultInjectionTestGate.WaitAsync();
        using TempDirectory temp = TempDirectory.Create();
        string profilePath = Path.Combine(temp.Path, "fault-profile.json");
        await File.WriteAllTextAsync(profilePath, """
            {
              "profile": "high-latency-yfinance",
              "delayMilliseconds": 25,
              "operations": [ "market-data" ]
            }
            """);

        string? previousPath = Environment.GetEnvironmentVariable(YFinanceServerFaultInjection.ProfilePathEnvironmentVariable);
        string? previousProfile = Environment.GetEnvironmentVariable(YFinanceServerFaultInjection.ProfileEnvironmentVariable);
        try
        {
            YFinanceServerFaultInjection.ResetCacheForTests();
            Environment.SetEnvironmentVariable(YFinanceServerFaultInjection.ProfilePathEnvironmentVariable, profilePath);
            Environment.SetEnvironmentVariable(YFinanceServerFaultInjection.ProfileEnvironmentVariable, null);

            ProtocolRequest<JsonElement> request = new()
            {
                RequestId = "req-fault-0003",
                Operation = YFinance.NET.Protocol.Constants.ProtocolOperations.GetQuote,
                Payload = JsonSerializer.SerializeToElement(new GetQuoteRequestDto("VOO"))
            };
            Stopwatch stopwatch = Stopwatch.StartNew();

            ProtocolResponse<EmptyPayload>? response = await YFinanceServerFaultInjection.TryApplyAsync(request, CancellationToken.None);

            stopwatch.Stop();
            Assert.Null(response);
            Assert.True(stopwatch.ElapsedMilliseconds >= 20);
        }
        finally
        {
            Environment.SetEnvironmentVariable(YFinanceServerFaultInjection.ProfilePathEnvironmentVariable, previousPath);
            Environment.SetEnvironmentVariable(YFinanceServerFaultInjection.ProfileEnvironmentVariable, previousProfile);
            YFinanceServerFaultInjection.ResetCacheForTests();
            FaultInjectionTestGate.Release();
        }
    }

    [Fact]
    public void ServerOptions_RequiresExplicitOptInForRemoteBinding()
    {
        ServerOptions explicitAddress = ServerOptions.Parse(["--bind-address", "0.0.0.0"]);
        ServerOptions allowRemote = ServerOptions.Parse(["--allow-remote"]);
        ServerOptions explicitAddressWins = ServerOptions.Parse(["--allow-remote", "--bind-address", "127.0.0.1"]);
        ServerOptions explicitAddressStillWins = ServerOptions.Parse(["--bind-address", "0.0.0.0", "--allow-remote"]);
        ServerOptions ipv6Any = ServerOptions.Parse(["--bind-address", "::"]);
        ServerOptions ipv6Loopback = ServerOptions.Parse(["--bind-address", "::1"]);

        Assert.Equal(IPAddress.Any, explicitAddress.BindAddress);
        Assert.Equal(IPAddress.Any, allowRemote.BindAddress);
        Assert.Equal(IPAddress.Loopback, explicitAddressWins.BindAddress);
        Assert.Equal(IPAddress.Any, explicitAddressStillWins.BindAddress);
        Assert.Equal(IPAddress.IPv6Any, ipv6Any.BindAddress);
        Assert.Equal(IPAddress.IPv6Loopback, ipv6Loopback.BindAddress);
    }

    [Fact]
    public void ServerOptions_RejectsInvalidBindAddress()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => ServerOptions.Parse(["--bind-address", "not-an-ip"]));

        Assert.Contains("--bind-address requires a valid IP address", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerOptions_RejectsRemoteBindingInOwnedMode()
    {
        ArgumentException allowRemoteEx = Assert.Throws<ArgumentException>(() => ServerOptions.Parse(["--owned", "--allow-remote"]));
        ArgumentException bindAddressEx = Assert.Throws<ArgumentException>(() => ServerOptions.Parse(["--owned", "--bind-address", "0.0.0.0"]));

        Assert.Contains("Owned mode requires a loopback bind address", allowRemoteEx.Message, StringComparison.Ordinal);
        Assert.Contains("Owned mode requires a loopback bind address", bindAddressEx.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishAndLauncher_StageAndDiscoverSiblingServerBundle()
    {
        string repoRoot = GetRepoRoot();
        string publishScript = File.ReadAllText(Path.Combine(repoRoot, "build", "publish-safe-temp.ps1"));
        string launcherSource = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Shared", "Services", "YFinanceServerProcessManager.cs"));

        Assert.Contains("$serverOut = Join-Path $publishRoot \"server\"", publishScript, StringComparison.Ordinal);
        Assert.Contains("$serverProject = \".\\YFinance.net\\YFinance.NET.Server\\YFinance.NET.Server.csproj\"", publishScript, StringComparison.Ordinal);
        Assert.Contains("$serverTempPublish = \".\\YFinance.net\\YFinance.NET.Server\\bin\\$Configuration\\net10.0\\publish\"", publishScript, StringComparison.Ordinal);
        Assert.Contains("& $manifestScript -PublishDir $serverOut", publishScript, StringComparison.Ordinal);
        Assert.Contains("Manifest generation failed for $serverOut", publishScript, StringComparison.Ordinal);
        string desktopProject = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Desktop", "PortfolioSaver.Desktop.csproj"));
        string configProject = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Config", "PortfolioSaver.Config.csproj"));
        string serverTargets = File.ReadAllText(Path.Combine(repoRoot, "build", "YFinanceServer.targets"));

        Assert.Contains("../../build/YFinanceServer.targets", desktopProject, StringComparison.Ordinal);
        Assert.Contains("../../build/YFinanceServer.targets", configProject, StringComparison.Ordinal);
        Assert.Contains("CopyOwnedYFinanceServerToOutput", serverTargets, StringComparison.Ordinal);
        Assert.Contains("CopyOwnedYFinanceServerToPublish", serverTargets, StringComparison.Ordinal);
        Assert.Contains("<OwnedYFinanceServerBundleFolder>YFinanceServer\\</OwnedYFinanceServerBundleFolder>", serverTargets, StringComparison.Ordinal);
        Assert.Contains("$(OwnedYFinanceServerOutput)**\\*", serverTargets, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(baseDirectory, \"YFinanceServer\", \"YFinance.NET.Server.exe\")", launcherSource, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(baseDirectory, \"YFinanceServer\", \"YFinance.NET.Server.dll\")", launcherSource, StringComparison.Ordinal);
        Assert.Contains("ServerStartupTimeout = TimeSpan.FromSeconds(60)", launcherSource, StringComparison.Ordinal);
        Assert.Contains("ServerStartupPollInterval = TimeSpan.FromMilliseconds(250)", launcherSource, StringComparison.Ordinal);
        Assert.Contains("Stopwatch startupStopwatch = Stopwatch.StartNew();", launcherSource, StringComparison.Ordinal);
        Assert.Contains("while (startupStopwatch.Elapsed < ServerStartupTimeout)", launcherSource, StringComparison.Ordinal);
        Assert.Contains("remaining < ServerStartupPollInterval ? remaining : ServerStartupPollInterval", launcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRepoRoot()", launcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PORTFOLIOSAVER_YFINANCE_SERVER_PATH", launcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("string siblingRepo = Path.Combine(current, \"repo\")", launcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("YFinance.NET.Server\", \"bin\", \"Release\"", launcherSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerProcessManager_AndServerProgram_DefendAgainstDuplicateOwnedServerLaunches()
    {
        string repoRoot = GetRepoRoot();
        string launcherSource = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Shared", "Services", "YFinanceServerProcessManager.cs"));
        string serverSource = File.ReadAllText(Path.Combine(repoRoot, "YFinance.net", "YFinance.NET.Server", "Hosting", "YFinanceServerProgram.cs"));

        Assert.Contains("OwnedServerAlreadyRunning", launcherSource, StringComparison.Ordinal);
        Assert.Contains("ServerAlreadyReachable", launcherSource, StringComparison.Ordinal);
        Assert.Contains("CanConnectAsync(cancellationToken)", launcherSource, StringComparison.Ordinal);
        Assert.Contains("ProtocolConstants.GetMutexName(options.Port)", serverSource, StringComparison.Ordinal);
        Assert.Contains("DuplicateServerStartRejected", serverSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerProgram_ClassifiesExpectedPendingReadShutdownAsInformational()
    {
        string serverSource = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "YFinance.net",
            "YFinance.NET.Server",
            "Hosting",
            "YFinanceServerProgram.cs"));

        Assert.Contains("ex is OperationCanceledException or ObjectDisposedException or IOException", serverSource, StringComparison.Ordinal);
        Assert.Contains("\"PendingReadDrainEnded\"", serverSource, StringComparison.Ordinal);
        Assert.Contains("\"connection-closing\"", serverSource, StringComparison.Ordinal);
        Assert.Contains("\"PendingReadDrainFailed\"", serverSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerProgram_AwaitsActiveClientHandlersOnShutdown()
    {
        object gate = new();
        TaskCompletionSource releaseHandler = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task handler = releaseHandler.Task;
        List<Task> handlers = [handler];

        Task<bool> drainTask = YFinanceServerProgram.AwaitClientHandlersAsync(handlers, gate, TimeSpan.FromSeconds(1));
        Task firstCompleted = await Task.WhenAny(drainTask, Task.Delay(150));

        Assert.NotSame(drainTask, firstCompleted);

        releaseHandler.SetResult();
        Assert.True(await drainTask.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ServerProgram_ClientHandlerDrainReturnsFalseOnTimeout()
    {
        object gate = new();
        TaskCompletionSource blockedHandler = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<Task> handlers = [blockedHandler.Task];

        bool drained = await YFinanceServerProgram.AwaitClientHandlersAsync(handlers, gate, TimeSpan.FromMilliseconds(25));

        Assert.False(drained);
        blockedHandler.SetResult();
    }

    [Fact]
    public async Task ServerProgram_ClientHandlerDrainReturnsTrueOnFaultedHandler()
    {
        object gate = new();
        List<Task> handlers = [Task.FromException(new InvalidOperationException("handler failed"))];

        bool drained = await YFinanceServerProgram.AwaitClientHandlersAsync(handlers, gate, TimeSpan.FromSeconds(1));

        Assert.True(drained);
    }

    [Fact]
    public void ServerProcessManager_ResolvesOnlyDeploymentRelativeServerBinary()
    {
        using TempDirectory temp = TempDirectory.Create();
        string appDirectory = Path.Combine(temp.Path, "app");
        string siblingServerDirectory = Path.Combine(appDirectory, "YFinanceServer");
        string untrustedParentDirectory = Path.Combine(temp.Path, "YFinance.net", "YFinance.NET.Server", "bin", "Release", "net10.0");
        Directory.CreateDirectory(appDirectory);
        Directory.CreateDirectory(siblingServerDirectory);
        Directory.CreateDirectory(untrustedParentDirectory);

        string trustedServer = Path.Combine(siblingServerDirectory, "YFinance.NET.Server.exe");
        string untrustedServer = Path.Combine(untrustedParentDirectory, "YFinance.NET.Server.exe");
        File.WriteAllText(trustedServer, string.Empty);
        File.WriteAllText(untrustedServer, string.Empty);

        (string fileName, string arguments, string traceArguments) =
            YFinanceServerProcessManager.ResolveLaunchCommand("test-token", appDirectory);

        Assert.Equal(trustedServer, fileName);
        Assert.Contains("--launch-token \"test-token\"", arguments, StringComparison.Ordinal);
        Assert.Contains("--launch-token \"<redacted>\"", traceArguments, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerProcessManager_IgnoresLegacyRepoBuildOutputWhenDeploymentBundleMissing()
    {
        using TempDirectory temp = TempDirectory.Create();
        string appDirectory = Path.Combine(temp.Path, "app");
        string untrustedParentDirectory = Path.Combine(temp.Path, "YFinance.net", "YFinance.NET.Server", "bin", "Release", "net10.0");
        Directory.CreateDirectory(appDirectory);
        Directory.CreateDirectory(untrustedParentDirectory);
        File.WriteAllText(Path.Combine(untrustedParentDirectory, "YFinance.NET.Server.exe"), string.Empty);

        Assert.Throws<FileNotFoundException>(() =>
            YFinanceServerProcessManager.ResolveLaunchCommand("test-token", appDirectory));
    }

    [Fact]
    public void ServerProcessManager_ResolvesSiblingPublishServerBundle()
    {
        using TempDirectory temp = TempDirectory.Create();
        string appDirectory = Path.Combine(temp.Path, "publish", "app");
        string siblingServerDirectory = Path.Combine(temp.Path, "publish", "server");
        Directory.CreateDirectory(appDirectory);
        Directory.CreateDirectory(siblingServerDirectory);

        string trustedServer = Path.Combine(siblingServerDirectory, "YFinance.NET.Server.exe");
        File.WriteAllText(trustedServer, string.Empty);

        (string fileName, _, _) =
            YFinanceServerProcessManager.ResolveLaunchCommand("test-token", appDirectory);

        Assert.Equal(trustedServer, fileName);
    }

    [Fact]
    public void ServerProcessManager_UsesDotnetForDllOnlyBundle()
    {
        using TempDirectory temp = TempDirectory.Create();
        string appDirectory = Path.Combine(temp.Path, "app");
        string serverDirectory = Path.Combine(appDirectory, "YFinanceServer");
        Directory.CreateDirectory(serverDirectory);

        string trustedServer = Path.Combine(serverDirectory, "YFinance.NET.Server.dll");
        File.WriteAllText(trustedServer, string.Empty);

        (string fileName, string arguments, string traceArguments) =
            YFinanceServerProcessManager.ResolveLaunchCommand("test-token", appDirectory);

        Assert.Equal("dotnet", fileName);
        Assert.Contains($"\"{trustedServer}\"", arguments, StringComparison.Ordinal);
        Assert.Contains("--launch-token \"test-token\"", arguments, StringComparison.Ordinal);
        Assert.Contains("--launch-token \"<redacted>\"", traceArguments, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerProcessManager_PrefersExeOverDllInSameBundle()
    {
        using TempDirectory temp = TempDirectory.Create();
        string appDirectory = Path.Combine(temp.Path, "app");
        string serverDirectory = Path.Combine(appDirectory, "YFinanceServer");
        Directory.CreateDirectory(serverDirectory);

        string trustedExe = Path.Combine(serverDirectory, "YFinance.NET.Server.exe");
        File.WriteAllText(trustedExe, string.Empty);
        File.WriteAllText(Path.Combine(serverDirectory, "YFinance.NET.Server.dll"), string.Empty);

        (string fileName, _, _) =
            YFinanceServerProcessManager.ResolveLaunchCommand("test-token", appDirectory);

        Assert.Equal(trustedExe, fileName);
    }

    [Fact]
    public void ServerProcessManager_InvalidatesCachedLaunchCommandWhenBinaryDisappears()
    {
        using TempDirectory temp = TempDirectory.Create();
        string appDirectory = Path.Combine(temp.Path, "app");
        string serverDirectory = Path.Combine(appDirectory, "YFinanceServer");
        Directory.CreateDirectory(serverDirectory);

        string trustedExe = Path.Combine(serverDirectory, "YFinance.NET.Server.exe");
        string trustedDll = Path.Combine(serverDirectory, "YFinance.NET.Server.dll");
        File.WriteAllText(trustedExe, string.Empty);

        (string firstFileName, _, _) =
            YFinanceServerProcessManager.ResolveLaunchCommand("first-token", appDirectory);
        File.Delete(trustedExe);
        File.WriteAllText(trustedDll, string.Empty);
        (string secondFileName, string secondArguments, string secondTraceArguments) =
            YFinanceServerProcessManager.ResolveLaunchCommand("second-token", appDirectory);

        Assert.Equal(trustedExe, firstFileName);
        Assert.Equal("dotnet", secondFileName);
        Assert.Contains($"\"{trustedDll}\"", secondArguments, StringComparison.Ordinal);
        Assert.Contains("--launch-token \"second-token\"", secondArguments, StringComparison.Ordinal);
        Assert.Contains("--launch-token \"<redacted>\"", secondTraceArguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerProcess_RespondsToHealthRequest_OverTcpProtocol()
    {
        (Process server, int port) = await StartReachableServerProcessAsync();
        using (server)
        try
        {
            await using YFinanceServerClient client = new(new YFinanceServerConnectionOptions("127.0.0.1", port, TimeSpan.FromSeconds(5), NullYFinanceServerClientTraceSink.Instance));
            HelloResponseDto hello = await client.HelloAsync(new HelloRequestDto("PortfolioSaver.Tests", "1.0", "TESTHASH", false, Environment.ProcessId));
            HealthResponseDto health = await client.HealthAsync();
            HealthResponseDto secondHealth = await client.HealthAsync();

            Assert.Equal(port, hello.ListenerPort);
            Assert.Equal("standalone", hello.Mode);
            Assert.Equal("ok", health.Status);
            Assert.Equal("ok", secondHealth.Status);
            Assert.True(secondHealth.UptimeSeconds >= health.UptimeSeconds);
        }
        finally
        {
            KillProcessIfRunning(server);
        }
    }

    [Fact]
    public async Task DuplicateServerGuard_IsScopedPerPort_NotGlobal()
    {
        (Process serverA, int portA) = await StartReachableServerProcessAsync();
        (Process serverB, int portB) = await StartReachableServerProcessAsync();
        using (serverA)
        using (serverB)
        try
        {
            await using YFinanceServerClient clientA = new(new YFinanceServerConnectionOptions("127.0.0.1", portA, TimeSpan.FromSeconds(5), NullYFinanceServerClientTraceSink.Instance));
            await using YFinanceServerClient clientB = new(new YFinanceServerConnectionOptions("127.0.0.1", portB, TimeSpan.FromSeconds(5), NullYFinanceServerClientTraceSink.Instance));

            HealthResponseDto healthA = await clientA.HealthAsync();
            HealthResponseDto healthB = await clientB.HealthAsync();

            Assert.Equal("ok", healthA.Status);
            Assert.Equal("ok", healthB.Status);
        }
        finally
        {
            KillProcessIfRunning(serverA);
            KillProcessIfRunning(serverB);
        }
    }

    [Fact]
    public async Task DuplicateServerGuard_SecondProcessExitsCleanlyWithoutReplacingPrimary()
    {
        (Process primary, int port) = await StartReachableServerProcessAsync();
        Process? duplicate = null;
        using (primary)
        {
            try
            {
                duplicate = StartServerProcess(port);

                Assert.True(await WaitForExitAsync(duplicate, TimeSpan.FromSeconds(10)));
                // Duplicate launch is a no-op guard, not a fatal bind failure: the
                // second process logs DuplicateServerStartRejected and leaves the
                // already-serving primary process untouched.
                Assert.Equal(0, duplicate.ExitCode);

                await using YFinanceServerClient client = new(new YFinanceServerConnectionOptions("127.0.0.1", port, TimeSpan.FromSeconds(5), NullYFinanceServerClientTraceSink.Instance));
                HealthResponseDto health = await client.HealthAsync();
                Assert.Equal("ok", health.Status);
            }
            finally
            {
                if (duplicate is not null)
                    KillProcessIfRunning(duplicate);
                KillProcessIfRunning(primary);
                duplicate?.Dispose();
            }
        }
    }

    [Fact]
    public async Task ServerProcess_ReturnsFatalExitWhenPortIsOccupiedByNonServer()
    {
        int port = GetAvailablePort();
        using TcpListener blocker = new(IPAddress.Loopback, port);
        blocker.Start();
        using Process server = StartServerProcess(port);

        Assert.True(await WaitForExitAsync(server, TimeSpan.FromSeconds(10)));
        Assert.NotEqual(0, server.ExitCode);
    }

    [Fact]
    public async Task ServerProcess_DisconnectsIdleClientAndAllowsReconnect()
    {
        (Process server, int port) = await StartReachableServerProcessAsync("--client-idle-timeout-seconds 5");
        using (server)
        try
        {
            using TcpClient idleClient = new();
            await idleClient.ConnectAsync(IPAddress.Loopback, port);
            NetworkStream idleStream = idleClient.GetStream();
            byte[] buffer = new byte[1];

            int read = await idleStream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(0, read);
            await WaitForPortAsync(port, TimeSpan.FromSeconds(10));
            Assert.False(server.HasExited);

            await using YFinanceServerClient client = new(new YFinanceServerConnectionOptions("127.0.0.1", port, TimeSpan.FromSeconds(5), NullYFinanceServerClientTraceSink.Instance));
            HealthResponseDto health = await client.HealthAsync();
            Assert.Equal("ok", health.Status);
        }
        finally
        {
            KillProcessIfRunning(server);
        }
    }

    [Fact]
    public async Task ServerProcess_DisconnectsClientThatIdlesAfterRequest()
    {
        (Process server, int port) = await StartReachableServerProcessAsync("--client-idle-timeout-seconds 5");
        using (server)
        try
        {
            using TcpClient rawClient = new();
            await rawClient.ConnectAsync(IPAddress.Loopback, port);
            await using NetworkStream stream = rawClient.GetStream();

            ProtocolRequest<HelloRequestDto> hello = new()
            {
                RequestId = "req-idle-after-request",
                Operation = ProtocolOperations.Hello,
                Payload = new HelloRequestDto("PortfolioSaver.Tests", "1.0", "TESTHASH", false, Environment.ProcessId)
            };
            ProtocolIntegrity.Stamp(hello, hello.Payload);
            await LengthPrefixedProtocolStream.WriteAsync(stream, ProtocolJson.Serialize(hello));

            byte[]? responseBytes = await LengthPrefixedProtocolStream.ReadAsync(stream).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(responseBytes);
            ProtocolResponse<JsonElement>? response = ProtocolJson.Deserialize<ProtocolResponse<JsonElement>>(responseBytes);
            Assert.NotNull(response);
            Assert.Equal(ProtocolResponseStatuses.Ok, response!.Status);

            byte[] buffer = new byte[1];
            int read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, read);
        }
        finally
        {
            KillProcessIfRunning(server);
        }
    }

    [Fact]
    public async Task ServerProcess_DisconnectsClientThatStallsMidFrame()
    {
        (Process server, int port) = await StartReachableServerProcessAsync("--client-idle-timeout-seconds 5");
        using (server)
        try
        {
            using TcpClient rawClient = new();
            await rawClient.ConnectAsync(IPAddress.Loopback, port);
            NetworkStream stream = rawClient.GetStream();
            byte[] prefix = new byte[ProtocolConstants.LengthPrefixBytes];
            BinaryPrimitives.WriteInt32BigEndian(prefix, 128);
            await stream.WriteAsync(prefix);
            await stream.FlushAsync();

            byte[] buffer = new byte[1];
            int read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(0, read);
            await WaitForPortAsync(port, TimeSpan.FromSeconds(10));
            Assert.False(server.HasExited);
        }
        finally
        {
            KillProcessIfRunning(server);
        }
    }

    [Fact]
    public async Task ServerProcess_KeepsClientAliveWhenRequestsArriveBeforeIdleTimeout()
    {
        (Process server, int port) = await StartReachableServerProcessAsync("--client-idle-timeout-seconds 5");
        using (server)
        try
        {
            await using YFinanceServerClient client = new(new YFinanceServerConnectionOptions("127.0.0.1", port, TimeSpan.FromSeconds(5), NullYFinanceServerClientTraceSink.Instance));

            HealthResponseDto first = await client.HealthAsync();
            await Task.Delay(TimeSpan.FromSeconds(1));
            HealthResponseDto second = await client.HealthAsync();
            await Task.Delay(TimeSpan.FromSeconds(1));
            HealthResponseDto third = await client.HealthAsync();

            Assert.Equal("ok", first.Status);
            Assert.Equal("ok", second.Status);
            Assert.Equal("ok", third.Status);
        }
        finally
        {
            KillProcessIfRunning(server);
        }
    }

    [Fact]
    public async Task ServerProcess_DoesNotIdleDisconnectWhileResponseIsInFlight()
    {
        await FaultInjectionTestGate.WaitAsync();
        string profilePath = Path.Combine(Path.GetTempPath(), $"dnppv-yfinance-delay-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                profilePath,
                JsonSerializer.Serialize(new
                {
                    profile = "high-latency-yfinance",
                    mode = "delay-only",
                    delayMilliseconds = 6000,
                    operations = new[] { ProtocolOperations.Health }
                }));

            int port = GetAvailablePort();
            Process server = StartServerProcess(
                port,
                "--client-idle-timeout-seconds 5",
                startInfo => startInfo.Environment[YFinanceServerFaultInjection.ProfilePathEnvironmentVariable] = profilePath);
            using (server)
            try
            {
                // This test deliberately delays Health responses, so the normal
                // health-based readiness probe would be delayed by the same
                // injected fault. A raw TCP probe confirms the listener is ready
                // before the delayed protocol request proves idle behavior.
                await WaitForTcpPortOpenAsync(port, TimeSpan.FromSeconds(10));
                await using YFinanceServerClient client = new(new YFinanceServerConnectionOptions("127.0.0.1", port, TimeSpan.FromSeconds(20), NullYFinanceServerClientTraceSink.Instance));
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
                Stopwatch stopwatch = Stopwatch.StartNew();

                HealthResponseDto response = await client.HealthAsync(timeout.Token);

                Assert.Equal("ok", response.Status);
                Assert.True(stopwatch.Elapsed >= TimeSpan.FromSeconds(5));
            }
            finally
            {
                KillProcessIfRunning(server);
            }
        }
        finally
        {
            try
            {
                File.Delete(profilePath);
            }
            catch
            {
            }

            FaultInjectionTestGate.Release();
        }
    }

    [Fact]
    public async Task OwnedServerProcess_ExitsWhenOwnerProcessExits()
    {
        int port = GetAvailablePort();
        using Process owner = StartShortLivedOwnerProcess();
        using Process server = StartServerProcess(port, $"--owned --owner-pid {owner.Id}");
        try
        {
            await WaitForPortAsync(port);

            Assert.True(await WaitForExitAsync(owner, TimeSpan.FromSeconds(10)));
            Assert.True(await WaitForExitAsync(server, TimeSpan.FromSeconds(30)));
            Assert.Equal(0, server.ExitCode);
        }
        finally
        {
            KillProcessIfRunning(owner);
            KillProcessIfRunning(server);
        }
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "DoNotPanicPortfolioVisualizer.sln");
            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static Process StartServerProcess(int port, string extraArguments = "", Action<ProcessStartInfo>? configureStartInfo = null)
    {
        string repoRoot = GetRepoRoot();
        string serverDll = Path.Combine(repoRoot, "YFinance.net", "YFinance.NET.Server", "bin", "Release", "net10.0", "YFinance.NET.Server.dll");
        string arguments = $"\"{serverDll}\" --port {port} --max-clients 16";
        if (!string.IsNullOrWhiteSpace(extraArguments))
            arguments += " " + extraArguments;

        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        configureStartInfo?.Invoke(startInfo);

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start YFinance.NET.Server process.");
    }

    private static async Task<(Process Process, int Port)> StartReachableServerProcessAsync(string extraArguments = "", int attempts = 5, Action<ProcessStartInfo>? configureStartInfo = null)
    {
        List<Exception> failures = [];
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            // A child process cannot inherit an already-bound port reservation,
            // so this best-effort probe is paired with retries to avoid rare
            // loopback port races in parallel or noisy test environments.
            int port = GetAvailablePort();
            Process process = StartServerProcess(port, extraArguments, configureStartInfo);
            try
            {
                await WaitForPortAsync(port, TimeSpan.FromSeconds(5));
                return (process, port);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
                KillProcessIfRunning(process);
                process.Dispose();
            }
        }

        throw new AggregateException("Failed to start a reachable YFinance.NET.Server process on an available loopback port.", failures);
    }

    private static Process StartShortLivedOwnerProcess()
    {
        ProcessStartInfo startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 2\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
            : new ProcessStartInfo
            {
                FileName = "sh",
                Arguments = "-c \"sleep 2\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start short-lived owner process.");
    }

    private static int GetAvailablePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static MemoryStream CreateFramedStream(int length, byte[]? payload = null)
    {
        byte[] prefix = new byte[ProtocolConstants.LengthPrefixBytes];
        BinaryPrimitives.WriteInt32BigEndian(prefix, length);
        MemoryStream stream = new();
        stream.Write(prefix);
        if (payload is not null)
            stream.Write(payload);
        stream.Position = 0;
        return stream;
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task WaitForPortAsync(int port)
        => await WaitForPortAsync(port, TimeSpan.FromSeconds(15));

    private static async Task WaitForPortAsync(int port, TimeSpan timeoutDuration)
    {
        using CancellationTokenSource timeout = new(timeoutDuration);
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                await using YFinanceServerClient client = new(new YFinanceServerConnectionOptions("127.0.0.1", port, TimeSpan.FromMilliseconds(500), NullYFinanceServerClientTraceSink.Instance));
                HealthResponseDto health = await client.HealthAsync(timeout.Token);
                if (string.Equals(health.Status, "ok", StringComparison.Ordinal))
                    return;
            }
            catch
            {
            }

            await Task.Delay(200, timeout.Token);
        }

        throw new TimeoutException($"Timed out waiting for server on port {port}.");
    }

    private static async Task WaitForTcpPortOpenAsync(int port, TimeSpan timeoutDuration)
    {
        using CancellationTokenSource timeout = new(timeoutDuration);
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                using TcpClient client = new();
                await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
                return;
            }
            catch
            {
            }

            await Task.Delay(200, timeout.Token);
        }

        throw new TimeoutException($"Timed out waiting for TCP listener on port {port}.");
    }

    private static void KillProcessIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch
        {
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dnppv-yfinance-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
