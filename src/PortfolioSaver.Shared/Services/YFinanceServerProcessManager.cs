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
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net.Sockets;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Shared.Services;

public static class YFinanceServerProcessManager
{
    private static readonly TimeSpan ServerStartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ServerStartupPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly SemaphoreSlim Sync = new(1, 1);
    private static readonly IEqualityComparer<string> PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly ConcurrentDictionary<string, string> ResolvedServerPathByBaseDirectory = new(PathComparer);
    private static Process? _ownedProcess;
    private static string? _launchToken;

    public static async Task EnsureOwnedServerAsync(string clientType, CancellationToken cancellationToken = default)
    {
        await Sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ownedProcess is not null)
            {
                if (!_ownedProcess.HasExited)
                {
                    TraceLog.InfoState("YFinanceServerManager", "OwnedServerAlreadyRunning", [new("client_type", clientType), new("pid", _ownedProcess.Id)]);
                    return;
                }

                _ownedProcess.Dispose();
                _ownedProcess = null;
                _launchToken = null;
            }

            if (await CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                TraceLog.InfoState("YFinanceServerManager", "ServerAlreadyReachable", [new("client_type", clientType)]);
                return;
            }

            string token = $"{clientType}-{Environment.ProcessId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            (string fileName, string arguments, string traceArguments) = ResolveLaunchCommand(token);
            ProcessStartInfo startInfo = new()
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            TraceLog.InfoState("YFinanceServerManager", "ServerLaunchStart", [new("client_type", clientType), new("file_name", fileName), new("arguments", traceArguments)]);
            try
            {
                _ownedProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null.");
            }
            catch (Exception ex)
            {
                TraceLog.ErrorState(
                    "YFinanceServerManager",
                    "ServerLaunchFailed",
                    [new("client_type", clientType), new("file_name", fileName), new("arguments", traceArguments)],
                    ex);
                throw new InvalidOperationException("Failed to start YFinance.NET server process.", ex);
            }

            _launchToken = token;

            Stopwatch startupStopwatch = Stopwatch.StartNew();
            int attempt = 0;
            while (startupStopwatch.Elapsed < ServerStartupTimeout)
            {
                attempt++;
                if (await CanConnectAsync(cancellationToken).ConfigureAwait(false))
                {
                    TraceLog.InfoState("YFinanceServerManager", "ServerLaunchReady", [new("client_type", clientType), new("pid", _ownedProcess.Id), new("attempt", attempt), new("elapsed_milliseconds", Math.Round(startupStopwatch.Elapsed.TotalMilliseconds, 0))]);
                    return;
                }

                if (_ownedProcess.HasExited)
                    throw new InvalidOperationException($"YFinance.NET server exited early with code {_ownedProcess.ExitCode}.");

                TimeSpan remaining = ServerStartupTimeout - startupStopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    break;

                await Task.Delay(remaining < ServerStartupPollInterval ? remaining : ServerStartupPollInterval, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException("Timed out waiting for YFinance.NET server to start listening.");
        }
        finally
        {
            Sync.Release();
        }
    }

    public static async Task StopOwnedServerAsync(CancellationToken cancellationToken = default)
    {
        await Sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ownedProcess is null)
                return;

            try
            {
                if (!_ownedProcess.HasExited)
                {
                    TraceLog.InfoState("YFinanceServerManager", "ServerStopStart", [new("pid", _ownedProcess.Id)]);
                    _ownedProcess.Kill(entireProcessTree: true);
                    await _ownedProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                TraceLog.WarnState("YFinanceServerManager", "ServerStopFailed", [new("message", ex.Message)]);
            }
            finally
            {
                _ownedProcess.Dispose();
                _ownedProcess = null;
                _launchToken = null;
            }
        }
        finally
        {
            Sync.Release();
        }
    }

    private static async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            using TcpClient client = new();
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(500));
            await client.ConnectAsync("127.0.0.1", 14870, timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    internal static (string FileName, string Arguments, string TraceArguments) ResolveLaunchCommand(string token, string? baseDirectoryOverride = null)
    {
        string baseDirectory = Path.GetFullPath(baseDirectoryOverride ?? AppContext.BaseDirectory);
        if (TryGetCachedLaunchPath(baseDirectory) is string cachedPath)
            return BuildLaunchCommandForPath(cachedPath, token);

        string[] exeCandidates =
        [
            Path.Combine(baseDirectory, "YFinanceServer", "YFinance.NET.Server.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "server", "YFinance.NET.Server.exe"))
        ];

        foreach (string candidate in exeCandidates)
        {
            if (File.Exists(candidate))
            {
                CacheResolvedLaunchPath(baseDirectory, candidate);
                return BuildLaunchCommandForPath(candidate, token);
            }
        }

        string[] dllCandidates =
        [
            Path.Combine(baseDirectory, "YFinanceServer", "YFinance.NET.Server.dll"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "server", "YFinance.NET.Server.dll"))
        ];

        foreach (string candidate in dllCandidates)
        {
            if (File.Exists(candidate))
            {
                CacheResolvedLaunchPath(baseDirectory, candidate);
                return BuildLaunchCommandForPath(candidate, token);
            }
        }

        throw new FileNotFoundException("Could not locate YFinance.NET.Server executable or DLL. Expected the owned server bundle under the application YFinanceServer folder or the publish sibling server folder.");
    }

    private static void CacheResolvedLaunchPath(string baseDirectory, string path)
        => ResolvedServerPathByBaseDirectory[baseDirectory] = path;

    private static string? TryGetCachedLaunchPath(string baseDirectory)
    {
        if (!ResolvedServerPathByBaseDirectory.TryGetValue(baseDirectory, out string? cachedPath))
            return null;

        if (File.Exists(cachedPath))
            return cachedPath;

        ResolvedServerPathByBaseDirectory.TryRemove(new KeyValuePair<string, string>(baseDirectory, cachedPath));
        return null;
    }

    private static (string FileName, string Arguments, string TraceArguments) BuildLaunchCommandForPath(string path, string token)
    {
        bool isDll = string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase);
        string arguments = BuildArguments(token);
        string traceArguments = BuildArguments("<redacted>");
        return isDll
            ? ("dotnet", $"\"{path}\" {arguments}", $"\"{path}\" {traceArguments}")
            : (path, arguments, traceArguments);
    }

    private static string BuildArguments(string token)
        => $"--port 14870 --owned --owner-pid {Environment.ProcessId} --max-clients 1024 --launch-token \"{token}\"";

}
