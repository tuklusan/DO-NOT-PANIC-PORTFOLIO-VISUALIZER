using System.Text.Json;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;

namespace PortfolioSaver.Data.Services;

public sealed class HistoricalCacheService : IHistoricalCacheService
{
    private readonly string _rootFolder;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    public HistoricalCacheService(string? rootFolder = null)
    {
        string configuredRoot = string.IsNullOrWhiteSpace(rootFolder)
            ? Defaults.GetHistoricalCacheFolder()
            : Environment.ExpandEnvironmentVariables(rootFolder);

        _rootFolder = configuredRoot;

        Directory.CreateDirectory(_rootFolder);
    }

    internal Action? PurgeStartedForTesting { get; set; }
    internal Action? PurgeIterationForTesting { get; set; }

    public async Task<TickerHistorySnapshot?> LoadAsync(string symbol, CancellationToken cancellationToken = default)
    {
        string path = GetPath(symbol);
        if (!File.Exists(path))
            return null;

        FileInfo info = new(path);
        if (DateTimeOffset.UtcNow - info.LastWriteTimeUtc > MaxAge)
        {
            TryDelete(path);
            return null;
        }

        if (info.Length == 0)
        {
            TryDelete(path);
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<TickerHistorySnapshot>(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            TryDelete(path);
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task SaveAsync(TickerHistorySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        string path = GetPath(snapshot.Symbol);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: cancellationToken);
    }

    public Task PurgeExpiredAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => PurgeExpired(cancellationToken), cancellationToken);

    private void PurgeExpired(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_rootFolder))
            return;

        PurgeStartedForTesting?.Invoke();
        foreach (string file in Directory.EnumerateFiles(_rootFolder, "*.json", SearchOption.TopDirectoryOnly))
        {
            PurgeIterationForTesting?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo info = new(file);
            if (DateTimeOffset.UtcNow - info.LastWriteTimeUtc > MaxAge)
                TryDelete(file);
        }
    }

    private string GetPath(string symbol)
    {
        string safe = string.Concat(symbol.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        if (string.IsNullOrWhiteSpace(safe))
            safe = "unknown";

        return Path.Combine(_rootFolder, $"{safe.ToUpperInvariant()}.json");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }
}
