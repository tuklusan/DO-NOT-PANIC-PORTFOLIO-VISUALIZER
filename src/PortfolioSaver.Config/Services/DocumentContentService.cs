using System.IO;
using PortfolioSaver.Shared;
using PortfolioSaver.Shared.Licensing;

namespace PortfolioSaver.Config.Services;

public sealed class DocumentContentService
{
    public string GetHelpText()
        => LoadOrFallback("help.txt", DefaultHelpText);

    public string GetAboutText()
        => LoadOrFallback("about.txt", DefaultAboutText);

    public string GetLicenseText()
    {
        try
        {
            string latest = MitLicenseService.GetMitTextAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(latest))
                return latest;
        }
        catch
        {
        }

        string local = LoadOrFallback("license-mit.txt", string.Empty);
        if (!string.IsNullOrWhiteSpace(local))
            return local;

        return MitLicenseService.GetFallbackMitText();
    }

    private static string LoadOrFallback(string fileName, string fallback)
    {
        string candidate = Path.Combine(AppContext.BaseDirectory, "Content", fileName);
        if (File.Exists(candidate))
        {
            string text = File.ReadAllText(candidate).Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return fallback;
    }

    private static readonly string DefaultHelpText = $"{AppIdentity.ApplicationName} Help\r\n\r\n" +
                                           "- Add up to 4 ticker tapes.\r\n" +
                                           "- Each tape can contain up to 8 tickers.\r\n" +
                                           "- Leave a ticker name blank to let Apply auto-fill it when validation succeeds.\r\n" +
                                           "- Apply validates ticker symbols when a network connection is available.\r\n" +
                                           "- Invalid symbols are automatically unchecked so they do not break the screensaver.\r\n" +
                                           "- RSS feeds are validated during Apply. Invalid feeds fall back to the default Yahoo Finance feed.\r\n" +
                                           "- Advanced settings control per-provider hourly and daily request budgets.";

    private static readonly string DefaultAboutText = $"{AppIdentity.ApplicationName}\r\n\r\n" +
                                            $"{PortfolioVersion.BaselineLabel} baseline.\r\n\r\n" +
                                            "This build shows floating ticker tapes, finance headlines, background exchange imagery, and market-aware overlays.\r\n" +
                                            "The configuration app lets you tune tapes, data sources, image behavior, and refresh settings.\r\n\r\n" +
                                            $"Published by {AppIdentity.PublisherName}.\r\n" +
                                            $"Author: {AppIdentity.AuthorName}.\r\n" +
                                            $"Licensed under the {AppIdentity.LicenseName}.";
}
