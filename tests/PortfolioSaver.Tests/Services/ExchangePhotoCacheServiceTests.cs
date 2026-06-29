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
using System.Net;
using System.Net.Http;
using System.Reflection;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Media.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ExchangePhotoCacheServiceTests
{
    [Fact]
    public void GetImmediateBackgrounds_UsesOnlyCustomFolderWhenEnabled()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string customFolder = Path.Combine(tempRoot, "custom");
        string nestedFolder = Path.Combine(customFolder, "nested");
        Directory.CreateDirectory(nestedFolder);
        string imagePath = Path.Combine(customFolder, "custom-one.jpg");
        string nestedImagePath = Path.Combine(nestedFolder, "custom-two.jpg");
        File.WriteAllBytes(imagePath, CreateMinimalJpegBytes());
        File.WriteAllBytes(nestedImagePath, CreateMinimalJpegBytes());

        AppSettings settings = Defaults.CreateSettings();
        settings.UseCustomBackgroundImageFolder = true;
        settings.CustomBackgroundImageFolder = customFolder;
        settings.BackgroundIncludeSubfolders = true;

        ExchangePhotoCacheService service = new();
        IReadOnlyList<string> images = service.GetImmediateBackgrounds(settings);

        Assert.Equal(2, images.Count);
        Assert.Contains(imagePath, images, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(nestedImagePath, images, StringComparer.OrdinalIgnoreCase);
        Assert.All(images, path => Assert.StartsWith(customFolder, path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetImmediateBackgrounds_DefaultModeDeletesTmpFilesAndReturnsLocalFilesOnly()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string cacheFolder = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(cacheFolder);
        string existingPath = Path.Combine(cacheFolder, "existing.jpg");
        string partialPath = Path.Combine(cacheFolder, "partial.jpg.TMP");
        File.WriteAllBytes(existingPath, CreateMinimalJpegBytes());
        File.WriteAllBytes(partialPath, CreateMinimalJpegBytes());
        File.SetLastWriteTimeUtc(partialPath, DateTime.UtcNow.AddMinutes(-15));

        AppSettings settings = Defaults.CreateSettings();
        settings.UseCustomBackgroundImageFolder = false;
        settings.BackgroundImageFolder = cacheFolder;

        ExchangePhotoCacheService service = new();
        IReadOnlyList<string> images = service.GetImmediateBackgrounds(settings);

        Assert.False(File.Exists(partialPath));
        Assert.Contains(existingPath, images, StringComparer.OrdinalIgnoreCase);
        Assert.All(images, path =>
        {
            Assert.True(File.Exists(path));
            Assert.False(Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) &&
                         (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
        });
    }

    [Fact]
    public void GetImmediateBackgrounds_DefaultModeKeepsFreshTmpFiles()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string cacheFolder = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(cacheFolder);
        string existingPath = Path.Combine(cacheFolder, "existing.jpg");
        string partialPath = Path.Combine(cacheFolder, "active-download.jpg.TMP");
        File.WriteAllBytes(existingPath, CreateMinimalJpegBytes());
        File.WriteAllBytes(partialPath, CreateMinimalJpegBytes());
        File.SetLastWriteTimeUtc(partialPath, DateTime.UtcNow);

        AppSettings settings = Defaults.CreateSettings();
        settings.UseCustomBackgroundImageFolder = false;
        settings.BackgroundImageFolder = cacheFolder;

        ExchangePhotoCacheService service = new();
        IReadOnlyList<string> images = service.GetImmediateBackgrounds(settings);

        Assert.True(File.Exists(partialPath));
        Assert.Contains(existingPath, images, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(partialPath, images, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WarmDefaultManifestCacheAsync_DownloadsMissingImagesWithTmpThenFinalRename()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string cacheFolder = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(cacheFolder);

        AppSettings settings = Defaults.CreateSettings();
        settings.UseCustomBackgroundImageFolder = false;
        settings.BackgroundImageFolder = cacheFolder;

        using HttpClient httpClient = new(new StaticImageHandler());
        ExchangePhotoCacheService service = new(() => httpClient);

        await service.WarmDefaultManifestCacheAsync(settings);

        string attributionPath = Path.Combine(cacheFolder, "exchange-photo-attribution.txt");
        string attributionManifest = File.ReadAllText(attributionPath);
        Assert.True(File.Exists(attributionPath));
        Assert.Contains("Download manifest:", attributionManifest, StringComparison.Ordinal);
        Assert.Contains("https://commons.wikimedia.org/wiki/File:", attributionManifest, StringComparison.Ordinal);
        Assert.True(Directory.EnumerateFiles(cacheFolder, "*.jpg", SearchOption.TopDirectoryOnly).Count() >= 4);
        Assert.Empty(Directory.EnumerateFiles(cacheFolder, "*.TMP", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task WarmDefaultManifestCacheAsync_SerializesConcurrentWarmups()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string cacheFolder = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(cacheFolder);

        AppSettings settings = Defaults.CreateSettings();
        settings.UseCustomBackgroundImageFolder = false;
        settings.BackgroundImageFolder = cacheFolder;

        ConcurrentTrackingImageHandler handler = new(TimeSpan.FromMilliseconds(15));
        ExchangePhotoCacheService service = new(() => new HttpClient(handler, disposeHandler: false));

        await Task.WhenAll(
            service.WarmDefaultManifestCacheAsync(settings),
            service.WarmDefaultManifestCacheAsync(settings));

        Assert.True(handler.MaxConcurrentRequests <= 1, $"Expected serialized downloads, saw {handler.MaxConcurrentRequests} concurrent requests.");
        Assert.Empty(Directory.EnumerateFiles(cacheFolder, "*.TMP", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task GetAvailableBackgroundsAsync_StartsWarmupButReturnsOnlyImmediateLocalFiles()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string cacheFolder = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(cacheFolder);
        string existingPath = Path.Combine(cacheFolder, "existing.jpg");
        File.WriteAllBytes(existingPath, CreateMinimalJpegBytes());

        AppSettings settings = Defaults.CreateSettings();
        settings.UseCustomBackgroundImageFolder = false;
        settings.BackgroundImageFolder = cacheFolder;

        using HttpClient httpClient = new(new StaticImageHandler());
        ExchangePhotoCacheService service = new(() => httpClient);

        IReadOnlyList<string> images = await service.GetAvailableBackgroundsAsync(settings, httpClient, networkAvailable: true);

        Assert.Contains(existingPath, images, StringComparer.OrdinalIgnoreCase);
        Assert.All(images, path => Assert.True(File.Exists(path), path));
        Assert.DoesNotContain(images, path => path.StartsWith("http", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetAvailableBackgroundsAsync_BackgroundWarmupHonorsCancellationAndReleasesGate()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string cacheFolder = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(cacheFolder);
        string existingPath = Path.Combine(cacheFolder, "existing.jpg");
        File.WriteAllBytes(existingPath, CreateMinimalJpegBytes());

        AppSettings settings = Defaults.CreateSettings();
        settings.UseCustomBackgroundImageFolder = false;
        settings.BackgroundImageFolder = cacheFolder;

        BlockingImageHandler handler = new();
        using HttpClient httpClient = new(handler);
        ExchangePhotoCacheService service = new(() => httpClient);
        using CancellationTokenSource cts = new();

        IReadOnlyList<string> images = await service.GetAvailableBackgroundsAsync(settings, httpClient, networkAvailable: true, cancellationToken: cts.Token);
        Task warmup = service.CurrentDefaultManifestWarmupTask ?? throw new InvalidOperationException("Background warmup was not started.");
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();

        await warmup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(warmup.IsCompletedSuccessfully);
        Assert.Contains(existingPath, images, StringComparer.OrdinalIgnoreCase);

        handler.BlockRequests = false;
        using CancellationTokenSource retryCts = new(TimeSpan.FromSeconds(10));
        await service.WarmDefaultManifestCacheAsync(settings, retryCts.Token);
        Assert.Empty(Directory.EnumerateFiles(cacheFolder, "*.TMP", SearchOption.TopDirectoryOnly));
    }


    [Fact]
    public void GetFooterAttributionsForBackgrounds_MapsBundledAndDownloadedCacheFiles()
    {
        string cacheFolder = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string starterPath = Path.Combine(cacheFolder, "new-york-stock-exchange.jpg");
        string downloadedPath = Path.Combine(cacheFolder, "frankfurt-skyline-2022.jpg");
        string customPath = Path.Combine(cacheFolder, "family-photo.jpg");

        ExchangePhotoCacheService service = new();
        IReadOnlyDictionary<string, string> attributions = service.GetFooterAttributionsForBackgrounds([starterPath, downloadedPath, customPath]);

        Assert.Equal(2, attributions.Count);
        Assert.Equal("Jean-Christophe BENOIST, CC BY 3.0", attributions[starterPath]);
        Assert.Equal("Jorg Braukmann, CC BY-SA 4.0", attributions[downloadedPath]);
        Assert.All(attributions.Values, attribution =>
        {
            Assert.DoesNotContain("http", attribution, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("|", attribution, StringComparison.Ordinal);
        });
        Assert.False(attributions.ContainsKey(customPath));
    }

    [Fact]
    public void GetFullAttributionsForBackgrounds_PreservesSourceMetadata()
    {
        string cacheFolder = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string downloadedPath = Path.Combine(cacheFolder, "frankfurt-skyline-2022.jpg");

        ExchangePhotoCacheService service = new();
        IReadOnlyDictionary<string, string> attributions = service.GetFullAttributionsForBackgrounds([downloadedPath]);

        Assert.Single(attributions);
        Assert.Contains("Frankfurt Skyline 2022", attributions[downloadedPath], StringComparison.Ordinal);
        Assert.Contains("Jorg Braukmann", attributions[downloadedPath], StringComparison.Ordinal);
        Assert.Contains("CC BY-SA 4.0", attributions[downloadedPath], StringComparison.Ordinal);
        Assert.Contains("https://commons.wikimedia.org/wiki/File:", attributions[downloadedPath], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Image title | Creator Name | CC0 | https://example.invalid/image", "Creator Name, CC0")]
    [InlineData("Creator Name | CC0", "Creator Name, CC0")]
    [InlineData("Image title |  | CC0 | https://example.invalid/image", "Unknown, Unknown license")]
    [InlineData("Image title | Creator Name |  | https://example.invalid/image", "Unknown, Unknown license")]
    [InlineData("", "Unknown, Unknown license")]
    [InlineData(null, "Unknown, Unknown license")]
    public void FooterAttributionFormatter_UsesShortSafeDisplayShape(string? attributionLine, string expected)
    {
        Assert.Equal(expected, InvokeFooterAttributionFormatter(attributionLine));
    }

    [Fact]
    public async Task WarmDefaultManifestCacheAsync_RejectsNonJpegDownloads()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string cacheFolder = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(cacheFolder);

        AppSettings settings = Defaults.CreateSettings();
        settings.UseCustomBackgroundImageFolder = false;
        settings.BackgroundImageFolder = cacheFolder;

        using HttpClient httpClient = new(new StaticImageHandler([0x4E, 0x4F, 0x50, 0x45]));
        ExchangePhotoCacheService service = new(() => httpClient);

        await service.WarmDefaultManifestCacheAsync(settings);

        Assert.Empty(Directory.EnumerateFiles(cacheFolder, "*.TMP", SearchOption.TopDirectoryOnly));
        Assert.False(File.Exists(Path.Combine(cacheFolder, "shanghai-skyline-2007.jpg")));
    }

    [Fact]
    public async Task WarmDefaultManifestCacheAsync_StopsPromptlyWhenDownloadIsCanceled()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string cacheFolder = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(cacheFolder);

        AppSettings settings = Defaults.CreateSettings();
        settings.UseCustomBackgroundImageFolder = false;
        settings.BackgroundImageFolder = cacheFolder;

        BlockingImageHandler handler = new();
        using HttpClient httpClient = new(handler);
        ExchangePhotoCacheService service = new(() => httpClient);
        using CancellationTokenSource cts = new();

        Task warmup = service.WarmDefaultManifestCacheAsync(settings, cts.Token);
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => warmup.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(Directory.EnumerateFiles(cacheFolder, "*.TMP", SearchOption.TopDirectoryOnly));
    }

    private static byte[] CreateMinimalJpegBytes()
        => [0xFF, 0xD8, 0xFF, 0xD9];

    private static string InvokeFooterAttributionFormatter(string? attributionLine)
    {
        // Reflection keeps the edge-case checks close to the private formatter without widening production API surface.
        MethodInfo method = typeof(ExchangePhotoCacheService).GetMethod("ToFooterAttribution", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ToFooterAttribution formatter was not found.");

        return Assert.IsType<string>(method.Invoke(null, [attributionLine]));
    }

    private sealed class StaticImageHandler(byte[]? responseBytes = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responseBytes ?? CreateMinimalJpegBytes())
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ConcurrentTrackingImageHandler(TimeSpan delay) : HttpMessageHandler
    {
        private int _currentRequests;
        private int _maxConcurrentRequests;

        public int MaxConcurrentRequests => _maxConcurrentRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int current = Interlocked.Increment(ref _currentRequests);
            try
            {
                int snapshot;
                do
                {
                    snapshot = _maxConcurrentRequests;
                    if (current <= snapshot)
                        break;
                }
                while (Interlocked.CompareExchange(ref _maxConcurrentRequests, current, snapshot) != snapshot);

                await Task.Delay(delay, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateMinimalJpegBytes())
                };
            }
            finally
            {
                Interlocked.Decrement(ref _currentRequests);
            }
        }
    }

    private sealed class BlockingImageHandler : HttpMessageHandler
    {
        public TaskCompletionSource<bool> RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool BlockRequests = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestStarted.TrySetResult(true);
            if (BlockRequests)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(CreateMinimalJpegBytes())
            };
        }
    }
}
