using System.Net.Http;

namespace PortfolioSaver.Shared.Licensing;

public static class MitLicenseService
{
    private static readonly Uri[] MitLicenseSources =
    [
        new("https://raw.githubusercontent.com/spdx/license-list-data/main/text/MIT.txt"),
        new("https://raw.githubusercontent.com/github/choosealicense.com/gh-pages/_licenses/mit.txt")
    ];

    public static string GetFallbackMitText()
        => NormalizeLineEndings(FallbackMitText);

    public static async Task<string> GetMitTextAsync(CancellationToken cancellationToken = default)
    {
        foreach (Uri source in MitLicenseSources)
        {
            string? fetched = await TryGetTextAsync(source, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(fetched))
                continue;

            string normalized = NormalizeFetchedLicenseText(fetched);
            if (ContainsCoreMitClauses(normalized))
                return normalized;
        }

        return GetFallbackMitText();
    }

    private static async Task<string?> TryGetTextAsync(Uri source, CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = new()
            {
                Timeout = TimeSpan.FromSeconds(6)
            };

            using HttpRequestMessage request = new(HttpMethod.Get, source);
            request.Headers.UserAgent.ParseAdd("PortfolioVisualizer-LicenseFetcher/1.0");

            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeFetchedLicenseText(string raw)
    {
        if (raw.StartsWith("---", StringComparison.Ordinal))
        {
            int secondFence = raw.IndexOf("\n---", StringComparison.Ordinal);
            if (secondFence >= 0)
            {
                raw = raw[(secondFence + 4)..];
            }
        }

        return NormalizeLineEndings(raw).Trim();
    }

    private static bool ContainsCoreMitClauses(string text)
    {
        return text.Contains("Permission is hereby granted, free of charge", StringComparison.OrdinalIgnoreCase) &&
               text.Contains("THE SOFTWARE IS PROVIDED \"AS IS\"", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLineEndings(string value)
        => value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private const string FallbackMitText =
        "MIT License\n\n" +
        "Copyright (c) 2026 " + AppIdentity.AuthorName + "\n\n" +
        "Permission is hereby granted, free of charge, to any person obtaining a copy " +
        "of this software and associated documentation files (the \"Software\"), to deal " +
        "in the Software without restriction, including without limitation the rights " +
        "to use, copy, modify, merge, publish, distribute, sublicense, and/or sell " +
        "copies of the Software, and to permit persons to whom the Software is " +
        "furnished to do so, subject to the following conditions:\n\n" +
        "The above copyright notice and this permission notice shall be included in all " +
        "copies or substantial portions of the Software.\n\n" +
        "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR " +
        "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, " +
        "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE " +
        "AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER " +
        "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, " +
        "OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE " +
        "SOFTWARE.";
}
