using System.IO;
using System.Text.Json;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Screensaver.Services;

public sealed class ProviderBudgetLedgerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _ledgerPath;
    private readonly object _sync = new();
    private readonly object _saveSync = new();
    private ProviderBudgetLedger? _ledger;
    private long _ledgerVersion;
    private long _lastPersistedLedgerVersion;

    public ProviderBudgetLedgerService(string? ledgerPath = null)
    {
        _ledgerPath = string.IsNullOrWhiteSpace(ledgerPath)
            ? Path.Combine(PathHelper.GetLocalDataDirectory(), "provider-query-usage.json")
            : ledgerPath;
    }

    public bool TryReserve(DataSourcePolicySettings policy, int queryCost, TimeSpan minimumReuseInterval, DateTimeOffset nowUtc)
    {
        if (queryCost <= 0)
            return false;

        ProviderBudgetLedger? snapshot = null;
        long snapshotVersion = 0;
        lock (_sync)
        {
            ProviderBudgetEntry entry = GetEntry(policy.Kind);
            var capabilities = DataSourceCatalog.GetCapabilities(policy.Kind);
            Prune(entry, nowUtc);

            if (entry.CooldownUntilUtc is DateTimeOffset cooldownUntilUtc && cooldownUntilUtc > nowUtc)
                return false;

            if (entry.LastQueryUtc is DateTimeOffset lastQueryUtc &&
                nowUtc - lastQueryUtc < minimumReuseInterval)
            {
                return false;
            }

            int effectiveMinuteBudget = GetEffectiveMinuteBudget(policy.Kind, capabilities.HardMaxQueriesPerMinute);
            if (effectiveMinuteBudget > 0)
            {
                int minuteCount = entry.QueryTimestampsUtc.Count(timestamp => timestamp > nowUtc.AddMinutes(-1));
                if (minuteCount + queryCost > effectiveMinuteBudget)
                    return false;
            }

            int hourlyCount = entry.QueryTimestampsUtc.Count(timestamp => timestamp > nowUtc.AddHours(-1));
            if (hourlyCount + queryCost > policy.MaxQueriesPerHour)
                return false;

            if (entry.QueryTimestampsUtc.Count + queryCost > policy.MaxQueriesPerDay)
                return false;

            for (int i = 0; i < queryCost; i++)
                entry.QueryTimestampsUtc.Add(nowUtc);

            entry.LastQueryUtc = nowUtc;
            snapshot = CloneLedger(_ledger ?? throw new InvalidOperationException("Provider budget ledger has not been loaded."));
            snapshotVersion = ++_ledgerVersion;
        }

        SaveLedger(snapshot, snapshotVersion);
        return true;
    }

    public void NoteRateLimit(DataSourceKind kind, TimeSpan cooldown, DateTimeOffset nowUtc)
    {
        ProviderBudgetLedger snapshot;
        long snapshotVersion;
        lock (_sync)
        {
            ProviderBudgetEntry entry = GetEntry(kind);
            entry.CooldownUntilUtc = nowUtc.Add(cooldown);
            snapshot = CloneLedger(_ledger ?? throw new InvalidOperationException("Provider budget ledger has not been loaded."));
            snapshotVersion = ++_ledgerVersion;
        }

        SaveLedger(snapshot, snapshotVersion);
    }

    private ProviderBudgetEntry GetEntry(DataSourceKind kind)
    {
        EnsureLedgerLoadedLocked();
        ProviderBudgetLedger ledger = _ledger ?? throw new InvalidOperationException("Provider budget ledger has not been loaded.");
        if (!ledger.Entries.TryGetValue(kind, out ProviderBudgetEntry? entry))
        {
            entry = new ProviderBudgetEntry();
            ledger.Entries[kind] = entry;
        }

        return entry;
    }

    private void EnsureLedgerLoadedLocked()
    {
        _ledger ??= LoadLedger();
    }

    private static void Prune(ProviderBudgetEntry entry, DateTimeOffset nowUtc)
    {
        DateTimeOffset dayCutoff = nowUtc.AddDays(-1);
        entry.QueryTimestampsUtc.RemoveAll(timestamp => timestamp <= dayCutoff);

        if (entry.CooldownUntilUtc is DateTimeOffset cooldownUntilUtc && cooldownUntilUtc <= nowUtc)
            entry.CooldownUntilUtc = null;
    }

    private static int GetEffectiveMinuteBudget(DataSourceKind kind, int hardMaxQueriesPerMinute)
    {
        if (hardMaxQueriesPerMinute <= 0)
            return 0;

        return hardMaxQueriesPerMinute;
    }

    private ProviderBudgetLedger LoadLedger()
    {
        try
        {
            if (!File.Exists(_ledgerPath))
                return new ProviderBudgetLedger();

            string json = File.ReadAllText(_ledgerPath);
            return JsonSerializer.Deserialize<ProviderBudgetLedger>(json, JsonOptions) ?? new ProviderBudgetLedger();
        }
        catch
        {
            return new ProviderBudgetLedger();
        }
    }

    private void SaveLedger(ProviderBudgetLedger ledger, long ledgerVersion)
    {
        lock (_saveSync)
        {
            if (ledgerVersion <= _lastPersistedLedgerVersion)
                return;

            string targetPath = Path.GetFullPath(_ledgerPath);
            string directory = Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory;
            Directory.CreateDirectory(directory);

            string tempPath = Path.Combine(directory, Path.GetFileName(targetPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            string json = JsonSerializer.Serialize(ledger, JsonOptions);
            try
            {
                using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new(stream))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                File.Move(tempPath, targetPath, overwrite: true);
                _lastPersistedLedgerVersion = ledgerVersion;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }

    private static ProviderBudgetLedger CloneLedger(ProviderBudgetLedger ledger)
    {
        // The ledger is intentionally tiny; cloning lets disk I/O happen without holding the state lock.
        ProviderBudgetLedger clone = new();
        foreach ((DataSourceKind kind, ProviderBudgetEntry entry) in ledger.Entries)
        {
            clone.Entries[kind] = new ProviderBudgetEntry
            {
                QueryTimestampsUtc = [.. entry.QueryTimestampsUtc],
                LastQueryUtc = entry.LastQueryUtc,
                CooldownUntilUtc = entry.CooldownUntilUtc
            };
        }

        return clone;
    }

    private sealed class ProviderBudgetLedger
    {
        public Dictionary<DataSourceKind, ProviderBudgetEntry> Entries { get; set; } = [];
    }

    private sealed class ProviderBudgetEntry
    {
        public List<DateTimeOffset> QueryTimestampsUtc { get; set; } = [];
        public DateTimeOffset? LastQueryUtc { get; set; }
        public DateTimeOffset? CooldownUntilUtc { get; set; }
    }
}
