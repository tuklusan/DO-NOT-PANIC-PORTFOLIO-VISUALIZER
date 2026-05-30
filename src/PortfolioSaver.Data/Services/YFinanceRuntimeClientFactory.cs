using System.Security.Cryptography;
using System.Text;
using PortfolioSaver.Shared;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Services;
using YFinance.NET.Client;
using YFinance.NET.Protocol.Dtos;

namespace PortfolioSaver.Data.Services;

public static class YFinanceRuntimeClientFactory
{
    private static readonly object Sync = new();
    private static readonly SemaphoreSlim ClientGate = new(1, 1);
    private static long _operationSequence;
    private static YFinanceServerClient? _sharedClient;
    private static bool _helloCompleted;

    public static YFinanceServerClient GetSharedClient()
    {
        lock (Sync)
        {
            _sharedClient ??= new YFinanceServerClient(new YFinanceServerConnectionOptions(
                "127.0.0.1",
                YFinance.NET.Protocol.Constants.ProtocolConstants.DefaultPort,
                TimeSpan.FromSeconds(5),
                PortfolioSaverYFinanceServerClientTraceSink.Instance));
            return _sharedClient;
        }
    }

    public static async Task EnsureServerReadyAsync(string clientType, string clientVersion, CancellationToken cancellationToken = default)
    {
        await YFinanceServerProcessManager.EnsureOwnedServerAsync(clientType, cancellationToken).ConfigureAwait(false);

        if (_helloCompleted)
            return;

        await ClientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_helloCompleted)
                return;

            HelloRequestDto hello = new(
                clientType,
                clientVersion,
                BuildMachineHash(),
                true,
                Environment.ProcessId);
            TraceLog.InfoState("YFinanceUiBridge", "ServerHelloStart", [new("client_type", clientType), new("client_version", clientVersion)]);
            await GetSharedClient().ConnectAsync(hello, cancellationToken).ConfigureAwait(false);
            _helloCompleted = true;
            TraceLog.InfoState("YFinanceUiBridge", "ServerHelloComplete", [new("client_type", clientType), new("client_version", clientVersion)]);
        }
        finally
        {
            ClientGate.Release();
        }
    }

    public static async Task<T> RunSerializedAsync<T>(string lane, Func<YFinanceServerClient, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        => await RunSerializedAsync(lane, CreateOperationId(lane), action, cancellationToken).ConfigureAwait(false);

    public static async Task<T> RunSerializedAsync<T>(string lane, string operationId, Func<YFinanceServerClient, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        await EnsureServerReadyAsync("PortfolioSaver.Runtime", PortfolioVersion.SemanticVersion, cancellationToken).ConfigureAwait(false);
        await ClientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TraceLog.InfoState("YFinanceRuntimeClientFactory", "SerializedClientEnter", [new("lane", lane), new("operation_id", operationId)]);
            return await action(GetSharedClient(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TraceLog.WarnState("YFinanceRuntimeClientFactory", "SerializedClientError", [new("lane", lane), new("operation_id", operationId), new("message", ex.Message)]);
            throw;
        }
        finally
        {
            TraceLog.InfoState("YFinanceRuntimeClientFactory", "SerializedClientExit", [new("lane", lane), new("operation_id", operationId)]);
            ClientGate.Release();
        }
    }

    public static string CreateOperationId(string lane)
        => $"{lane}-{Interlocked.Increment(ref _operationSequence):D8}";

    private static string BuildMachineHash()
    {
        string raw = $"{Environment.MachineName}|{Environment.UserName}|{Environment.OSVersion}|{Environment.ProcessorCount}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32];
    }
}
