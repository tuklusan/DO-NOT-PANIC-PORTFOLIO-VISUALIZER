using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PortfolioSaver.Shared.Helpers;
using YFinance.NET.Api;
using YFinance.NET.Config;
using YFinance.NET.Diagnostics;
using YFinance.NET.Protocol.Constants;
using YFinance.NET.Protocol.Dtos;
using YFinance.NET.Protocol.Errors;
using YFinance.NET.Protocol.Integrity;
using YFinance.NET.Protocol.Messages;
using YFinance.NET.Protocol.Transport;
using YFinance.NET.Server.Mapping;

namespace YFinance.NET.Server.Hosting;

internal static class YFinanceServerProgram
{
    private static readonly string TraceRoot = ResolveTraceRoot();

    public static int Run(string[] args)
    {
        ServerOptions options = ServerOptions.Parse(args);
        using Mutex singleInstanceMutex = new(false, ProtocolConstants.GetMutexName(options.Port), out bool createdNew);
        if (!createdNew)
        {
            YFinanceCircularTraceSink.Instance.WarnState("YFinanceServer", "DuplicateServerStartRejected",
            [new("port", options.Port), new("owned_mode", options.OwnedMode), new("owner_pid", options.OwnerProcessId)]);
            return 0;
        }

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

        YFinanceCircularTraceSink.Instance.InfoState("YFinanceServer", "ServerStartup",
        [new("port", options.Port), new("owned_mode", options.OwnedMode), new("owner_pid", options.OwnerProcessId), new("max_clients", options.MaxConcurrentClients)]);

        try
        {
            RunAsync(options, cts.Token).GetAwaiter().GetResult();
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            YFinanceCircularTraceSink.Instance.ErrorState("YFinanceServer", "ServerFatal", [], ex);
            return -1;
        }
    }

    private static async Task RunAsync(ServerOptions options, CancellationToken cancellationToken)
    {
        using YFinanceClient client = CreateDomainClient();
        using TcpListener listener = new(IPAddress.Any, options.Port);
        listener.Start(options.MaxConcurrentClients);

        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? ownerMonitor = options.OwnedMode && options.OwnerProcessId is int ownerPid
            ? MonitorOwnerAsync(ownerPid, linkedCts)
            : null;

        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        int activeConnections = 0;

        while (!linkedCts.IsCancellationRequested)
        {
            TcpClient tcpClient;
            try
            {
                tcpClient = await listener.AcceptTcpClientAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            int current = Interlocked.Increment(ref activeConnections);
            if (current > options.MaxConcurrentClients)
            {
                Interlocked.Decrement(ref activeConnections);
                await using NetworkStream rejected = tcpClient.GetStream();
                ProtocolResponse<EmptyPayload> overload = new()
                {
                    RequestId = string.Empty,
                    Operation = string.Empty,
                    Status = ProtocolResponseStatuses.Error,
                    Error = new ProtocolError(ProtocolErrorCodes.ServerOverloaded, "Server is overloaded.", true)
                };
                ProtocolIntegrity.Stamp(overload, overload.Payload);
                await LengthPrefixedProtocolStream.WriteAsync(rejected, ProtocolJson.Serialize(overload), linkedCts.Token).ConfigureAwait(false);
                tcpClient.Dispose();
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleClientAsync(tcpClient, client, options, startedUtc, () => Volatile.Read(ref activeConnections), linkedCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref activeConnections);
                    tcpClient.Dispose();
                }
            }, linkedCts.Token);
        }

        if (ownerMonitor is not null)
            await ownerMonitor.ConfigureAwait(false);

        listener.Stop();
    }

    private static YFinanceClient CreateDomainClient()
        => new(new YFinanceOptions
        {
            MinimumRequestSpacing = TimeSpan.FromSeconds(1),
            MaxRetries = 3,
            DefaultCacheTtl = TimeSpan.FromMinutes(10),
            SummaryCacheTtl = TimeSpan.FromMinutes(10),
            PersistentMetadataCacheTtl = TimeSpan.FromMinutes(10),
            MaxSymbolsPerQuoteRequest = 25,
            TraceSink = YFinanceCircularTraceSink.Instance
        });

    private static async Task MonitorOwnerAsync(int ownerPid, CancellationTokenSource shutdown)
    {
        try
        {
            Process owner = Process.GetProcessById(ownerPid);
            while (!shutdown.IsCancellationRequested)
            {
                if (owner.HasExited)
                {
                    YFinanceCircularTraceSink.Instance.InfoState("YFinanceServer", "OwnerProcessExited", [new("owner_pid", ownerPid)]);
                    shutdown.Cancel();
                    return;
                }

                await Task.Delay(1000, shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            YFinanceCircularTraceSink.Instance.WarnState("YFinanceServer", "OwnerProcessUnavailable", [new("owner_pid", ownerPid), new("message", ex.Message)]);
            shutdown.Cancel();
        }
    }

    private static async Task HandleClientAsync(TcpClient tcpClient, YFinanceClient client, ServerOptions options, DateTimeOffset startedUtc, Func<int> getActiveConnections, CancellationToken cancellationToken)
    {
        string remote = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
        YFinanceCircularTraceSink.Instance.InfoState("YFinanceServer", "ClientConnected", [new("remote", remote)]);
        await using NetworkStream stream = tcpClient.GetStream();
        SemaphoreSlim writeGate = new(1, 1);
        List<Task> inFlight = [];
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[]? messageBytes = await LengthPrefixedProtocolStream.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            if (messageBytes is null)
                break;

            ProtocolRequest<JsonElement>? request = ProtocolJson.Deserialize<ProtocolRequest<JsonElement>>(messageBytes);
            if (request is null)
                throw new InvalidOperationException("Protocol request could not be deserialized.");
            if (!ProtocolIntegrity.Verify(request, request.Payload))
            {
                YFinanceCircularTraceSink.Instance.WarnState("YFinanceServer", "RequestIntegrityRejected", [new("remote", remote), new("request_id", request.RequestId), new("operation", request.Operation), new("timestamp", request.Timestamp), new("payload_checksum", request.PayloadChecksum)]);
                ProtocolResponse<EmptyPayload> integrityError = CreateError(request, ProtocolErrorCodes.ProtocolViolation, "Payload checksum mismatch.", false);
                await WriteResponseAsync(stream, writeGate, integrityError, remote, request, cancellationToken).ConfigureAwait(false);
                continue;
            }

            YFinanceCircularTraceSink.Instance.InfoState("YFinanceServer", "RequestReceived", [new("remote", remote), new("request_id", request.RequestId), new("operation", request.Operation), new("timestamp", request.Timestamp), new("payload_checksum", request.PayloadChecksum)]);
            Task requestTask = Task.Run(async () =>
            {
                object response = await DispatchAsync(request, client, options, startedUtc, getActiveConnections, cancellationToken).ConfigureAwait(false);
                await WriteResponseAsync(stream, writeGate, response, remote, request, cancellationToken).ConfigureAwait(false);
            }, cancellationToken);
            inFlight.Add(requestTask);
            if (string.Equals(request.Operation, ProtocolOperations.Goodbye, StringComparison.Ordinal))
                break;
        }

        if (inFlight.Count > 0)
            await Task.WhenAll(inFlight).ConfigureAwait(false);

        YFinanceCircularTraceSink.Instance.InfoState("YFinanceServer", "ClientDisconnected", [new("remote", remote)]);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, SemaphoreSlim writeGate, object response, string remote, ProtocolRequest<JsonElement> request, CancellationToken cancellationToken)
    {
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LengthPrefixedProtocolStream.WriteAsync(stream, ProtocolJson.Serialize(response), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }

        if (response is ProtocolEnvelope envelope)
        {
            YFinanceCircularTraceSink.Instance.InfoState("YFinanceServer", "ResponseSent", [new("remote", remote), new("request_id", request.RequestId), new("operation", request.Operation), new("timestamp", envelope.Timestamp), new("payload_checksum", envelope.PayloadChecksum)]);
        }
        else
        {
            YFinanceCircularTraceSink.Instance.InfoState("YFinanceServer", "ResponseSent", [new("remote", remote), new("request_id", request.RequestId), new("operation", request.Operation)]);
        }
    }

    private static async Task<object> DispatchAsync(ProtocolRequest<JsonElement> request, YFinanceClient client, ServerOptions options, DateTimeOffset startedUtc, Func<int> getActiveConnections, CancellationToken cancellationToken)
    {
        try
        {
            return request.Operation switch
            {
                ProtocolOperations.Hello => CreateOk(request, HandleHello(request.Payload.Deserialize<HelloRequestDto>(ProtocolJson.SerializerOptions), options, getActiveConnections())),
                ProtocolOperations.Goodbye => CreateOk(request, new EmptyPayload()),
                ProtocolOperations.Health => CreateOk(request, new HealthResponseDto("ok", (DateTimeOffset.UtcNow - startedUtc).TotalSeconds, getActiveConnections(), 0, options.OwnedMode ? "owned" : "standalone")),
                ProtocolOperations.GetServerStatus => CreateOk(request, new ServerStatusResponseDto("BETA-6", ProtocolConstants.Version, options.OwnedMode ? "owned" : "standalone", options.Port, getActiveConnections(), options.MaxConcurrentClients, 0, options.OwnerProcessId, Path.Combine(TraceRoot, "Trace", "yfinance.circular.log"))),
                ProtocolOperations.GetQuote => CreateOk(request, await HandleGetQuoteAsync(request.Payload.Deserialize<GetQuoteRequestDto>(ProtocolJson.SerializerOptions), client, cancellationToken).ConfigureAwait(false)),
                ProtocolOperations.GetQuotes => CreateOk(request, await HandleGetQuotesAsync(request.Payload.Deserialize<GetQuotesRequestDto>(ProtocolJson.SerializerOptions), client, cancellationToken).ConfigureAwait(false)),
                ProtocolOperations.GetHistory => CreateOk(request, await HandleGetHistoryAsync(request.Payload.Deserialize<GetHistoryRequestDto>(ProtocolJson.SerializerOptions), client, cancellationToken).ConfigureAwait(false)),
                ProtocolOperations.GetMarketTiming => CreateOk(request, await HandleGetMarketTimingAsync(request.Payload.Deserialize<GetMarketTimingRequestDto>(ProtocolJson.SerializerOptions), client, cancellationToken).ConfigureAwait(false)),
                ProtocolOperations.GetTickerInfo => CreateOk(request, await HandleGetTickerInfoAsync(request.Payload.Deserialize<GetTickerInfoRequestDto>(ProtocolJson.SerializerOptions), client, cancellationToken).ConfigureAwait(false)),
                _ => CreateError(request, ProtocolErrorCodes.UnsupportedOperation, $"Unsupported operation '{request.Operation}'.")
            };
        }
        catch (Exception ex)
        {
            return CreateError(request, MapErrorCode(ex), ex.Message, IsRetryable(ex));
        }
    }

    private static HelloResponseDto HandleHello(HelloRequestDto? payload, ServerOptions options, int activeConnections)
        => new("BETA-6", ProtocolConstants.Version, [ProtocolOperations.Hello, ProtocolOperations.Goodbye, ProtocolOperations.Health, ProtocolOperations.GetServerStatus, ProtocolOperations.GetQuote, ProtocolOperations.GetQuotes, ProtocolOperations.GetHistory, ProtocolOperations.GetMarketTiming, ProtocolOperations.GetTickerInfo], options.Port, options.OwnedMode ? "owned" : "standalone", activeConnections);

    private static async Task<QuoteDto> HandleGetQuoteAsync(GetQuoteRequestDto? payload, YFinanceClient client, CancellationToken cancellationToken)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.Symbol))
            throw new InvalidOperationException("Quote request requires a symbol.");

        Models.QuoteSnapshot? quote = await client.Ticker(payload.Symbol).GetQuoteAsync(cancellationToken).ConfigureAwait(false);
        if (quote is null)
            throw new InvalidOperationException($"No quote returned for symbol '{payload.Symbol}'.");
        QuoteDto mapped = ProtocolMapper.MapQuote(quote);
        TraceQuoteResponse("get_quote", mapped);
        return mapped;
    }

    private static async Task<QuotesResponseDto> HandleGetQuotesAsync(GetQuotesRequestDto? payload, YFinanceClient client, CancellationToken cancellationToken)
    {
        if (payload is null || payload.Symbols.Count == 0)
            throw new InvalidOperationException("Quotes request requires symbols.");

        IReadOnlyDictionary<string, Models.QuoteSnapshot> quotes = await client.Tickers(payload.Symbols).GetQuotesAsync(cancellationToken).ConfigureAwait(false);
        List<QuoteDto> mapped = quotes.Values.Select(ProtocolMapper.MapQuote).ToList();
        // Emit one compact line per symbol so VM spot checks can map displayed
        // UI values back to symbol-level YFinance.NET evidence without parsing
        // protocol payloads from the transport trace.
        foreach (QuoteDto quote in mapped)
            TraceQuoteResponse("get_quotes", quote);

        List<string> missing = payload.Symbols.Where(symbol => !quotes.ContainsKey(symbol)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new QuotesResponseDto(mapped, missing);
    }

    private static void TraceQuoteResponse(string operation, QuoteDto quote)
        // The circular sink is intentionally queue-backed/thread-safe; quote handlers
        // can run concurrently when multiple clients pipeline requests.
        => YFinanceCircularTraceSink.Instance.InfoState(
            "YFinanceServer",
            "QuoteResponseObserved",
            [
                new("operation", operation),
                new("symbol", quote.Symbol),
                new("price", quote.RegularMarketPrice),
                new("change", quote.RegularMarketChange),
                new("change_percent", quote.RegularMarketChangePercent),
                new("market_state", quote.MarketState ?? string.Empty),
                new("fetch_timestamp_utc", quote.FetchTimestampUtc)
            ]);

    private static async Task<HistoryResponseDto> HandleGetHistoryAsync(GetHistoryRequestDto? payload, YFinanceClient client, CancellationToken cancellationToken)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.Symbol))
            throw new InvalidOperationException("History request requires a symbol.");

        Models.HistoryResponse history = await client.Ticker(payload.Symbol).GetHistoryResponseAsync(payload.StartUtc, payload.EndUtc, payload.Interval, cancellationToken).ConfigureAwait(false);
        return ProtocolMapper.MapHistory(history);
    }

    private static async Task<MarketTimingDto> HandleGetMarketTimingAsync(GetMarketTimingRequestDto? payload, YFinanceClient client, CancellationToken cancellationToken)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.Symbol))
            throw new InvalidOperationException("Market timing request requires a symbol.");

        Models.MarketTimingSnapshot? timing = await client.Ticker(payload.Symbol).GetMarketTimingAsync(cancellationToken).ConfigureAwait(false);
        if (timing is null)
            throw new InvalidOperationException($"No market timing returned for symbol '{payload.Symbol}'.");
        return ProtocolMapper.MapMarketTiming(timing);
    }

    private static async Task<TickerInfoDto> HandleGetTickerInfoAsync(GetTickerInfoRequestDto? payload, YFinanceClient client, CancellationToken cancellationToken)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.Symbol))
            throw new InvalidOperationException("Ticker info request requires a symbol.");

        Models.TickerInfo? info = await client.Ticker(payload.Symbol).GetInfoAsync(cancellationToken).ConfigureAwait(false);
        if (info is null)
            throw new InvalidOperationException($"No ticker info returned for symbol '{payload.Symbol}'.");
        return ProtocolMapper.MapTickerInfo(info);
    }

    private static ProtocolResponse<TPayload> CreateOk<TPayload>(ProtocolRequest<JsonElement> request, TPayload payload)
    {
        ProtocolResponse<TPayload> response = new()
        {
            RequestId = request.RequestId,
            Operation = request.Operation,
            Status = ProtocolResponseStatuses.Ok,
            Payload = payload
        };
        ProtocolIntegrity.Stamp(response, response.Payload);
        return response;
    }

    private static ProtocolResponse<EmptyPayload> CreateError(ProtocolRequest<JsonElement> request, string code, string message, bool retryable = false)
    {
        ProtocolResponse<EmptyPayload> response = new()
        {
            RequestId = request.RequestId,
            Operation = request.Operation,
            Status = ProtocolResponseStatuses.Error,
            Error = new ProtocolError(code, message, retryable),
            Payload = new EmptyPayload()
        };
        ProtocolIntegrity.Stamp(response, response.Payload);
        return response;
    }

    private static string MapErrorCode(Exception ex)
    {
        if (ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
            return ProtocolErrorCodes.UpstreamThrottled;
        if (ex is HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout })
            return ProtocolErrorCodes.Timeout;
        if (ex is HttpRequestException)
            return ProtocolErrorCodes.UpstreamUnavailable;
        if (ex is TaskCanceledException or TimeoutException)
            return ProtocolErrorCodes.Timeout;
        return ProtocolErrorCodes.InternalError;
    }

    private static bool IsRetryable(Exception ex)
        => ex is HttpRequestException or TaskCanceledException or TimeoutException;

    private static string ResolveTraceRoot()
        => AppDataRootResolver.ResolveInstalledLocalDataRoot();
}
