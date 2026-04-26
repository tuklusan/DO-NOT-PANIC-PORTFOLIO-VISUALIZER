using System.IO;
using System.Windows.Media.Imaging;

namespace PortfolioSaver.Media.Services;

public sealed class BackgroundPreloadService
{
    public BitmapImage? Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(filePath, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
