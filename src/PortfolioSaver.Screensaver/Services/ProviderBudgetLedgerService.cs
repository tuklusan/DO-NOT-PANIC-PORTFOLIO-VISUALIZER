using System.IO;
using System.Text.Json;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Screensaver.Services;

public sealed class ProviderBudgetLedgerService
{
    private const int TwelveDataMinuteSafetyReserve = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _ledgerPath;
    private readonly object _sync = new();
    private ProviderBudgetLedger _ledger;

    public ProviderBudgetLedgerService(string? ledgerPath = null)
    {
        _ledgerPath = string.IsNullOrWhiteSpace(ledgerPath)
            ? Path.Combine(PathHelper.GetLocalDataDirectory(), "provider-query-usage.json")
            : ledgerPath;
        _ledger = LoadLedger();
    }

    public bool TryReserve(DataSourcePolicySettings policy, int queryCost, TimeSpan minimumReuseInterval, DateTimeOffset nowUtc)
    {
        if (queryCost <= 0)
            return false;

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
            SaveLedger();
            return true;
        }
    }

    public void NoteRateLimit(DataSourceKind kind, TimeSpan cooldown, DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            ProviderBudgetEntry entry = GetEntry(kind);
            entry.CooldownUntilUtc = nowUtc.Add(cooldown);
            SaveLedger();
        }
    }

    private ProviderBudgetEntry GetEntry(DataSourceKind kind)
    {
        if (!_ledger.Entries.TryGetValue(kind, out ProviderBudgetEntry? entry))
        {
            entry = new ProviderBudgetEntry();
            _ledger.Entries[kind] = entry;
        }

        return entry;
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

        if (kind != DataSourceKind.TwelveData)
            return hardMaxQueriesPerMinute;

        return Math.Max(1, hardMaxQueriesPerMinute - TwelveDataMinuteSafetyReserve);
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

    private void SaveLedger()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_ledgerPath)!);
        File.WriteAllText(_ledgerPath, JsonSerializer.Serialize(_ledger, JsonOptions));
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
