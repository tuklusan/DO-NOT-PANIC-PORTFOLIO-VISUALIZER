using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Media.Services;

public sealed class ExchangePhotoCacheService
{
    private const string BundledFolderName = "ExchangeBackgrounds";
    private static readonly IReadOnlySet<string> BundledStarterFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "new-york-stock-exchange.jpg",
        "london-skyline-public-domain.jpg",
        "shanghai-skyline-public-domain.jpg"
    };

    private static readonly IReadOnlyDictionary<string, string> BundledStarterAttributions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["new-york-stock-exchange.jpg"] = "NYC - New York Stock Exchange | Jean-Christophe BENOIST | CC BY 3.0 | https://commons.wikimedia.org/wiki/File:NYC_-_New_York_Stock_Exchange.JPG",
        ["london-skyline-public-domain.jpg"] = "London Skyline | Rodrigo.Argenton | CC0 | https://commons.wikimedia.org/wiki/File:London_Skyline_(199638369).jpeg",
        ["shanghai-skyline-public-domain.jpg"] = "Shanghai skyline from the Bund | Pxfuel | CC0 | https://commons.wikimedia.org/wiki/File:Shanghai_skyline_from_the_bund.jpg"
    };
    private readonly BackgroundImageService _backgroundImageService = new();
    private readonly Func<HttpClient> _httpClientFactory;
    private readonly object _downloadLock = new();
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private Task? _downloadTask;

    private static readonly IReadOnlyList<ExchangePhotoCatalogEntry> Catalog =
    [
        new("shanghai-skyline.jpg", "Shanghai skyline", "https://upload.wikimedia.org/wikipedia/commons/2/22/Shanghai_skyline.jpg", "Shanghai skyline | Rose Abrams | CC BY 4.0 | https://commons.wikimedia.org/wiki/File:Shanghai_skyline.jpg"),
        new("shanghai-skyline-2007.jpg", "Shanghai skyline 2007", "https://upload.wikimedia.org/wikipedia/commons/0/07/Shanghai_skyline_2007.jpg", "Shanghai skyline 2007 | Roberto67xxx | Public domain | https://commons.wikimedia.org/wiki/File:Shanghai_skyline_2007.jpg"),
        new("lower-manhattan-skyline-2017.jpg", "Lower Manhattan skyline", "https://upload.wikimedia.org/wikipedia/commons/f/f7/Lower_Manhattan_skyline_-_June_2017.jpg", "Lower Manhattan skyline | MusikAnimal | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Lower_Manhattan_skyline_-_June_2017.jpg"),
        new("toronto-sunset-skyline-panorama.jpg", "Toronto sunset skyline panorama", "https://upload.wikimedia.org/wikipedia/commons/3/3c/Sunset_Toronto_Skyline_Panorama_Crop_from_Snake_Island.jpg", "Toronto sunset skyline panorama | Jchmrt | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Sunset_Toronto_Skyline_Panorama_Crop_from_Snake_Island.jpg"),
        new("toronto-skyline-trillium-park.jpg", "Toronto skyline from Trillium Park", "https://upload.wikimedia.org/wikipedia/commons/8/86/Toronto_skyline_viewed_from_Trillium_Park.jpg", "Toronto skyline from Trillium Park | Maksim Sokolov (maxergon.com) | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Toronto_skyline_viewed_from_Trillium_Park.jpg"),
        new("hong-kong-skyline-restitch.jpg", "Hong Kong Skyline Restitch", "https://upload.wikimedia.org/wikipedia/commons/2/23/Hong_Kong_Skyline_Restitch_-_Dec_2007.jpg", "Hong Kong Skyline Restitch | Diliff | CC BY 3.0 | https://commons.wikimedia.org/wiki/File:Hong_Kong_Skyline_Restitch_-_Dec_2007.jpg"),
        new("hong-kong-exchange-square-night.jpg", "Hong Kong Exchange Square night", "https://upload.wikimedia.org/wikipedia/commons/5/59/%E2%80%9C%E9%A6%99%E6%B8%AF%E4%B8%AD%E7%92%B0%E4%BA%A4%E6%98%93%E5%BB%A3%E5%A0%B4_Exchange_Square%2C_Central%2C_Hong_Kong%E2%80%9D_%E5%9F%8E%E5%B8%82%E5%BB%BA%E7%AF%89%E5%A4%9C%E4%B9%8B%E5%BD%A2_Urban_Architecture_Forms_at_Night_SML.20130209.7D.21836.P1.L1.BW_%288478502166%29.jpg", "Hong Kong Exchange Square night architecture | See-ming Lee | CC BY 2.0 | https://commons.wikimedia.org/wiki/File:%E2%80%9C%E9%A6%99%E6%B8%AF%E4%B8%AD%E7%92%B0%E4%BA%A4%E6%98%93%E5%BB%A3%E5%A0%B4_Exchange_Square%2C_Central%2C_Hong_Kong%E2%80%9D_%E5%9F%8E%E5%B8%82%E5%BB%BA%E7%AF%89%E5%A4%9C%E4%B9%8B%E5%BD%A2_Urban_Architecture_Forms_at_Night_SML.20130209.7D.21836.P1.L1.BW_(8478502166).jpg"),
        new("singapore-skyline-2019.jpg", "Singapore Skyline 2019", "https://upload.wikimedia.org/wikipedia/commons/2/2e/Singapore_Skyline_2019-10.jpg", "Singapore Skyline 2019-10 | Unwicked | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Singapore_Skyline_2019-10.jpg"),
        new("singapore-downtown-core.jpg", "Singapore Downtown Core", "https://upload.wikimedia.org/wikipedia/commons/8/8f/Singapore%2C_Downtown_Core_%28I%29.jpg", "Singapore Downtown Core | Supanut Arunoprayote | CC BY 4.0 | https://commons.wikimedia.org/wiki/File:Singapore,_Downtown_Core_(I).jpg"),
        new("dubai-skyline-burj-khalifa.jpg", "Dubai skyline", "https://upload.wikimedia.org/wikipedia/commons/c/cc/Dubai_Skyline_mit_Burj_Khalifa_%2818241030269%29.jpg", "Dubai skyline | Tim Reckmann | CC BY 2.0 | https://commons.wikimedia.org/wiki/File:Dubai_Skyline_mit_Burj_Khalifa_(18241030269).jpg"),
        new("tokyo-skyline-skytree.jpg", "Tokyo skyline from Skytree", "https://upload.wikimedia.org/wikipedia/commons/9/98/Tokyo_skyline_seen_from_Tokyo_Skytree.jpg", "Tokyo skyline from Skytree | Ruthsic | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Tokyo_skyline_seen_from_Tokyo_Skytree.jpg"),
        new("tokyo-sunset-skyline.jpg", "Tokyo sunset skyline", "https://upload.wikimedia.org/wikipedia/commons/a/a6/Tokyo_-_Sunset_Skyline.jpg", "Tokyo sunset skyline | Fred Cherrygarden | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Tokyo_-_Sunset_Skyline.jpg"),
        new("tokyo-minato-night.jpg", "Tokyo Minato night", "https://upload.wikimedia.org/wikipedia/commons/7/75/Minato_City%2C_Tokyo%2C_Japan_%28Night%29.jpg", "Tokyo Minato night | David Kernan | CC BY 4.0 | https://commons.wikimedia.org/wiki/File:Minato_City,_Tokyo,_Japan_(Night).jpg"),
        new("seoul-skyline-night-2018.jpg", "Seoul Skyline Night 2018", "https://upload.wikimedia.org/wikipedia/commons/1/14/Seoul_Skyline_Night_2018.jpg", "Seoul Skyline Night 2018 | mauveine.kim | CC0 | https://commons.wikimedia.org/wiki/File:Seoul_Skyline_Night_2018.jpg"),
        new("han-river-seoul-skyline.jpg", "Han River Seoul skyline", "https://upload.wikimedia.org/wikipedia/commons/c/cc/Han_River_Seoul_skyline_Pixabay_1214950.jpg", "Han River Seoul skyline | USAGI_POST | CC0 | https://commons.wikimedia.org/wiki/File:Han_River_Seoul_skyline_Pixabay_1214950.jpg"),
        new("frankfurt-skyline-2022-night.jpg", "Frankfurt Skyline 2022 night", "https://upload.wikimedia.org/wikipedia/commons/e/eb/Frankfurt_Skyline_2022_bei_Nacht.jpg", "Frankfurt Skyline 2022 night | Jorg Braukmann | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Frankfurt_Skyline_2022_bei_Nacht.jpg"),
        new("frankfurt-skyline-2022.jpg", "Frankfurt Skyline 2022", "https://upload.wikimedia.org/wikipedia/commons/e/ef/Frankfurt_Skyline_2022.jpg", "Frankfurt Skyline 2022 | Jorg Braukmann | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Frankfurt_Skyline_2022.jpg"),
        new("ecb-frankfurt-skyline-dawn.jpg", "ECB and Frankfurt Skyline at dawn", "https://upload.wikimedia.org/wikipedia/commons/5/51/Seat_of_the_European_Central_Bank_and_Frankfurt_Skyline_at_dawn_20150422_1.jpg", "ECB and Frankfurt Skyline at dawn | DXR | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Seat_of_the_European_Central_Bank_and_Frankfurt_Skyline_at_dawn_20150422_1.jpg"),
        new("zurich-skyline-blue-hour.jpg", "Zurich skyline blue hour", "https://upload.wikimedia.org/wikipedia/commons/b/ba/Zurich_skyline_blue_hour.jpg", "Zurich skyline blue hour | Kuhnmi | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Zurich_skyline_blue_hour.jpg"),
        new("zurich-skyline.jpg", "The Zurich skyline", "https://upload.wikimedia.org/wikipedia/commons/5/52/The_Zurich_skyline.jpg", "The Zurich skyline | sdh_zh | CC BY 2.0 | https://commons.wikimedia.org/wiki/File:The_Zurich_skyline.jpg"),
        new("paris-night.jpg", "Paris Night", "https://upload.wikimedia.org/wikipedia/commons/e/e6/Paris_Night.jpg", "Paris Night | Benh LIEU SONG | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Paris_Night.jpg"),
        new("paris-skyline-montmartre.jpg", "Paris skyline from Montmartre", "https://upload.wikimedia.org/wikipedia/commons/3/39/Paris_skyline_from_Montmartre_2026-01-03.jpg", "Paris skyline from Montmartre | Renee Kools | CC BY 4.0 | https://commons.wikimedia.org/wiki/File:Paris_skyline_from_Montmartre_2026-01-03.jpg"),
        new("madrid-skyline.jpg", "Madrid skyline", "https://upload.wikimedia.org/wikipedia/commons/b/b7/Madrid_-_Madrid_skyline_-_140314_195825.jpg", "Madrid skyline | Barcex | CC BY-SA 3.0 | https://commons.wikimedia.org/wiki/File:Madrid_-_Madrid_skyline_-_140314_195825.jpg"),
        new("sydney-skyline-2021.jpg", "Sydney skyline January 2021", "https://upload.wikimedia.org/wikipedia/commons/c/cf/Sydney_skyline%2C_January_2021.jpg", "Sydney skyline January 2021 | andrew milling | Public domain | https://commons.wikimedia.org/wiki/File:Sydney_skyline,_January_2021.jpg"),
        new("mumbai-skyline-night.jpg", "Mumbai Skyline at Night", "https://upload.wikimedia.org/wikipedia/commons/1/14/Mumbai_Skyline_at_Night.jpg", "Mumbai Skyline at Night | Cididity Hat | CC BY-SA 3.0 | https://commons.wikimedia.org/wiki/File:Mumbai_Skyline_at_Night.jpg"),
        new("sao-paulo-skyline.jpg", "Sao Paulo skyline", "https://upload.wikimedia.org/wikipedia/commons/1/15/S%C3%A3o_Paulo_skyline.jpg", "Sao Paulo skyline | Beatriz Posada Alonso | CC BY-SA 3.0 | https://commons.wikimedia.org/wiki/File:S%C3%A3o_Paulo_skyline.jpg"),
        new("johannesburg-skyline.jpg", "Johannesburg skyline", "https://upload.wikimedia.org/wikipedia/commons/c/c6/Johannesburg_skyline.jpg", "Johannesburg skyline | Khaanya96 | CC BY-SA 4.0 | https://commons.wikimedia.org/wiki/File:Johannesburg_skyline.jpg"),
        new("london-skyline-8556054641.jpg", "London skyline", "https://upload.wikimedia.org/wikipedia/commons/5/58/London_Skyline_-_8556054641.jpg", "London skyline | Donnchadh H. | CC BY 2.0 | https://commons.wikimedia.org/wiki/File:London_Skyline_-_8556054641.jpg"),
        new("tokyo-stock-exchange-entrance-2024.jpg", "Tokyo Stock Exchange entrance", "https://upload.wikimedia.org/wikipedia/commons/c/c8/The_Entrance_of_Tokyo_Stock_Exchange_Main_Building_20240329.jpg", "Tokyo Stock Exchange entrance | Suicasmo | CC0 | https://commons.wikimedia.org/wiki/File:The_Entrance_of_Tokyo_Stock_Exchange_Main_Building_20240329.jpg"),
        new("australian-securities-exchange-entrance.jpg", "Australian Securities Exchange entrance", "https://upload.wikimedia.org/wikipedia/commons/f/ff/Australian_Securities_Exchange_entrance_%28cropped%29.jpg", "Australian Securities Exchange entrance | Jason7825 | Public domain | https://commons.wikimedia.org/wiki/File:Australian_Securities_Exchange_entrance_(cropped).jpg")
    ];

    private static HttpClient CreateDefaultHttpClient()
    {
        HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(45) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DoNotPanicPortfolioVisualizer/6.0");
        return httpClient;
    }

    public ExchangePhotoCacheService()
        : this(CreateDefaultHttpClient)
    {
    }

    public ExchangePhotoCacheService(Func<HttpClient> httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public event Action? BackgroundCacheWarmupCompleted;

    public Task<IReadOnlyList<string>> GetAvailableBackgroundsAsync(
        AppSettings settings,
        HttpClient httpClient,
        bool networkAvailable,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> immediateBackgrounds = GetImmediateBackgrounds(settings);
        if (networkAvailable && !settings.UseCustomBackgroundImageFolder)
            StartDefaultManifestWarmup(settings.BackgroundImageFolder);

        return Task.FromResult(immediateBackgrounds);
    }

    public IReadOnlyList<string> GetImmediateBackgrounds(AppSettings settings)
    {
        if (settings.UseCustomBackgroundImageFolder)
            return string.IsNullOrWhiteSpace(settings.CustomBackgroundImageFolder)
                ? []
                : GetImages(settings.CustomBackgroundImageFolder, settings.BackgroundIncludeSubfolders);

        string cacheFolder = settings.BackgroundImageFolder;
        try
        {
            if (_cacheGate.Wait(0))
            {
                try
                {
                    Directory.CreateDirectory(cacheFolder);
                    DeletePartialDownloads(cacheFolder);
                    CopyBundledStarterImages(cacheFolder);
                    WriteAttributionFile(cacheFolder);

                    IReadOnlyList<string> cached = GetImages(cacheFolder, includeSubfolders: false);
                    if (cached.Count > 0)
                        return cached;
                }
                finally
                {
                    _cacheGate.Release();
                }
            }
            else
            {
                IReadOnlyList<string> cached = GetImages(cacheFolder, includeSubfolders: false);
                if (cached.Count > 0)
                    return cached;
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Background cache preparation failed for {0}: {1}: {2}", cacheFolder, ex.GetType().Name, ex.Message);
        }

        string bundledFolder = GetBundledAssetsFolder();
        return GetImages(bundledFolder, includeSubfolders: false)
            .Where(path => BundledStarterFileNames.Contains(Path.GetFileName(path)))
            .ToList();
    }

    public async Task WarmDefaultManifestCacheAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings.UseCustomBackgroundImageFolder)
            return;

        string cacheFolder = settings.BackgroundImageFolder;
        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PrepareDefaultCacheFolderAsync(cacheFolder, cancellationToken).ConfigureAwait(false);
            bool changed = await DownloadMissingManifestImagesAsync(cacheFolder, cancellationToken).ConfigureAwait(false);
            if (changed)
                BackgroundCacheWarmupCompleted?.Invoke();
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    public string GetManagedCacheFolder(AppSettings settings) => settings.BackgroundImageFolder;
    public IReadOnlyDictionary<string, string> GetAttributionsForBackgrounds(IEnumerable<string> backgroundPaths)
    {
        Dictionary<string, string> attributions = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in backgroundPaths)
        {
            string fileName = Path.GetFileName(path);
            string? attribution = ResolveAttribution(fileName);
            if (!string.IsNullOrWhiteSpace(attribution))
                attributions[path] = attribution;
        }

        return attributions;
    }

    private static string? ResolveAttribution(string fileName)
    {
        if (BundledStarterAttributions.TryGetValue(fileName, out string? starterAttribution))
            return starterAttribution;

        return Catalog.FirstOrDefault(entry => string.Equals(entry.LocalFileName, fileName, StringComparison.OrdinalIgnoreCase))?.AttributionLine;
    }

    private void StartDefaultManifestWarmup(string cacheFolder)
    {
        lock (_downloadLock)
        {
            if (_downloadTask is { IsCompleted: false })
                return;

            _downloadTask = Task.Run(async () =>
            {
                try
                {
                    await _downloadGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        await PrepareDefaultCacheFolderAsync(cacheFolder, CancellationToken.None).ConfigureAwait(false);
                        bool changed = await DownloadMissingManifestImagesAsync(cacheFolder, CancellationToken.None).ConfigureAwait(false);
                        if (changed)
                            BackgroundCacheWarmupCompleted?.Invoke();
                    }
                    finally
                    {
                        _downloadGate.Release();
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Background image warmup failed: {0}: {1}", ex.GetType().Name, ex.Message);
                }
            });
        }
    }

    private async Task PrepareDefaultCacheFolderAsync(string cacheFolder, CancellationToken cancellationToken)
    {
        await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(cacheFolder);
            DeletePartialDownloads(cacheFolder);
            CopyBundledStarterImages(cacheFolder);
            WriteAttributionFile(cacheFolder);
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private async Task<bool> DownloadMissingManifestImagesAsync(string cacheFolder, CancellationToken cancellationToken)
    {
        bool changed = false;
        using HttpClient httpClient = _httpClientFactory();
        foreach (ExchangePhotoCatalogEntry entry in Catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string targetPath = Path.Combine(cacheFolder, entry.LocalFileName);
            if (File.Exists(targetPath))
                continue;

            string tempPath = targetPath + ".TMP";
            try
            {
                await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (File.Exists(targetPath))
                        continue;
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                finally
                {
                    _cacheGate.Release();
                }

                using HttpResponseMessage response = await httpClient.GetAsync(entry.DownloadUrl, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using (Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (FileStream targetStream = File.Create(tempPath))
                {
                    await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
                    await targetStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!IsJpegFile(tempPath))
                    throw new InvalidDataException("Downloaded background is not a JPEG file.");

                await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!File.Exists(targetPath))
                    {
                        File.Move(tempPath, targetPath);
                        changed = true;
                    }
                }
                finally
                {
                    _cacheGate.Release();
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Background image download failed for {0}: {1}: {2}", entry.LocalFileName, ex.GetType().Name, ex.Message);
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        return changed;
    }


    private static bool IsJpegFile(string path)
    {
        Span<byte> header = stackalloc byte[3];
        using FileStream stream = File.OpenRead(path);
        return stream.Read(header) == header.Length &&
               header[0] == 0xFF &&
               header[1] == 0xD8 &&
               header[2] == 0xFF;
    }

    private IReadOnlyList<string> GetImages(string folderPath, bool includeSubfolders)
        => _backgroundImageService.GetImages(folderPath, includeSubfolders)
            .Select(image => image.FilePath)
            .Where(File.Exists)
            .ToList();

    private void CopyBundledStarterImages(string cacheFolder)
    {
        string bundledFolder = GetBundledAssetsFolder();
        if (!Directory.Exists(bundledFolder))
            return;

        foreach (string sourcePath in Directory.EnumerateFiles(bundledFolder))
        {
            string fileName = Path.GetFileName(sourcePath);
            if (!BundledStarterFileNames.Contains(fileName))
                continue;

            string targetPath = Path.Combine(cacheFolder, fileName);
            if (!File.Exists(targetPath))
                File.Copy(sourcePath, targetPath, overwrite: false);
        }
    }

    private static void DeletePartialDownloads(string cacheFolder)
    {
        if (!Directory.Exists(cacheFolder))
            return;

        DateTime staleBeforeUtc = DateTime.UtcNow.AddMinutes(-10);
        foreach (string tempPath in Directory.EnumerateFiles(cacheFolder, "*.TMP", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(tempPath) > staleBeforeUtc)
                    continue;

                File.Delete(tempPath);
            }
            catch
            {
                // A locked partial will be ignored by the selector and retried on a future warm-up.
            }
        }
    }

    private static void WriteAttributionFile(string cacheFolder)
    {
        string targetPath = Path.Combine(cacheFolder, "exchange-photo-attribution.txt");
        StringBuilder builder = new();
        builder.AppendLine("DO NOT PANIC PORTFOLIO VISUALIZER background image manifest");
        builder.AppendLine();
        builder.AppendLine("Bundled starter images and downloadable cache images come from Wikimedia Commons or compatible public-domain sources.");
        builder.AppendLine("If attribution is required on screen, append the relevant line to the bottom-left footer text.");
        builder.AppendLine();
        builder.AppendLine("Bundled starters:");
        builder.AppendLine("- NYC - New York Stock Exchange | Jean-Christophe BENOIST | CC BY 3.0 | https://commons.wikimedia.org/wiki/File:NYC_-_New_York_Stock_Exchange.JPG");
        builder.AppendLine("- London Skyline | Rodrigo.Argenton | CC0 | https://commons.wikimedia.org/wiki/File:London_Skyline_(199638369).jpeg");
        builder.AppendLine("- Shanghai skyline from the Bund | Pxfuel | CC0 | https://commons.wikimedia.org/wiki/File:Shanghai_skyline_from_the_bund.jpg");
        builder.AppendLine();
        builder.AppendLine("Download manifest:");
        foreach (ExchangePhotoCatalogEntry entry in Catalog)
            builder.AppendLine("- " + entry.AttributionLine);

        File.WriteAllText(targetPath, builder.ToString(), Encoding.UTF8);
    }

    private static string GetBundledAssetsFolder()
        => Path.Combine(AppContext.BaseDirectory, "Assets", BundledFolderName);

    private sealed record ExchangePhotoCatalogEntry(
        string LocalFileName,
        string DisplayName,
        string DownloadUrl,
        string AttributionLine);
}
