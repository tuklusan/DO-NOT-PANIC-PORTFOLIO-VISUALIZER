using System.IO;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using PortfolioSaver.Shared.Services;
using YFinance.NET.Client;
using YFinance.NET.Protocol.Dtos;
using YFinance.NET.Protocol.Integrity;
using YFinance.NET.Protocol.Messages;
using YFinance.NET.Protocol.Transport;
using YFinance.NET.Server.Hosting;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class YFinanceClientServerProtocolTests
{
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
        Assert.Equal(DateTimeOffset.Now.Offset, request.Timestamp.Offset);
        Assert.True(ProtocolIntegrity.Verify(request, request.Payload));
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
    public void OwnedModeStartup_IsWiredIntoInteractiveApps()
    {
        string repoRoot = GetRepoRoot();
        string desktopApp = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Desktop", "App.xaml.cs"));
        string configApp = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Config", "App.xaml.cs"));
        string screensaverApp = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Screensaver", "App.xaml.cs"));
        string shutdownQueue = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Shared", "Services", "OwnedServerShutdownQueue.cs"));

        Assert.Contains("YFinanceServerProcessManager.EnsureOwnedServerAsync(\"PortfolioSaver.Desktop\")", desktopApp, StringComparison.Ordinal);
        Assert.Contains("OwnedServerShutdownQueue.QueueShutdown(\"Desktop.App\")", desktopApp, StringComparison.Ordinal);
        Assert.DoesNotContain("StopOwnedServerAsync().GetAwaiter().GetResult()", desktopApp, StringComparison.Ordinal);
        Assert.Contains("YFinanceServerProcessManager.EnsureOwnedServerAsync(\"PortfolioSaver.Config\")", configApp, StringComparison.Ordinal);
        Assert.Contains("OwnedServerShutdownQueue.QueueShutdown(\"Config.App\")", configApp, StringComparison.Ordinal);
        Assert.DoesNotContain("StopOwnedServerAsync().GetAwaiter().GetResult()", configApp, StringComparison.Ordinal);
        Assert.Contains("YFinanceServerProcessManager.EnsureOwnedServerAsync(\"PortfolioSaver.Screensaver\")", screensaverApp, StringComparison.Ordinal);
        Assert.Contains("OwnedServerShutdownQueue.QueueShutdown(\"Screensaver.App\")", screensaverApp, StringComparison.Ordinal);
        Assert.DoesNotContain("StopOwnedServerAsync().GetAwaiter().GetResult()", screensaverApp, StringComparison.Ordinal);
        Assert.Contains("YFinanceServerProcessManager.StopOwnedServerAsync(timeout.Token)", shutdownQueue, StringComparison.Ordinal);
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
    public void ServerOptions_DefaultsToLoopbackBinding()
    {
        ServerOptions options = ServerOptions.Parse([]);

        Assert.Equal(IPAddress.Loopback, options.BindAddress);
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
        string desktopProject = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Desktop", "PortfolioSaver.Desktop.csproj"));
        string configProject = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Config", "PortfolioSaver.Config.csproj"));
        string screensaverProject = File.ReadAllText(Path.Combine(repoRoot, "src", "PortfolioSaver.Screensaver", "PortfolioSaver.Screensaver.csproj"));

        string serverTargets = File.ReadAllText(Path.Combine(repoRoot, "build", "YFinanceServer.targets"));

        Assert.Contains("../../build/YFinanceServer.targets", desktopProject, StringComparison.Ordinal);
        Assert.Contains("../../build/YFinanceServer.targets", configProject, StringComparison.Ordinal);
        Assert.Contains("../../build/YFinanceServer.targets", screensaverProject, StringComparison.Ordinal);
        Assert.Contains("CopyOwnedYFinanceServerToOutput", serverTargets, StringComparison.Ordinal);
        Assert.Contains("CopyOwnedYFinanceServerToPublish", serverTargets, StringComparison.Ordinal);
        Assert.Contains("<OwnedYFinanceServerBundleFolder>YFinanceServer\\</OwnedYFinanceServerBundleFolder>", serverTargets, StringComparison.Ordinal);
        Assert.Contains("$(OwnedYFinanceServerOutput)**\\*", serverTargets, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(baseDirectory, \"YFinanceServer\", \"YFinance.NET.Server.exe\")", launcherSource, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(baseDirectory, \"YFinanceServer\", \"YFinance.NET.Server.dll\")", launcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRepoRoot()", launcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PORTFOLIOSAVER_YFINANCE_SERVER_PATH", launcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("string siblingRepo = Path.Combine(current, \"repo\")", launcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("YFinance.NET.Server\", \"bin\", \"Release\"", launcherSource, StringComparison.Ordinal);
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
    public async Task ServerProcess_RespondsToHealthRequest_OverTcpProtocol()
    {
        using Process server = StartServerProcess(14871);
        try
        {
            await WaitForPortAsync(14871);
            await using YFinanceServerClient client = new(new YFinanceServerConnectionOptions("127.0.0.1", 14871, TimeSpan.FromSeconds(5), NullYFinanceServerClientTraceSink.Instance));
            HelloResponseDto hello = await client.HelloAsync(new HelloRequestDto("PortfolioSaver.Tests", "BETA-6", "TESTHASH", false, Environment.ProcessId));
            HealthResponseDto health = await client.HealthAsync();

            Assert.Equal(14871, hello.ListenerPort);
            Assert.Equal("standalone", hello.Mode);
            Assert.Equal("ok", health.Status);
        }
        finally
        {
            KillProcessIfRunning(server);
        }
    }

    [Fact]
    public async Task DuplicateServerGuard_IsScopedPerPort_NotGlobal()
    {
        using Process serverA = StartServerProcess(14872);
        using Process serverB = StartServerProcess(14873);
        try
        {
            await WaitForPortAsync(14872);
            await WaitForPortAsync(14873);

            await using YFinanceServerClient clientA = new(new YFinanceServerConnectionOptions("127.0.0.1", 14872, TimeSpan.FromSeconds(5), NullYFinanceServerClientTraceSink.Instance));
            await using YFinanceServerClient clientB = new(new YFinanceServerConnectionOptions("127.0.0.1", 14873, TimeSpan.FromSeconds(5), NullYFinanceServerClientTraceSink.Instance));

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

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "PortfolioScreensaver.sln");
            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static Process StartServerProcess(int port)
    {
        string repoRoot = GetRepoRoot();
        string serverDll = Path.Combine(repoRoot, "YFinance.net", "YFinance.NET.Server", "bin", "Release", "net10.0", "YFinance.NET.Server.dll");
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            Arguments = $"\"{serverDll}\" --port {port} --max-clients 16",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start YFinance.NET.Server process.");
    }

    private static async Task WaitForPortAsync(int port)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
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
