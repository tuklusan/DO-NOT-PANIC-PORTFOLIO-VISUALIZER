using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Threading;
using PortfolioSaver.Config.Services;
using PortfolioSaver.Config.ViewModels;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class MainWindowViewModelValidationTests
{
    [Fact]
    public void DisableInvalidSymbols_AutoDisablesMatchingTickers()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();

        TickerItemEditorViewModel badTicker = new(new TickerItem { Symbol = "BAD1", Enabled = true });
        TickerItemEditorViewModel goodTicker = new(new TickerItem { Symbol = "GOOD1", Enabled = true });
        TickerGroupEditorViewModel group = new();
        group.Tickers.Clear();
        group.Tickers.Add(badTicker);
        group.Tickers.Add(goodTicker);
        vm.Groups.Clear();
        vm.Groups.Add(group);

        int disabled = InvokePrivate<int>(vm, "DisableInvalidSymbols", [new[] { "bad1" }]);

        Assert.Equal(1, disabled);
        Assert.False(badTicker.Enabled);
        Assert.Equal(SymbolValidationState.Invalid, badTicker.ValidationState);
        Assert.True(goodTicker.Enabled);
    }

    [Fact]
    public void OnEditorChanged_WithDataEdit_InvalidatesValidatedState()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        InvokePrivate<object?>(vm, "OnEditorChanged", [null, new PropertyChangedEventArgs(nameof(TickerItemEditorViewModel.Symbol))]);

        Assert.False(vm.IsValidated);
        Assert.Equal("Validate", vm.PrimaryButtonText);
        Assert.Equal("Configuration changed. Click Validate.", vm.StatusMessage);
        Assert.Equal(string.Empty, GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.False(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void OnEditorChanged_WithValidationStateUpdate_DoesNotInvalidate()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        InvokePrivate<object?>(vm, "OnEditorChanged", [null, new PropertyChangedEventArgs(nameof(TickerItemEditorViewModel.ValidationState))]);

        Assert.True(vm.IsValidated);
        Assert.Equal("Validate", vm.PrimaryButtonText);
        Assert.Equal("fingerprint", GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.True(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public async Task OnStateTimerTickAsync_DoesNotTriggerBackgroundSymbolValidation()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();

        TickerItemEditorViewModel ticker = new(new TickerItem { Symbol = "AAPL", Enabled = true });
        ticker.ValidationState = SymbolValidationState.Unknown;
        ticker.ValidationMessage = "Pending validation";
        TickerGroupEditorViewModel group = new();
        group.Tickers.Clear();
        group.Tickers.Add(ticker);
        vm.Groups.Clear();
        vm.Groups.Add(group);

        Task task = InvokePrivate<Task>(vm, "OnStateTimerTickAsync", []);
        await task;

        Assert.Equal(SymbolValidationState.Unknown, ticker.ValidationState);
        Assert.Equal("Pending validation", ticker.ValidationMessage);
    }

    [Fact]
    public void UpdateConnectivityState_WhenConnectivityRecovers_ClearsInternetRequiredStatus()
    {
        FakeConnectivityService connectivity = new(initiallyAvailable: true);
        MainWindowViewModel vm = CreateIsolatedViewModel(connectivity);
        vm.StatusMessage = "Internet connection is required for ticker validation.";
        SetPrivateField(vm, "_isNetworkAvailable", false);

        InvokePrivate<object?>(vm, "UpdateConnectivityState", []);

        Assert.True(vm.IsNetworkAvailable);
        Assert.Equal("Internet connection detected. Continue with Validate.", vm.StatusMessage);
    }

    [Fact]
    public void EnsureValidationConnectivity_ForcesFreshProbeBeforeBlockingValidation()
    {
        FakeConnectivityService connectivity = new(initiallyAvailable: false);
        MainWindowViewModel vm = CreateIsolatedViewModel(connectivity);
        connectivity.SetAvailable(true);

        bool available = InvokePrivate<bool>(vm, "EnsureValidationConnectivity", []);

        Assert.True(available);
        Assert.True(vm.IsNetworkAvailable);
        Assert.Equal(1, connectivity.ForceProbeCalls);
    }

    [Fact]
    public void SetApplying_UpdatesPrimaryActionState_AndValidationLog()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel(new FakeConnectivityService(initiallyAvailable: true));

        InvokePrivate<object?>(vm, "BeginValidationRun", []);

        Assert.True(vm.IsApplying);
        Assert.False(vm.IsValidationActionEnabled);
        Assert.Equal("Validating...", vm.PrimaryButtonText);

        InvokePrivate<object?>(vm, "AppendValidationLog", ["AAPL -> APPLE INC."]);
        Assert.Contains("AAPL -> APPLE INC.", vm.ValidationLogText, StringComparison.Ordinal);

        InvokePrivate<object?>(vm, "EndValidationRun", []);

        Assert.False(vm.IsApplying);
        Assert.True(vm.IsValidationActionEnabled);
        Assert.Equal("Validate", vm.PrimaryButtonText);
    }

    [Fact]
    public async Task ValidateSymbolsAgainstSourcesAsync_TrustsRecentCachedSymbolProfile()
    {
        string localDataRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(localDataRoot);
        string? originalLocalDataRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", localDataRoot);

        try
        {
            SymbolProfileStore store = new(Path.Combine(localDataRoot, "symbol-profiles.json"));
            store.Save(
            [
                new SymbolProfile
                {
                    Symbol = "AAPL",
                    CanonicalSymbol = "AAPL",
                    DisplayName = "Apple Inc.",
                    LastValidatedUtc = DateTimeOffset.UtcNow.AddMinutes(-6),
                    SupportedQuoteSources = [PortfolioSaver.Core.Enums.DataSourceKind.YahooFinance]
                }
            ]);

            MainWindowViewModel vm = CreateIsolatedViewModel(new FakeConnectivityService(initiallyAvailable: true));
            TickerGroupEditorViewModel group = new();
            group.Tickers.Clear();
            group.Tickers.Add(new TickerItemEditorViewModel(new TickerItem
            {
                Symbol = "AAPL",
                DisplayName = "Apple Inc.",
                Enabled = true
            }));
            vm.Groups.Clear();
            vm.Groups.Add(group);

            YahooSymbolValidationResult result = await InvokePrivate<Task<YahooSymbolValidationResult>>(
                vm,
                "ValidateSymbolsAgainstSourcesAsync",
                [vm.Settings, (IReadOnlyList<string>)["AAPL"]]);

            YahooSymbolValidationEntry entry = Assert.Single(result.Entries.Values);
            Assert.True(entry.IsValid);
            Assert.Empty(result.InvalidSymbols);
            Assert.Empty(result.DeferredSymbols);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", originalLocalDataRoot);
            try
            {
                if (Directory.Exists(localDataRoot))
                    Directory.Delete(localDataRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static MainWindowViewModel CreateIsolatedViewModel(IConnectivityService? connectivity = null)
    {
        MainWindowViewModel vm = new(connectivity);
        DispatcherTimer timer = GetPrivateField<DispatcherTimer>(vm, "_stateTimer");
        timer.Stop();
        return vm;
    }

    private static T InvokePrivate<T>(object instance, string methodName, object?[] args)
    {
        MethodInfo? method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        object? value = method!.Invoke(instance, args);
        return value is null ? default! : (T)value;
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        object? value = field!.GetValue(instance);
        return Assert.IsType<T>(value);
    }

    private sealed class FakeConnectivityService(bool initiallyAvailable) : IConnectivityService
    {
        private bool _available = initiallyAvailable;

        public int ForceProbeCalls { get; private set; }

        public bool IsInternetAvailable() => _available;

        public void ForceProbe()
        {
            ForceProbeCalls++;
        }

        public void SetAvailable(bool available) => _available = available;
    }
}
