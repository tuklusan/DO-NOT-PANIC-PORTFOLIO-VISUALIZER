using PortfolioSaver.Shared.Helpers;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class PathHelperTests
{
    [Fact]
    public void GetAppDataDirectory_DefaultsToLocalAppDataPortfolioSaverRoot()
    {
        string? previousLocalOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", null);

        try
        {
            string expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PortfolioSaver");

            Assert.Equal(expected, PathHelper.GetAppDataDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLocalOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyOverride);
        }
    }

    [Fact]
    public void GetAppDataDirectory_PrefersLocalOverrideOverLegacyOverride()
    {
        string? previousLocalOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string localOverride = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"), "local");
        string legacyOverride = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"), "legacy");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", localOverride);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", legacyOverride);

        try
        {
            Assert.Equal(Path.GetFullPath(localOverride), PathHelper.GetAppDataDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLocalOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyOverride);
        }
    }
}
