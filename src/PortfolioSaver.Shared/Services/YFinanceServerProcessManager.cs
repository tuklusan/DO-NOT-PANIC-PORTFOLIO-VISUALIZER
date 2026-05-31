using System.Diagnostics;
using System.Net.Sockets;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Shared.Services;

public static class YFinanceServerProcessManager
{
    private static readonly SemaphoreSlim Sync = new(1, 1);
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
            (string fileName, string arguments) = ResolveLaunchCommand(token);
            ProcessStartInfo startInfo = new()
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            TraceLog.InfoState("YFinanceServerManager", "ServerLaunchStart", [new("client_type", clientType), new("file_name", fileName), new("arguments", arguments)]);
            _ownedProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start YFinance.NET server process.");
            _launchToken = token;

            for (int attempt = 0; attempt < 30; attempt++)
            {
                if (await CanConnectAsync(cancellationToken).ConfigureAwait(false))
                {
                    TraceLog.InfoState("YFinanceServerManager", "ServerLaunchReady", [new("client_type", clientType), new("pid", _ownedProcess.Id), new("attempt", attempt + 1)]);
                    return;
                }

                if (_ownedProcess.HasExited)
                    throw new InvalidOperationException($"YFinance.NET server exited early with code {_ownedProcess.ExitCode}.");

                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
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
        catch
        {
            return false;
        }
    }

    private static (string FileName, string Arguments) ResolveLaunchCommand(string token)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] exeCandidates =
        [
            Path.Combine(baseDirectory, "YFinance.NET.Server.exe"),
            Path.Combine(baseDirectory, "YFinance.NET.Server", "YFinance.NET.Server.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "server", "YFinance.NET.Server.exe")),
            Path.Combine(GetRepoRoot(), "YFinance.net", "YFinance.NET.Server", "bin", "Release", "net10.0", "YFinance.NET.Server.exe"),
            Path.Combine(GetRepoRoot(), "YFinance.net", "YFinance.NET.Server", "bin", "Debug", "net10.0", "YFinance.NET.Server.exe")
        ];

        foreach (string candidate in exeCandidates)
        {
            if (File.Exists(candidate))
                return (candidate, BuildArguments(token, isDll: false));
        }

        string[] dllCandidates =
        [
            Path.Combine(baseDirectory, "YFinance.NET.Server.dll"),
            Path.Combine(baseDirectory, "YFinance.NET.Server", "YFinance.NET.Server.dll"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "server", "YFinance.NET.Server.dll")),
            Path.Combine(GetRepoRoot(), "YFinance.net", "YFinance.NET.Server", "bin", "Release", "net10.0", "YFinance.NET.Server.dll"),
            Path.Combine(GetRepoRoot(), "YFinance.net", "YFinance.NET.Server", "bin", "Debug", "net10.0", "YFinance.NET.Server.dll")
        ];

        foreach (string candidate in dllCandidates)
        {
            if (File.Exists(candidate))
                return ("dotnet", $"\"{candidate}\" {BuildArguments(token, isDll: true)}");
        }

        throw new FileNotFoundException("Could not locate YFinance.NET.Server executable or DLL.");
    }

    private static string BuildArguments(string token, bool isDll)
        => $"--port 14870 --owned --owner-pid {Environment.ProcessId} --max-clients 1024 --launch-token \"{token}\"";

    private static string GetRepoRoot()
    {
        string? current = Path.GetFullPath(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); i++)
        {
            if (File.Exists(Path.Combine(current, "PortfolioScreensaver.sln")))
                return current;

            string siblingRepo = Path.Combine(current, "repo");
            if (File.Exists(Path.Combine(siblingRepo, "PortfolioScreensaver.sln")))
                return siblingRepo;

            DirectoryInfo? parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
