using System.IO;
using System.Net.Http;
using System.Text;
using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Media.Services;

public sealed class ExchangePhotoCacheService
{
    private const string BundledFolderName = "ExchangeBackgrounds";

    private readonly BackgroundImageService _backgroundImageService = new();
    private readonly IReadOnlyList<ExchangePhotoCatalogEntry> _catalog =
    [
        new(
            "new-york-stock-exchange.jpg",
            "New York Stock Exchange",
            "NYC - New York Stock Exchange.JPG",
            "NYC - New York Stock Exchange | Jean-Christophe BENOIST | CC BY 3.0 | https://commons.wikimedia.org/wiki/File:NYC_-_New_York_Stock_Exchange.JPG"),
        new(
            "frankfurt-stock-exchange.jpg",
            "Frankfurt Stock Exchange",
            "Frankfurt Stock Exchange.jpg",
            "Frankfurt Stock Exchange | Dietmar Rabich | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Frankfurt_Stock_Exchange.jpg"),
        new(
            "tokyo-stock-exchange.jpg",
            "Tokyo Stock Exchange",
            "Outside Tokyo Stock Exchange (59463).jpg",
            "Outside Tokyo Stock Exchange | Syced | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Outside_Tokyo_Stock_Exchange_(59463).jpg"),
        new(
            "euronext-amsterdam.jpg",
            "Euronext Amsterdam",
            "Beursplein 5.jpg",
            "Beursplein 5 / Euronext Amsterdam | GerardM | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Beursplein_5.jpg"),
        new(
            "australian-securities-exchange.jpg",
            "Australian Securities Exchange",
            "Australian Securities Exchange entrance (cropped).jpg",
            "Australian Securities Exchange entrance | Jason7825 | Public domain | https://commons.wikimedia.org/wiki/File:Australian_Securities_Exchange_entrance_(cropped).jpg"),
        new(
            "bombay-stock-exchange.jpg",
            "Bombay Stock Exchange",
            "Bombay-Stock-Exchange.jpg",
            "Bombay Stock Exchange | Nichalp | CC BY-SA | https://commons.wikimedia.org/wiki/File:Bombay-Stock-Exchange.jpg"),
        new(
            "shanghai-stock-exchange.jpg",
            "Shanghai Stock Exchange",
            "Shanghai Stock Exchange 20140630 200642.jpg",
            "Shanghai Stock Exchange | Qa003qa003 | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Shanghai_Stock_Exchange_20140630_200642.jpg"),
        new(
            "hong-kong-stock-exchange.jpg",
            "Hong Kong Stock Exchange",
            "Exchange Square (äº¤æ˜“å»£å¡²), Hong Kong Stock Exchange, Hong Kong SAR, China (Ank Kumar, Infosys Limited) 01.jpg",
            "Hong Kong Stock Exchange / Exchange Square | Ank Kumar | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Exchange_Square_(äº¤æ˜“å»£å¡²),_Hong_Kong_Stock_Exchange,_Hong_Kong_SAR,_China_(Ank_Kumar,_Infosys_Limited)_01.jpg"),
        new(
            "london-skyline-public-domain.jpg",
            "London skyline",
            "London Skyline (199638369).jpeg",
            "London Skyline | Rodrigo.Argenton | CC0 | https://commons.wikimedia.org/wiki/File:London_Skyline_(199638369).jpeg"),
        new(
            "frankfurt-skyline-public-domain.jpg",
            "Frankfurt skyline",
            "Frankfurt Skyline Wikimedia Commons.jpg",
            "Frankfurt Skyline | WikimediaImages | CC0 | bundled public-domain skyline starter"),
        new(
            "shanghai-skyline-public-domain.jpg",
            "Shanghai skyline from the Bund",
            "Shanghai skyline from the bund.jpg",
            "Shanghai skyline from the Bund | Pxfuel | CC0 | https://commons.wikimedia.org/wiki/File:Shanghai_skyline_from_the_bund.jpg"),
        new(
            "hong-kong-skyline-public-domain.jpg",
            "Hong Kong skyline",
            "Hong Kong skyline Wikimedia Commons.jpg",
            "Hong Kong skyline | Piqsels | CC0 | bundled public-domain skyline starter"),
        new(
            "sydney-skyline-public-domain.jpg",
            "Sydney skyline",
            "Sydney skyline, January 2021.jpg",
            "Sydney skyline, January 2021 | andrew milling | Public Domain Mark | https://commons.wikimedia.org/wiki/File:Sydney_skyline,_January_2021.jpg")
    ];

    public async Task<IReadOnlyList<string>> GetAvailableBackgroundsAsync(
        AppSettings settings,
        HttpClient httpClient,
        bool networkAvailable,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> immediateBackgrounds = GetImmediateBackgrounds(settings);
        if (!networkAvailable)
            return immediateBackgrounds;

        if (settings.UseCustomBackgroundImageFolder && !string.IsNullOrWhiteSpace(settings.CustomBackgroundImageFolder))
            return immediateBackgrounds;

        string cacheFolder = settings.BackgroundImageFolder;
        await TryDownloadNextMissingImageAsync(cacheFolder, httpClient, cancellationToken);

        IReadOnlyList<string> refreshed = GetImages(cacheFolder, includeSubfolders: false);
        return refreshed.Count > 0 ? refreshed : immediateBackgrounds;
    }

    public IReadOnlyList<string> GetImmediateBackgrounds(AppSettings settings)
    {
        if (settings.UseCustomBackgroundImageFolder && !string.IsNullOrWhiteSpace(settings.CustomBackgroundImageFolder))
        {
            IReadOnlyList<string> custom = GetImages(settings.CustomBackgroundImageFolder, settings.BackgroundIncludeSubfolders);
            if (custom.Count > 0)
                return custom;
        }

        string cacheFolder = settings.BackgroundImageFolder;
        Directory.CreateDirectory(cacheFolder);

        CopyBundledStarterImages(cacheFolder);
        WriteAttributionFile(cacheFolder);

        IReadOnlyList<string> cached = GetImages(cacheFolder, includeSubfolders: false);
        if (cached.Count > 0)
            return cached;

        string bundledFolder = GetBundledAssetsFolder();
        return GetImages(bundledFolder, includeSubfolders: false);
    }

    public string GetManagedCacheFolder(AppSettings settings) => settings.BackgroundImageFolder;

    private IReadOnlyList<string> GetImages(string folderPath, bool includeSubfolders)
        => _backgroundImageService.GetImages(folderPath, includeSubfolders)
            .Select(image => image.FilePath)
            .ToList();

    private void CopyBundledStarterImages(string cacheFolder)
    {
        string bundledFolder = GetBundledAssetsFolder();
        if (!Directory.Exists(bundledFolder))
            return;

        foreach (string sourcePath in Directory.EnumerateFiles(bundledFolder))
        {
            string fileName = Path.GetFileName(sourcePath);
            if (fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                continue;

            string targetPath = Path.Combine(cacheFolder, fileName);
            if (!File.Exists(targetPath))
                File.Copy(sourcePath, targetPath, overwrite: false);
        }
    }

    private async Task TryDownloadNextMissingImageAsync(string cacheFolder, HttpClient httpClient, CancellationToken cancellationToken)
    {
        ExchangePhotoCatalogEntry? nextMissing = _catalog.FirstOrDefault(entry => !File.Exists(Path.Combine(cacheFolder, entry.LocalFileName)));
        if (nextMissing is null)
            return;

        string tempPath = Path.Combine(cacheFolder, nextMissing.LocalFileName + ".download");
        string targetPath = Path.Combine(cacheFolder, nextMissing.LocalFileName);

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(nextMissing.DownloadUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream targetStream = File.Create(tempPath);
            await sourceStream.CopyToAsync(targetStream, cancellationToken);
            targetStream.Close();

            if (File.Exists(targetPath))
                File.Delete(targetPath);

            File.Move(tempPath, targetPath);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private void WriteAttributionFile(string cacheFolder)
    {
        string targetPath = Path.Combine(cacheFolder, "exchange-photo-attribution.txt");
        StringBuilder builder = new();
        builder.AppendLine("PortfolioSaver exchange photo cache");
        builder.AppendLine();
        builder.AppendLine("Bundled starter photos and runtime-downloaded photos come from Wikimedia Commons.");
        builder.AppendLine("Attribution details:");
        builder.AppendLine();

        foreach (ExchangePhotoCatalogEntry entry in _catalog)
            builder.AppendLine(entry.AttributionLine);

        File.WriteAllText(targetPath, builder.ToString(), Encoding.UTF8);
    }

    private static string GetBundledAssetsFolder()
        => Path.Combine(AppContext.BaseDirectory, "Assets", BundledFolderName);

    private sealed record ExchangePhotoCatalogEntry(
        string LocalFileName,
        string DisplayName,
        string WikimediaFileName,
        string AttributionLine)
    {
        public string DownloadUrl => $"https://commons.wikimedia.org/wiki/Special:Redirect/file/{Uri.EscapeDataString(WikimediaFileName)}";
    }
}

