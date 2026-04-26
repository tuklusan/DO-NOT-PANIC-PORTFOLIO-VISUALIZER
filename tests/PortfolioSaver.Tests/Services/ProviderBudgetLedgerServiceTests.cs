using System.Reflection;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ProviderBudgetLedgerServiceTests
{
    [Fact]
    public void TryReserve_EnforcesMinimumReuseIntervalPerProvider()
    {
        using LedgerHarness harness = LedgerHarness.Create();
        ProviderBudgetLedgerService service = harness.Service;
        DataSourcePolicySettings policy = CreatePolicy(DataSourceKind.YahooFinance, 100, 1000);
        DateTimeOffset now = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);

        Assert.True(service.TryReserve(policy, 1, TimeSpan.FromSeconds(15), now));
        Assert.False(service.TryReserve(policy, 1, TimeSpan.FromSeconds(15), now.AddSeconds(14)));
        Assert.True(service.TryReserve(policy, 1, TimeSpan.FromSeconds(15), now.AddSeconds(15)));
    }

    [Fact]
    public void TryReserve_EnforcesHourlyAndDailyBudgets()
    {
        using LedgerHarness harness = LedgerHarness.Create();
        ProviderBudgetLedgerService service = harness.Service;
        DateTimeOffset now = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);

        DataSourcePolicySettings hourly = CreatePolicy(DataSourceKind.Finnhub, maxPerHour: 3, maxPerDay: 1000);
        Assert.True(service.TryReserve(hourly, 2, TimeSpan.Zero, now));
        Assert.False(service.TryReserve(hourly, 2, TimeSpan.Zero, now.AddSeconds(1)));

        DataSourcePolicySettings daily = CreatePolicy(DataSourceKind.TwelveData, maxPerHour: 1000, maxPerDay: 2);
        Assert.True(service.TryReserve(daily, 1, TimeSpan.Zero, now));
        Assert.True(service.TryReserve(daily, 1, TimeSpan.Zero, now.AddMinutes(1)));
        Assert.False(service.TryReserve(daily, 1, TimeSpan.Zero, now.AddMinutes(2)));
    }

    [Fact]
    public void NoteRateLimit_BlocksDuringCooldown_ThenAllows()
    {
        using LedgerHarness harness = LedgerHarness.Create();
        ProviderBudgetLedgerService service = harness.Service;
        DataSourcePolicySettings policy = CreatePolicy(DataSourceKind.Tiingo, 100, 1000);
        DateTimeOffset now = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);

        Assert.True(service.TryReserve(policy, 1, TimeSpan.FromSeconds(15), now));
        service.NoteRateLimit(DataSourceKind.Tiingo, TimeSpan.FromMinutes(1), now);

        Assert.False(service.TryReserve(policy, 1, TimeSpan.FromSeconds(15), now.AddSeconds(30)));
        Assert.True(service.TryReserve(policy, 1, TimeSpan.FromSeconds(15), now.AddMinutes(1).AddSeconds(1)));
    }

    [Fact]
    public void TryReserve_EnforcesPerMinuteCreditsWhenProviderDefinesThem()
    {
        using LedgerHarness harness = LedgerHarness.Create();
        ProviderBudgetLedgerService service = harness.Service;
        DataSourcePolicySettings policy = CreatePolicy(DataSourceKind.TwelveData, 1000, 1000);
        DateTimeOffset now = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);

        Assert.True(service.TryReserve(policy, 7, TimeSpan.Zero, now));
        Assert.False(service.TryReserve(policy, 1, TimeSpan.Zero, now.AddSeconds(30)));
        Assert.True(service.TryReserve(policy, 1, TimeSpan.Zero, now.AddMinutes(1).AddSeconds(1)));
    }

    private static DataSourcePolicySettings CreatePolicy(DataSourceKind kind, int maxPerHour, int maxPerDay)
        => new()
        {
            Kind = kind,
            MaxQueriesPerHour = maxPerHour,
            MaxQueriesPerDay = maxPerDay,
            EnableSingleTickerQueries = true,
            EnableBatchTickerQueries = true
        };

    private sealed class LedgerHarness : IDisposable
    {
        private readonly string _tempDirectory;
        public ProviderBudgetLedgerService Service { get; }

        private LedgerHarness(string tempDirectory, ProviderBudgetLedgerService service)
        {
            _tempDirectory = tempDirectory;
            Service = service;
        }

        public static LedgerHarness Create()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
            string ledgerPath = Path.Combine(tempDirectory, "provider-query-usage.json");

            ProviderBudgetLedgerService service = new();
            Type serviceType = typeof(ProviderBudgetLedgerService);

            FieldInfo? pathField = serviceType.GetField("_ledgerPath", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo? ledgerField = serviceType.GetField("_ledger", BindingFlags.Instance | BindingFlags.NonPublic);
            Type? ledgerType = serviceType.GetNestedType("ProviderBudgetLedger", BindingFlags.NonPublic);

            Assert.NotNull(pathField);
            Assert.NotNull(ledgerField);
            Assert.NotNull(ledgerType);

            pathField!.SetValue(service, ledgerPath);
            ledgerField!.SetValue(service, Activator.CreateInstance(ledgerType!));

            return new LedgerHarness(tempDirectory, service);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDirectory))
                    Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }
}
