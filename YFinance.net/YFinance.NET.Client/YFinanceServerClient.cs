using System.Net.Sockets;
using System.Collections.Concurrent;
using YFinance.NET.Protocol.Constants;
using YFinance.NET.Protocol.Dtos;
using YFinance.NET.Protocol.Errors;
using YFinance.NET.Protocol.Integrity;
using YFinance.NET.Protocol.Messages;
using YFinance.NET.Protocol.Transport;
using System.Text.Json;

namespace YFinance.NET.Client;

public sealed class YFinanceServerClient : IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly YFinanceServerConnectionOptions _options;
    private readonly ConcurrentDictionary<string, IPendingRequest> _pendingRequests = new(StringComparer.Ordinal);
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _connectionCts;
    private Task? _receiveLoopTask;
    private long _requestSequence;
    private bool _helloSent;

    public YFinanceServerClient(YFinanceServerConnectionOptions? options = null)
    {
        _options = options ?? YFinanceServerConnectionOptions.Default;
    }

    public async Task<HelloResponseDto> HelloAsync(HelloRequestDto request, CancellationToken cancellationToken = default)
        => await SendAsync<HelloRequestDto, HelloResponseDto>(ProtocolOperations.Hello, request, cancellationToken).ConfigureAwait(false);

    public async Task<HealthResponseDto> HealthAsync(CancellationToken cancellationToken = default)
        => await SendAsync<EmptyPayload, HealthResponseDto>(ProtocolOperations.Health, new EmptyPayload(), cancellationToken).ConfigureAwait(false);

    public async Task<ServerStatusResponseDto> GetServerStatusAsync(CancellationToken cancellationToken = default)
        => await SendAsync<EmptyPayload, ServerStatusResponseDto>(ProtocolOperations.GetServerStatus, new EmptyPayload(), cancellationToken).ConfigureAwait(false);

    public async Task<QuoteDto> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        => await SendAsync<GetQuoteRequestDto, QuoteDto>(ProtocolOperations.GetQuote, new GetQuoteRequestDto(symbol), cancellationToken).ConfigureAwait(false);

    public async Task<QuotesResponseDto> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default)
        => await SendAsync<GetQuotesRequestDto, QuotesResponseDto>(ProtocolOperations.GetQuotes, new GetQuotesRequestDto(symbols), cancellationToken).ConfigureAwait(false);

    public async Task<HistoryResponseDto> GetHistoryAsync(string symbol, DateTimeOffset startUtc, DateTimeOffset endUtc, string interval, CancellationToken cancellationToken = default)
        => await SendAsync<GetHistoryRequestDto, HistoryResponseDto>(ProtocolOperations.GetHistory, new GetHistoryRequestDto(symbol, startUtc, endUtc, interval), cancellationToken).ConfigureAwait(false);

    public async Task<MarketTimingDto> GetMarketTimingAsync(string symbol, CancellationToken cancellationToken = default)
        => await SendAsync<GetMarketTimingRequestDto, MarketTimingDto>(ProtocolOperations.GetMarketTiming, new GetMarketTimingRequestDto(symbol), cancellationToken).ConfigureAwait(false);

    public async Task<TickerInfoDto> GetTickerInfoAsync(string symbol, CancellationToken cancellationToken = default)
        => await SendAsync<GetTickerInfoRequestDto, TickerInfoDto>(ProtocolOperations.GetTickerInfo, new GetTickerInfoRequestDto(symbol), cancellationToken).ConfigureAwait(false);

    public async Task GoodbyeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<EmptyPayload, EmptyPayload>(ProtocolOperations.Goodbye, new EmptyPayload(), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public async Task ConnectAsync(HelloRequestDto helloRequest, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_helloSent)
            {
                await SendCoreAsync<HelloRequestDto, HelloResponseDto>(ProtocolOperations.Hello, helloRequest, cancellationToken).ConfigureAwait(false);
                _helloSent = true;
            }
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(string operation, TRequest payload, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        return await SendCoreAsync<TRequest, TResponse>(operation, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> SendCoreAsync<TRequest, TResponse>(string operation, TRequest payload, CancellationToken cancellationToken)
    {
        string requestId = $"req-{Interlocked.Increment(ref _requestSequence):D8}";
        ProtocolRequest<TRequest> request = new()
        {
            RequestId = requestId,
            Operation = operation,
            Payload = payload
        };
        ProtocolIntegrity.Stamp(request, payload);
        PendingRequest<TResponse> pending = new(operation, requestId);
        if (!_pendingRequests.TryAdd(requestId, pending))
            throw new IOException($"Duplicate request id '{requestId}'.");

        _options.TraceSink.Info("ClientRequestSend",
        [
            new("request_id", requestId),
            new("operation", operation),
            new("timestamp", request.Timestamp),
            new("payload_checksum", request.PayloadChecksum)
        ]);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LengthPrefixedProtocolStream.WriteAsync(_stream!, ProtocolJson.Serialize(request), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _pendingRequests.TryRemove(requestId, out _);
            pending.TrySetException(ex);
            throw;
        }
        finally
        {
            _writeGate.Release();
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            if (_pendingRequests.TryRemove(requestId, out IPendingRequest? removed))
                removed.TrySetCanceled(cancellationToken);
        });
        return await pending.Task.ConfigureAwait(false);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_tcpClient is { Connected: true } && _stream is not null)
            return;

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_tcpClient is { Connected: true } && _stream is not null)
                return;

            DisposeSocket();
            _options.TraceSink.Info("ClientConnectStart", [new("host", _options.Host), new("port", _options.Port)]);
            _tcpClient = new TcpClient();
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ConnectTimeout);
            await _tcpClient.ConnectAsync(_options.Host, _options.Port, timeoutCts.Token).ConfigureAwait(false);
            _stream = _tcpClient.GetStream();
            _connectionCts = new CancellationTokenSource();
            _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_connectionCts.Token), _connectionCts.Token);
            _helloSent = false;
            _options.TraceSink.Info("ClientConnectComplete", [new("host", _options.Host), new("port", _options.Port)]);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[]? responseBytes = await LengthPrefixedProtocolStream.ReadAsync(_stream!, cancellationToken).ConfigureAwait(false);
                if (responseBytes is null)
                    throw new IOException("Connection closed before a response was received.");

                using JsonDocument document = JsonDocument.Parse(responseBytes);
                string? messageType = document.RootElement.TryGetProperty("messageType", out JsonElement typeElement)
                    ? typeElement.GetString()
                    : null;

                if (string.Equals(messageType, ProtocolMessageTypes.Event, StringComparison.Ordinal))
                {
                    ProtocolEvent<JsonElement>? protocolEvent = ProtocolJson.Deserialize<ProtocolEvent<JsonElement>>(responseBytes);
                    if (protocolEvent is null)
                        throw new IOException("Event could not be deserialized.");

                    VerifyEnvelope(protocolEvent, protocolEvent.Payload, "event", protocolEvent.EventType, protocolEvent.EventType);
                    _options.TraceSink.Info("ClientEventReceive",
                    [
                        new("event_type", protocolEvent.EventType),
                        new("timestamp", protocolEvent.Timestamp),
                        new("payload_checksum", protocolEvent.PayloadChecksum)
                    ]);
                    continue;
                }

                ProtocolResponse<JsonElement>? response = ProtocolJson.Deserialize<ProtocolResponse<JsonElement>>(responseBytes);
                if (response is null)
                    throw new IOException("Response could not be deserialized.");

                VerifyEnvelope(response, response.Payload, "response", response.Operation, response.RequestId);
                _options.TraceSink.Info("ClientResponseReceive",
                [
                    new("request_id", response.RequestId),
                    new("operation", response.Operation),
                    new("status", response.Status),
                    new("timestamp", response.Timestamp),
                    new("payload_checksum", response.PayloadChecksum)
                ]);

                if (!_pendingRequests.TryRemove(response.RequestId, out IPendingRequest? pending))
                {
                    _options.TraceSink.Warn("ClientResponseUnexpected",
                    [
                        new("request_id", response.RequestId),
                        new("operation", response.Operation),
                        new("status", response.Status)
                    ]);
                    continue;
                }

                if (!string.Equals(response.Status, ProtocolResponseStatuses.Ok, StringComparison.Ordinal))
                {
                    ProtocolError? error = response.Error;
                    _options.TraceSink.Warn("ClientResponseError",
                    [
                        new("request_id", response.RequestId),
                        new("operation", response.Operation),
                        new("code", error?.Code ?? ProtocolErrorCodes.InternalError),
                        new("message", error?.Message ?? "Unknown protocol error."),
                        new("retryable", error?.Retryable ?? false)
                    ]);
                    pending.TrySetProtocolError(error);
                    continue;
                }

                pending.TrySetPayload(response.Payload);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _options.TraceSink.Warn("ClientReceiveLoopFailed",
            [
                new("message", ex.Message)
            ]);
            FailPendingRequests(ex);
        }
    }

    private void VerifyEnvelope<TPayload>(ProtocolEnvelope envelope, TPayload? payload, string kind, string operationOrEvent, string requestId)
    {
        if (string.IsNullOrWhiteSpace(envelope.PayloadChecksum))
            throw CreateIntegrityFailure(kind, operationOrEvent, requestId, "missing payload checksum");

        if (!ProtocolIntegrity.Verify(envelope, payload))
            throw CreateIntegrityFailure(kind, operationOrEvent, requestId, "payload checksum mismatch");
    }

    private IOException CreateIntegrityFailure(string kind, string operationOrEvent, string requestId, string reason)
    {
        _options.TraceSink.Warn("ClientIntegrityFailure",
        [
            new("kind", kind),
            new("operation_or_event", operationOrEvent),
            new("request_id", requestId),
            new("reason", reason)
        ]);
        return new IOException($"Protocol integrity failure for {kind} '{operationOrEvent}' ({requestId}): {reason}.");
    }

    private void DisposeSocket()
    {
        try { _connectionCts?.Cancel(); } catch { }
        _connectionCts?.Dispose();
        _connectionCts = null;
        _receiveLoopTask = null;
        try { _stream?.Dispose(); } catch { }
        try { _tcpClient?.Dispose(); } catch { }
        _stream = null;
        _tcpClient = null;
        _helloSent = false;
    }

    private void FailPendingRequests(Exception ex)
    {
        foreach ((string requestId, IPendingRequest pending) in _pendingRequests.ToArray())
        {
            if (_pendingRequests.TryRemove(requestId, out IPendingRequest? removed))
                removed.TrySetException(ex);
        }
    }

    public void Dispose()
    {
        DisposeSocket();
        _connectGate.Dispose();
        _writeGate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        try { await GoodbyeAsync().ConfigureAwait(false); } catch { }
        Dispose();
    }
}

internal interface IPendingRequest
{
    void TrySetPayload(JsonElement payload);
    void TrySetProtocolError(ProtocolError? error);
    void TrySetCanceled(CancellationToken cancellationToken);
    void TrySetException(Exception ex);
}

internal sealed class PendingRequest<TResponse> : IPendingRequest
{
    private readonly TaskCompletionSource<TResponse> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _operation;
    private readonly string _requestId;

    public PendingRequest(string operation, string requestId)
    {
        _operation = operation;
        _requestId = requestId;
    }

    public Task<TResponse> Task => _tcs.Task;

    public void TrySetPayload(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            _tcs.TrySetException(new IOException($"Operation '{_operation}' returned no payload."));
            return;
        }

        TResponse? typedPayload = payload.Deserialize<TResponse>(ProtocolJson.SerializerOptions);
        if (typedPayload is null)
        {
            _tcs.TrySetException(new IOException($"Operation '{_operation}' returned an unreadable payload."));
            return;
        }

        _tcs.TrySetResult(typedPayload);
    }

    public void TrySetProtocolError(ProtocolError? error)
        => _tcs.TrySetException(new YFinanceServerProtocolException(
            error?.Code ?? ProtocolErrorCodes.InternalError,
            error?.Message ?? $"Unknown protocol error for request '{_requestId}'.",
            error?.Retryable ?? false));

    public void TrySetCanceled(CancellationToken cancellationToken)
        => _tcs.TrySetCanceled(cancellationToken);

    public void TrySetException(Exception ex)
        => _tcs.TrySetException(ex);
}

public sealed class YFinanceServerProtocolException : Exception
{
    public YFinanceServerProtocolException(string code, string message, bool retryable)
        : base(message)
    {
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }
    public bool Retryable { get; }
}
