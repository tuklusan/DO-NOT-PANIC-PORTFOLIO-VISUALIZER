using System.IO;
using System.Diagnostics;
using System.Text.Json;
using YFinance.NET.Client;
using YFinance.NET.Protocol.Dtos;
using YFinance.NET.Protocol.Integrity;
using YFinance.NET.Protocol.Messages;
using YFinance.NET.Protocol.Transport;
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

        Assert.Contains("YFinanceServerProcessManager.EnsureOwnedServerAsync(\"PortfolioSaver.Desktop\")", desktopApp, StringComparison.Ordinal);
        Assert.Contains("YFinanceServerProcessManager.StopOwnedServerAsync()", desktopApp, StringComparison.Ordinal);
        Assert.Contains("YFinanceServerProcessManager.EnsureOwnedServerAsync(\"PortfolioSaver.Config\")", configApp, StringComparison.Ordinal);
        Assert.Contains("YFinanceServerProcessManager.StopOwnedServerAsync()", configApp, StringComparison.Ordinal);
        Assert.Contains("YFinanceServerProcessManager.EnsureOwnedServerAsync(\"PortfolioSaver.Screensaver\")", screensaverApp, StringComparison.Ordinal);
        Assert.Contains("YFinanceServerProcessManager.StopOwnedServerAsync()", screensaverApp, StringComparison.Ordinal);
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
}
