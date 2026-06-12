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
    private static readonly SemaphoreSlim HelloGate = new(1, 1);
    private static long _operationSequence;
    private static YFinanceServerClient? _sharedClient;
    private static readonly List<YFinanceServerClient> RetiredClients = [];
    private static int _activeClientOperations;
    private static bool _helloCompleted;
    private static bool _serverReadyEnsured;
    private static readonly AsyncLocal<int> ServerStartupSuppressedForTests = new();

    internal static bool IsServerStartupSuppressedForTests => ServerStartupSuppressedForTests.Value > 0;

    public static async Task EnsureServerReadyAsync(string clientType, string clientVersion, CancellationToken cancellationToken = default)
    {
        if (IsServerStartupSuppressedForTests)
            return;

        if (!_serverReadyEnsured)
        {
            await YFinanceServerProcessManager.EnsureOwnedServerAsync(clientType, cancellationToken).ConfigureAwait(false);
            _serverReadyEnsured = true;
        }

        if (_helloCompleted)
            return;

        await HelloGate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
            YFinanceServerClient client = RentSharedClient();
            try
            {
                await client.ConnectAsync(hello, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                RetireConnectionState(client);
                throw;
            }
            finally
            {
                ReleaseSharedClientOperation();
            }

            _helloCompleted = true;
            TraceLog.InfoState("YFinanceUiBridge", "ServerHelloComplete", [new("client_type", clientType), new("client_version", clientVersion)]);
        }
        finally
        {
            HelloGate.Release();
        }
    }

    public static async Task<T> RunAsync<T>(string lane, Func<YFinanceServerClient, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        => await RunAsync(lane, CreateOperationId(lane), action, cancellationToken).ConfigureAwait(false);

    public static async Task<T> RunAsync<T>(string lane, string operationId, Func<YFinanceServerClient, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        await EnsureServerReadyAsync("PortfolioSaver.Runtime", PortfolioVersion.SemanticVersion, cancellationToken).ConfigureAwait(false);
        try
        {
            TraceLog.InfoState("YFinanceRuntimeClientFactory", "ClientOperationStart", [new("lane", lane), new("operation_id", operationId)]);
            YFinanceServerClient client = RentSharedClient();
            try
            {
                return await action(client, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                RetireConnectionState(client);
                throw;
            }
            finally
            {
                ReleaseSharedClientOperation();
            }
        }
        catch (Exception ex)
        {
            TraceLog.WarnState("YFinanceRuntimeClientFactory", "ClientOperationError", [new("lane", lane), new("operation_id", operationId), new("message", ex.Message)]);
            throw;
        }
        finally
        {
            TraceLog.InfoState("YFinanceRuntimeClientFactory", "ClientOperationComplete", [new("lane", lane), new("operation_id", operationId)]);
        }
    }

    public static async Task<T> RunSerializedAsync<T>(string lane, Func<YFinanceServerClient, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        => await RunAsync(lane, CreateOperationId(lane), action, cancellationToken).ConfigureAwait(false);

    public static async Task<T> RunSerializedAsync<T>(string lane, string operationId, Func<YFinanceServerClient, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        => await RunAsync(lane, operationId, action, cancellationToken).ConfigureAwait(false);

    public static string CreateOperationId(string lane)
        => $"{lane}-{Interlocked.Increment(ref _operationSequence):D8}";

    /// <summary>
    /// Suppresses owned-server startup for tests that exercise factory scheduling without using the client.
    /// </summary>
    /// <remarks>The returned scope must be disposed with a using statement.</remarks>
    internal static IDisposable SuppressServerStartupForTests()
    {
        ServerStartupSuppressedForTests.Value++;
        return new TestServerStartupSuppressionScope();
    }

    private static string BuildMachineHash()
    {
        string raw = $"{Environment.MachineName}|{Environment.UserName}|{Environment.OSVersion}|{Environment.ProcessorCount}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32];
    }

    private static YFinanceServerClient RentSharedClient()
    {
        lock (Sync)
        {
            _sharedClient ??= CreateClient();
            _activeClientOperations++;
            return _sharedClient;
        }
    }

    private static YFinanceServerClient CreateClient()
        => new(new YFinanceServerConnectionOptions(
            "127.0.0.1",
            YFinance.NET.Protocol.Constants.ProtocolConstants.DefaultPort,
            TimeSpan.FromSeconds(5),
            PortfolioSaverYFinanceServerClientTraceSink.Instance));

    private static void RetireConnectionState(YFinanceServerClient failedClient)
    {
        List<YFinanceServerClient> disposeNow = [];
        lock (Sync)
        {
            if (ReferenceEquals(_sharedClient, failedClient))
            {
                _sharedClient = null;
                _helloCompleted = false;
                _serverReadyEnsured = false;
            }

            if (_activeClientOperations == 0)
                disposeNow.Add(failedClient);
            else if (!RetiredClients.Any(client => ReferenceEquals(client, failedClient)))
                RetiredClients.Add(failedClient);
        }

        DisposeClients(disposeNow);
    }

    private static void ReleaseSharedClientOperation()
    {
        List<YFinanceServerClient> disposeNow = [];
        lock (Sync)
        {
            if (_activeClientOperations > 0)
                _activeClientOperations--;

            if (_activeClientOperations == 0 && RetiredClients.Count > 0)
            {
                disposeNow.AddRange(RetiredClients);
                RetiredClients.Clear();
            }
        }

        DisposeClients(disposeNow);
    }

    private static void ResetConnectionState()
    {
        YFinanceServerClient? sharedClient;
        lock (Sync)
        {
            sharedClient = _sharedClient;
            _sharedClient = null;
            _helloCompleted = false;
            _serverReadyEnsured = false;
        }

        if (sharedClient is not null)
            RetireConnectionState(sharedClient);
    }

    private static void DisposeClients(IReadOnlyList<YFinanceServerClient> clients)
    {
        foreach (YFinanceServerClient client in clients)
        {
            try
            {
                client.Dispose();
            }
            catch
            {
            }
        }
    }

    private sealed class TestServerStartupSuppressionScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                ServerStartupSuppressedForTests.Value--;
        }
    }
}
