using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Xunit;
using YFinance.NET.Client;
using YFinance.NET.Protocol.Constants;
using YFinance.NET.Protocol.Dtos;
using YFinance.NET.Protocol.Integrity;
using YFinance.NET.Protocol.Messages;
using YFinance.NET.Protocol.Transport;

namespace PortfolioSaver.Tests.Services;

public sealed class YFinanceServerClientPipelineTests
{
    [Fact]
    public async Task Client_CanPipelineRequests_AndMatchOutOfOrderResponses()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        Task serverTask = Task.Run(async () =>
        {
            await using NetworkStream stream = (await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false)).GetStream();

            ProtocolRequest<JsonElement> requestA = await ReadRequestAsync(stream, cts.Token).ConfigureAwait(false);
            ProtocolRequest<JsonElement> requestB = await ReadRequestAsync(stream, cts.Token).ConfigureAwait(false);

            await WriteQuoteResponseAsync(stream, requestB, cts.Token).ConfigureAwait(false);
            await Task.Delay(100, cts.Token).ConfigureAwait(false);
            await WriteQuoteResponseAsync(stream, requestA, cts.Token).ConfigureAwait(false);
        }, cts.Token);

        await using YFinanceServerClient client = new(new YFinanceServerConnectionOptions(
            "127.0.0.1",
            port,
            TimeSpan.FromSeconds(3),
            NullYFinanceServerClientTraceSink.Instance));

        Task<QuoteDto> first = client.GetQuoteAsync("AAA", cts.Token);
        Task<QuoteDto> second = client.GetQuoteAsync("BBB", cts.Token);

        QuoteDto[] results = await Task.WhenAll(first, second).WaitAsync(cts.Token);

        Assert.Equal("AAA", results[0].Symbol);
        Assert.Equal("BBB", results[1].Symbol);
        await serverTask;
    }

    [Fact]
    public async Task Client_DisposeWhileRequestPending_CompletesAndSettlesPendingRequest()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        TaskCompletionSource requestReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseServer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task serverTask = Task.Run(async () =>
        {
            using TcpClient accepted = await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false);
            await using NetworkStream stream = accepted.GetStream();
            _ = await ReadRequestAsync(stream, cts.Token).ConfigureAwait(false);
            requestReceived.SetResult();
            await releaseServer.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }, cts.Token);

        YFinanceServerClient client = new(new YFinanceServerConnectionOptions(
            "127.0.0.1",
            port,
            TimeSpan.FromSeconds(3),
            NullYFinanceServerClientTraceSink.Instance));

        Task<QuoteDto> pendingQuote = client.GetQuoteAsync("HANG", cts.Token);
        await requestReceived.Task.WaitAsync(cts.Token).ConfigureAwait(false);

        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await AssertPendingRequestSettledByDisposeAsync(pendingQuote).ConfigureAwait(false);

        releaseServer.SetResult();
        await serverTask.ConfigureAwait(false);
    }

    [Fact]
    public async Task Client_SyncDisposeWhileRequestPending_CompletesAndSettlesPendingRequest()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        TaskCompletionSource requestReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseServer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task serverTask = Task.Run(async () =>
        {
            using TcpClient accepted = await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false);
            await using NetworkStream stream = accepted.GetStream();
            _ = await ReadRequestAsync(stream, cts.Token).ConfigureAwait(false);
            requestReceived.SetResult();
            await releaseServer.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }, cts.Token);

        YFinanceServerClient client = new(new YFinanceServerConnectionOptions(
            "127.0.0.1",
            port,
            TimeSpan.FromSeconds(3),
            NullYFinanceServerClientTraceSink.Instance));

        Task<QuoteDto> pendingQuote = client.GetQuoteAsync("HANG", cts.Token);
        await requestReceived.Task.WaitAsync(cts.Token).ConfigureAwait(false);

        client.Dispose();
        await AssertPendingRequestSettledByDisposeAsync(pendingQuote).ConfigureAwait(false);

        releaseServer.SetResult();
        await serverTask.ConfigureAwait(false);
    }

    [Fact]
    public async Task Client_DisposeAsync_SendsGoodbyeOnExistingConnection()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        TaskCompletionSource<string> goodbyeOperation = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task serverTask = Task.Run(async () =>
        {
            using TcpClient accepted = await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false);
            await using NetworkStream stream = accepted.GetStream();

            ProtocolRequest<JsonElement> healthRequest = await ReadRequestAsync(stream, cts.Token).ConfigureAwait(false);
            await WriteHealthResponseAsync(stream, healthRequest, cts.Token).ConfigureAwait(false);

            ProtocolRequest<JsonElement> goodbyeRequest = await ReadRequestAsync(stream, cts.Token).ConfigureAwait(false);
            goodbyeOperation.SetResult(goodbyeRequest.Operation);
            await WriteEmptyResponseAsync(stream, goodbyeRequest, cts.Token).ConfigureAwait(false);
        }, cts.Token);

        await using (YFinanceServerClient client = new(new YFinanceServerConnectionOptions(
                         "127.0.0.1",
                         port,
                         TimeSpan.FromSeconds(3),
                         NullYFinanceServerClientTraceSink.Instance)))
        {
            HealthResponseDto health = await client.HealthAsync(cts.Token).ConfigureAwait(false);
            Assert.Equal("ok", health.Status);
        }

        Assert.Equal(ProtocolOperations.Goodbye, await goodbyeOperation.Task.WaitAsync(cts.Token).ConfigureAwait(false));
        await serverTask.ConfigureAwait(false);
    }

    [Fact]
    public void Client_SyncDispose_IsIdempotent()
    {
        YFinanceServerClient client = new(new YFinanceServerConnectionOptions(
            "127.0.0.1",
            1,
            TimeSpan.FromMilliseconds(50),
            NullYFinanceServerClientTraceSink.Instance));

        client.Dispose();
        client.Dispose();
    }

    [Fact]
    public async Task Client_AsyncDispose_IsIdempotent()
    {
        YFinanceServerClient client = new(new YFinanceServerConnectionOptions(
            "127.0.0.1",
            1,
            TimeSpan.FromMilliseconds(50),
            NullYFinanceServerClientTraceSink.Instance));

        await client.DisposeAsync().ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task Client_ConcurrentDisposeAndDisposeAsync_AreIdempotent()
    {
        YFinanceServerClient client = new(new YFinanceServerConnectionOptions(
            "127.0.0.1",
            1,
            TimeSpan.FromMilliseconds(50),
            NullYFinanceServerClientTraceSink.Instance));

        Task syncDispose = Task.Run(client.Dispose);
        Task asyncDispose = Task.Run(async () => await client.DisposeAsync().ConfigureAwait(false));

        await Task.WhenAll(syncDispose, asyncDispose).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Client_PublicCallsAfterSyncDispose_ThrowObjectDisposedException()
    {
        YFinanceServerClient client = new(new YFinanceServerConnectionOptions(
            "127.0.0.1",
            1,
            TimeSpan.FromMilliseconds(50),
            NullYFinanceServerClientTraceSink.Instance));

        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.HealthAsync().ConfigureAwait(false)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Client_PublicCallsAfterAsyncDispose_ThrowObjectDisposedException()
    {
        YFinanceServerClient client = new(new YFinanceServerConnectionOptions(
            "127.0.0.1",
            1,
            TimeSpan.FromMilliseconds(50),
            NullYFinanceServerClientTraceSink.Instance));

        await client.DisposeAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.HealthAsync().ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static async Task<ProtocolRequest<JsonElement>> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[]? bytes = await LengthPrefixedProtocolStream.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
        Assert.NotNull(bytes);
        ProtocolRequest<JsonElement>? request = ProtocolJson.Deserialize<ProtocolRequest<JsonElement>>(bytes!);
        Assert.NotNull(request);
        Assert.True(ProtocolIntegrity.Verify(request!, request!.Payload));
        return request!;
    }

    private static async Task AssertPendingRequestSettledByDisposeAsync(Task<QuoteDto> pendingQuote)
    {
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await pendingQuote.ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static async Task WriteQuoteResponseAsync(NetworkStream stream, ProtocolRequest<JsonElement> request, CancellationToken cancellationToken)
    {
        GetQuoteRequestDto? payload = request.Payload.Deserialize<GetQuoteRequestDto>(ProtocolJson.SerializerOptions);
        Assert.NotNull(payload);
        QuoteDto quote = new(
            payload!.Symbol,
            payload.Symbol,
            payload.Symbol,
            payload.Symbol,
            "USD",
            "TEST",
            "America/New_York",
            "EDT",
            "INDEX",
            "REGULAR",
            123.45m,
            120.00m,
            121.00m,
            124.00m,
            119.00m,
            3.45m,
            2.88m,
            null,
            null,
            DateTimeOffset.Now,
            new CacheMetadataDto("live", 0, false));

        ProtocolResponse<QuoteDto> response = new()
        {
            RequestId = request.RequestId,
            Operation = request.Operation,
            Status = "ok",
            Payload = quote
        };
        ProtocolIntegrity.Stamp(response, response.Payload);
        await LengthPrefixedProtocolStream.WriteAsync(stream, ProtocolJson.Serialize(response), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteHealthResponseAsync(NetworkStream stream, ProtocolRequest<JsonElement> request, CancellationToken cancellationToken)
    {
        HealthResponseDto health = new("ok", 1.0, 1, 0, "test");
        ProtocolResponse<HealthResponseDto> response = new()
        {
            RequestId = request.RequestId,
            Operation = request.Operation,
            Status = "ok",
            Payload = health
        };
        ProtocolIntegrity.Stamp(response, response.Payload);
        await LengthPrefixedProtocolStream.WriteAsync(stream, ProtocolJson.Serialize(response), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteEmptyResponseAsync(NetworkStream stream, ProtocolRequest<JsonElement> request, CancellationToken cancellationToken)
    {
        ProtocolResponse<EmptyPayload> response = new()
        {
            RequestId = request.RequestId,
            Operation = request.Operation,
            Status = "ok",
            Payload = new EmptyPayload()
        };
        ProtocolIntegrity.Stamp(response, response.Payload);
        await LengthPrefixedProtocolStream.WriteAsync(stream, ProtocolJson.Serialize(response), cancellationToken).ConfigureAwait(false);
    }
}
