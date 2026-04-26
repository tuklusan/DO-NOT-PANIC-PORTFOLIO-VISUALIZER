using System.Net;
using System.Net.Http;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Media.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ExchangePhotoCacheServiceTests
{
    [Fact]
    public void GetImmediateBackgrounds_UsesCustomFolderWhenEnabled()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string customFolder = Path.Combine(tempRoot, "custom");
        Directory.CreateDirectory(customFolder);
        string imagePath = Path.Combine(customFolder, "custom-one.jpg");
        File.WriteAllBytes(imagePath, CreateMinimalJpegBytes());

        AppSettings settings = Defaults.CreateSettings();
        settings.UseCustomBackgroundImageFolder = true;
        settings.CustomBackgroundImageFolder = customFolder;
        settings.BackgroundIncludeSubfolders = false;

        ExchangePhotoCacheService service = new();
        IReadOnlyList<string> images = service.GetImmediateBackgrounds(settings);

        Assert.Single(images);
        Assert.Equal(imagePath, images[0], ignoreCase: true);
    }

    [Fact]
    public async Task GetAvailableBackgroundsAsync_DownloadsMissingImagesOneByOneAndWritesAttribution()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string cacheFolder = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(cacheFolder);

        AppSettings settings = Defaults.CreateSettings();
        settings.UseCustomBackgroundImageFolder = false;
        settings.BackgroundImageFolder = cacheFolder;

        using HttpClient httpClient = new(new StaticImageHandler());
        ExchangePhotoCacheService service = new();

        _ = await service.GetAvailableBackgroundsAsync(settings, httpClient, networkAvailable: true);
        int firstCount = Directory.EnumerateFiles(cacheFolder, "*.jpg", SearchOption.TopDirectoryOnly).Count();

        _ = await service.GetAvailableBackgroundsAsync(settings, httpClient, networkAvailable: true);
        int secondCount = Directory.EnumerateFiles(cacheFolder, "*.jpg", SearchOption.TopDirectoryOnly).Count();

        string attributionPath = Path.Combine(cacheFolder, "exchange-photo-attribution.txt");
        Assert.True(File.Exists(attributionPath));
        Assert.True(firstCount >= 1);
        Assert.Equal(firstCount + 1, secondCount);
    }

    private static byte[] CreateMinimalJpegBytes()
        => [0xFF, 0xD8, 0xFF, 0xD9];

    private sealed class StaticImageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(CreateMinimalJpegBytes())
            };
            return Task.FromResult(response);
        }
    }
}
