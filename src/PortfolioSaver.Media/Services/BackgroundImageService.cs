using System.IO;
using PortfolioSaver.Media.Helpers;
using PortfolioSaver.Media.Models;

namespace PortfolioSaver.Media.Services;

public sealed class BackgroundImageService
{
    public IReadOnlyList<BackgroundImageInfo> GetImages(string folderPath, bool includeSubfolders = false)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return [];

        SearchOption searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(folderPath, "*.*", searchOption)
            .Where(ImageFileHelper.IsSupported)
            .Select(path => new BackgroundImageInfo
            {
                FilePath = path,
                DisplayName = Path.GetFileNameWithoutExtension(path)
            })
            .ToList();
    }
}
