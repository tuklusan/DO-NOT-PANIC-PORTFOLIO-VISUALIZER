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
using System.ComponentModel;
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
    // The protocol client owns one TCP stream with async pipelining internally,
    // but the runtime facade deliberately serializes access to the shared client.
    // This trades throughput for deterministic UI cadence and avoids corrupting
    // connection state during reconnect/retirement paths. Revisit only if the
    // facade moves to a tested client pool.
    private static readonly SemaphoreSlim SharedClientOperationGate = new(1, 1);
    private static long _operationSequence;
    private static SharedClientEntry? _sharedClient;
    private static readonly List<SharedClientEntry> RetiredClients = [];
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
            await SharedClientOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            SharedClientLease lease = RentSharedClient();
            try
            {
                await lease.Client.ConnectAsync(hello, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                RetireConnectionState(lease.Entry);
                throw;
            }
            finally
            {
                ReleaseSharedClientOperation(lease.Entry);
                SharedClientOperationGate.Release();
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
        string outcome = "success";
        try
        {
            await EnsureServerReadyAsync("PortfolioSaver.Runtime", PortfolioVersion.Version, cancellationToken).ConfigureAwait(false);
            TraceLog.InfoState("YFinanceRuntimeClientFactory", "ClientOperationStart", [new("lane", lane), new("operation_id", operationId)]);
            await SharedClientOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            SharedClientLease lease = RentSharedClient();
            try
            {
                return await action(lease.Client, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                RetireConnectionState(lease.Entry);
                throw;
            }
            finally
            {
                ReleaseSharedClientOperation(lease.Entry);
                SharedClientOperationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "canceled";
            TraceLog.InfoState("YFinanceRuntimeClientFactory", "ClientOperationCanceled", [new("lane", lane), new("operation_id", operationId)]);
            throw;
        }
        catch (Exception ex)
        {
            outcome = "faulted";
            TraceLog.WarnState("YFinanceRuntimeClientFactory", "ClientOperationError", [new("lane", lane), new("operation_id", operationId), new("message", ex.Message)]);
            throw;
        }
        finally
        {
            TraceLog.InfoState("YFinanceRuntimeClientFactory", "ClientOperationComplete", [new("lane", lane), new("operation_id", operationId), new("outcome", outcome)]);
        }
    }

    public static async Task<T> RunSerializedAsync<T>(string lane, Func<YFinanceServerClient, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        => await RunAsync(lane, CreateOperationId(lane), action, cancellationToken).ConfigureAwait(false);

    public static async Task<T> RunSerializedAsync<T>(string lane, string operationId, Func<YFinanceServerClient, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        => await RunAsync(lane, operationId, action, cancellationToken).ConfigureAwait(false);

    public static string CreateOperationId(string lane)
        => $"{lane}-{Interlocked.Increment(ref _operationSequence):D8}";

    /// <summary>
    /// Forces a full shared-client reset after sustained runtime quote failures.
    /// This is intentionally limited to product assemblies that own recovery policy.
    /// </summary>
    /// <remarks>
    /// This method is safe while an operation is in flight because reset only
    /// retires the current shared entry; disposal is deferred until the active
    /// lease is released. New work is serialized through <see cref="RunAsync{T}(string, Func{YFinanceServerClient, CancellationToken, Task{T}}, CancellationToken)"/>.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static void ResetConnectionStateForRecovery(string reason)
    {
        TraceLog.WarnState("YFinanceRuntimeClientFactory", "ClientConnectionResetForRecovery", [new("reason", reason)]);
        ResetConnectionState();
    }

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

    private static SharedClientLease RentSharedClient()
    {
        lock (Sync)
        {
            _sharedClient ??= new SharedClientEntry(CreateClient());
            _sharedClient.ActiveOperations++;
            return new SharedClientLease(_sharedClient.Client, _sharedClient);
        }
    }

    private static YFinanceServerClient CreateClient()
        => new(new YFinanceServerConnectionOptions(
            "127.0.0.1",
            YFinance.NET.Protocol.Constants.ProtocolConstants.DefaultPort,
            TimeSpan.FromSeconds(5),
            PortfolioSaverYFinanceServerClientTraceSink.Instance));

    private static void RetireConnectionState(SharedClientEntry failedEntry)
    {
        List<YFinanceServerClient> disposeNow = [];
        lock (Sync)
        {
            if (ReferenceEquals(_sharedClient, failedEntry))
            {
                _sharedClient = null;
                _helloCompleted = false;
                _serverReadyEnsured = false;
            }

            failedEntry.Retired = true;
            if (failedEntry.ActiveOperations == 0)
            {
                RetiredClients.Remove(failedEntry);
                disposeNow.Add(failedEntry.Client);
            }
            else if (!RetiredClients.Contains(failedEntry))
                RetiredClients.Add(failedEntry);
        }

        DisposeClients(disposeNow);
    }

    private static void ReleaseSharedClientOperation(SharedClientEntry entry)
    {
        List<YFinanceServerClient> disposeNow = [];
        lock (Sync)
        {
            if (entry.ActiveOperations > 0)
                entry.ActiveOperations--;

            if (entry.ActiveOperations == 0 && entry.Retired)
            {
                RetiredClients.Remove(entry);
                disposeNow.Add(entry.Client);
            }
        }

        DisposeClients(disposeNow);
    }

    private static void ResetConnectionState()
    {
        SharedClientEntry? sharedClient;
        lock (Sync)
        {
            sharedClient = _sharedClient;
            _sharedClient = null;
            _helloCompleted = false;
            _serverReadyEnsured = false;
        }

        // Reset may be requested while an operation holds a lease. Retiring the
        // entry marks it unusable for future work, but disposal is deferred until
        // ReleaseSharedClientOperation observes that the active lease is gone.
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

    private sealed class SharedClientEntry(YFinanceServerClient client)
    {
        public YFinanceServerClient Client { get; } = client;
        public int ActiveOperations { get; set; }
        public bool Retired { get; set; }
    }

    private readonly record struct SharedClientLease(YFinanceServerClient Client, SharedClientEntry Entry);
}
