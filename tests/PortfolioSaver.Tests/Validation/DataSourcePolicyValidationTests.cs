using PortfolioSaver.Config.ViewModels;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Validation;
using Xunit;

namespace PortfolioSaver.Tests.Validation;

public sealed class DataSourcePolicyValidationTests
{
    [Fact]
    public void DataSourcePolicyEditor_ClampsBudgets_AndDisablesUnsupportedModes()
    {
        DataSourcePolicyEditorViewModel vm = new(new DataSourcePolicySettings
        {
            Kind = DataSourceKind.Finnhub,
            MaxQueriesPerHour = 999_999,
            MaxQueriesPerDay = 999_999,
            EnableSingleTickerQueries = true,
            EnableBatchTickerQueries = true
        });

        Assert.Equal(DataSourceCatalog.GetCapabilities(DataSourceKind.Finnhub).HardMaxQueriesPerHour, vm.MaxQueriesPerHour);
        Assert.Equal(DataSourceCatalog.GetCapabilities(DataSourceKind.Finnhub).HardMaxQueriesPerDay, vm.MaxQueriesPerDay);
        Assert.True(vm.EnableSingleTickerQueries);
        Assert.False(vm.EnableBatchTickerQueries);

        vm.MaxQueriesPerHour = 0;
        vm.MaxQueriesPerDay = 0;
        vm.EnableBatchTickerQueries = true;

        Assert.Equal(1, vm.MaxQueriesPerHour);
        Assert.Equal(1, vm.MaxQueriesPerDay);
        Assert.False(vm.EnableBatchTickerQueries);
    }

    [Fact]
    public void SettingsValidator_RejectsOutOfBoundsBudgets_AndUnsupportedBatchMode()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.DataSources =
        [
            new DataSourcePolicySettings
            {
                Kind = DataSourceKind.Finnhub,
                MaxQueriesPerHour = 999_999,
                MaxQueriesPerDay = 999_999,
                EnableSingleTickerQueries = true,
                EnableBatchTickerQueries = true
            }
        ];

        SettingsValidator validator = new();
        IReadOnlyList<string> errors = validator.Validate(settings);

        Assert.Contains(errors, error => error.Contains("hourly budget", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("daily budget", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("does not support multi-ticker queries", StringComparison.OrdinalIgnoreCase));
    }
}
