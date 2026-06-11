using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using PortfolioSaver.Config.Services;
using PortfolioSaver.Config.ViewModels;
using PortfolioSaver.Core.Constants;
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
        Assert.False(vm.ShowValidateButton);
        Assert.True(vm.ShowValidatedActionButtons);
        Assert.Equal("fingerprint", GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.True(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void OnEditorChanged_WhileApplying_DoesNotInvalidateValidatedState()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", false);
        SetPrivateField(vm, "_isApplying", true);

        InvokePrivate<object?>(vm, "OnEditorChanged", [null, new PropertyChangedEventArgs(nameof(TickerItemEditorViewModel.DisplayName))]);

        Assert.True(vm.IsValidated);
        Assert.Equal("Validating...", vm.PrimaryButtonText);
        Assert.Equal("fingerprint", GetPrivateField<string>(vm, "_validatedFingerprint"));
    }

    [Fact]
    public void OnEditorChanged_WithNonPersistedTickerProperty_DoesNotInvalidate()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        TickerItemEditorViewModel ticker = new(new TickerItem { Symbol = "AAPL", Enabled = true });
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        InvokePrivate<object?>(vm, "OnEditorChanged", [ticker, new PropertyChangedEventArgs(nameof(TickerItemEditorViewModel.ValidationBadgeText))]);

        Assert.True(vm.IsValidated);
        Assert.Equal("fingerprint", GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.True(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void OnEditorChanged_WithUnknownTickerProperty_DoesNotInvalidate()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        TickerItemEditorViewModel ticker = new(new TickerItem { Symbol = "AAPL", Enabled = true });
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        InvokePrivate<object?>(vm, "OnEditorChanged", [ticker, new PropertyChangedEventArgs("FutureUiOnlyProperty")]);

        Assert.True(vm.IsValidated);
        Assert.Equal("fingerprint", GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.True(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void OnEditorChanged_WithNormalizedTickerMatch_DoesNotInvalidate()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        TickerItemEditorViewModel ticker = new(new TickerItem { Symbol = " AAPL ", Enabled = true });
        TickerGroupEditorViewModel group = new();
        group.Tickers.Clear();
        group.Tickers.Add(ticker);
        vm.Groups.Clear();
        vm.Groups.Add(group);
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);
        AppSettings validated = Defaults.CreateSettings();
        validated.Groups =
        [
            new TickerGroup
            {
                Name = group.Name,
                Enabled = group.Enabled,
                Speed = group.SpeedValue,
                RenderMode = group.RenderMode,
                Direction = group.Direction,
                RowHeight = group.RowHeight,
                Tickers = [new TickerItem { Symbol = "AAPL", DisplayName = ticker.DisplayName, Enabled = true }]
            }
        ];
        SetPrivateField(vm, "_validatedCandidateSettings", validated);
        InvokePrivate<object?>(vm, "CaptureValidatedEditorSnapshots", [validated]);

        InvokePrivate<object?>(vm, "OnEditorChanged", [ticker, new PropertyChangedEventArgs(nameof(TickerItemEditorViewModel.Symbol))]);

        Assert.True(vm.IsValidated);
        Assert.Equal("fingerprint", GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.True(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void OnEditorChanged_WithNormalizedGroupNameMatch_DoesNotInvalidate()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        TickerGroupEditorViewModel group = new(new TickerGroup { Name = " Core " });
        vm.Groups.Clear();
        vm.Groups.Add(group);
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);
        AppSettings validated = Defaults.CreateSettings();
        validated.Groups =
        [
            new TickerGroup
            {
                Name = "Core",
                Enabled = group.Enabled,
                Speed = group.SpeedValue,
                RenderMode = group.RenderMode,
                Direction = group.Direction,
                RowHeight = group.RowHeight
            }
        ];
        SetPrivateField(vm, "_validatedCandidateSettings", validated);
        InvokePrivate<object?>(vm, "CaptureValidatedEditorSnapshots", [validated]);

        InvokePrivate<object?>(vm, "OnEditorChanged", [group, new PropertyChangedEventArgs(nameof(TickerGroupEditorViewModel.Name))]);

        Assert.True(vm.IsValidated);
        Assert.Equal("fingerprint", GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.True(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void GroupsMove_InvalidatesValidatedState()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        vm.Groups.Clear();
        vm.Groups.Add(new TickerGroupEditorViewModel(new TickerGroup { Name = "A" }));
        vm.Groups.Add(new TickerGroupEditorViewModel(new TickerGroup { Name = "B" }));
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        vm.Groups.Move(0, 1);

        Assert.False(vm.IsValidated);
        Assert.Equal(string.Empty, GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.False(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void TickerReplacementAtSameIndex_InvalidatesValidatedState()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        TickerGroupEditorViewModel group = new();
        group.Tickers.Clear();
        group.Tickers.Add(new TickerItemEditorViewModel(new TickerItem { Symbol = "AAPL" }));
        vm.Groups.Clear();
        vm.Groups.Add(group);
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        group.Tickers[0] = new TickerItemEditorViewModel(new TickerItem { Symbol = "AAPL" });

        Assert.False(vm.IsValidated);
        Assert.Equal(string.Empty, GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.False(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void TickerAdd_InvalidatesValidatedState()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        TickerGroupEditorViewModel group = new();
        group.Tickers.Clear();
        vm.Groups.Clear();
        vm.Groups.Add(group);
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        group.Tickers.Add(new TickerItemEditorViewModel(new TickerItem { Symbol = "AAPL" }));

        Assert.False(vm.IsValidated);
        Assert.Equal(string.Empty, GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.False(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void TickerRemove_InvalidatesValidatedState()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        TickerGroupEditorViewModel group = new();
        TickerItemEditorViewModel ticker = new(new TickerItem { Symbol = "AAPL" });
        group.Tickers.Clear();
        group.Tickers.Add(ticker);
        vm.Groups.Clear();
        vm.Groups.Add(group);
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        group.Tickers.Remove(ticker);

        Assert.False(vm.IsValidated);
        Assert.Equal(string.Empty, GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.False(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void TickerClear_UnhooksOldTickersAndInvalidatesValidatedState()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        TickerGroupEditorViewModel group = new();
        TickerItemEditorViewModel oldTicker = new(new TickerItem { Symbol = "AAPL" });
        group.Tickers.Clear();
        group.Tickers.Add(oldTicker);
        vm.Groups.Clear();
        vm.Groups.Add(group);
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        group.Tickers.Clear();

        Assert.False(vm.IsValidated);
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);
        oldTicker.Symbol = "MSFT";

        Assert.True(vm.IsValidated);
        Assert.Equal("fingerprint", GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.True(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void TickerMove_InvalidatesValidatedState()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        TickerGroupEditorViewModel group = new();
        group.Tickers.Clear();
        group.Tickers.Add(new TickerItemEditorViewModel(new TickerItem { Symbol = "AAPL" }));
        group.Tickers.Add(new TickerItemEditorViewModel(new TickerItem { Symbol = "MSFT" }));
        vm.Groups.Clear();
        vm.Groups.Add(group);
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        group.Tickers.Move(0, 1);

        Assert.False(vm.IsValidated);
        Assert.Equal(string.Empty, GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.False(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void GroupsReset_UnhooksOldGroupsAndHooksNewGroups()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        TickerGroupEditorViewModel oldGroup = new(new TickerGroup { Name = "Old" });
        vm.Groups.Clear();
        vm.Groups.Add(oldGroup);

        vm.Groups.Clear();
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);
        oldGroup.Name = "Old changed";

        Assert.True(vm.IsValidated);
        Assert.Equal("fingerprint", GetPrivateField<string>(vm, "_validatedFingerprint"));

        TickerGroupEditorViewModel newGroup = new(new TickerGroup { Name = "New" });
        vm.Groups.Add(newGroup);
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        newGroup.Name = "New changed";

        Assert.False(vm.IsValidated);
        Assert.Equal(string.Empty, GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.False(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void PersistedEditorPropertyContract_CoversPersistedModels()
    {
        // If persisted model properties change, update this mapping and IsPersistedEditorProperty together.
        Dictionary<string, string> tickerMapping = new()
        {
            [nameof(TickerItem.Symbol)] = nameof(TickerItemEditorViewModel.Symbol),
            [nameof(TickerItem.DisplayName)] = nameof(TickerItemEditorViewModel.DisplayName),
            [nameof(TickerItem.Quantity)] = nameof(TickerItemEditorViewModel.Quantity),
            [nameof(TickerItem.CostBasis)] = nameof(TickerItemEditorViewModel.CostBasis),
            [nameof(TickerItem.Currency)] = nameof(TickerItemEditorViewModel.Currency),
            [nameof(TickerItem.Enabled)] = nameof(TickerItemEditorViewModel.Enabled)
        };
        Dictionary<string, string> groupMapping = new()
        {
            [nameof(TickerGroup.Name)] = nameof(TickerGroupEditorViewModel.Name),
            [nameof(TickerGroup.Enabled)] = nameof(TickerGroupEditorViewModel.Enabled),
            [nameof(TickerGroup.Speed)] = nameof(TickerGroupEditorViewModel.SpeedValue),
            [nameof(TickerGroup.RenderMode)] = nameof(TickerGroupEditorViewModel.RenderMode),
            [nameof(TickerGroup.Direction)] = nameof(TickerGroupEditorViewModel.Direction),
            [nameof(TickerGroup.RowHeight)] = nameof(TickerGroupEditorViewModel.RowHeight)
        };

        string[] tickerModelProperties = typeof(TickerItem).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Order()
            .ToArray();
        string[] groupModelProperties = typeof(TickerGroup).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Except([nameof(TickerGroup.Id), nameof(TickerGroup.Tickers)])
            .Order()
            .ToArray();

        Assert.Equal(tickerModelProperties, tickerMapping.Keys.Order().ToArray());
        Assert.Equal(groupModelProperties, groupMapping.Keys.Order().ToArray());
        MainWindowViewModel vm = CreateIsolatedViewModel();
        foreach (string propertyName in tickerMapping.Values)
        {
            Assert.True(InvokePrivate<bool>(
                vm,
                "IsPersistedEditorProperty",
                [new TickerItemEditorViewModel(), propertyName]));
        }

        foreach (string propertyName in groupMapping.Values)
        {
            Assert.True(InvokePrivate<bool>(
                vm,
                "IsPersistedEditorProperty",
                [new TickerGroupEditorViewModel(), propertyName]));
        }
    }

    [Theory]
    [InlineData(nameof(TickerGroupEditorViewModel.Name))]
    [InlineData(nameof(TickerGroupEditorViewModel.Enabled))]
    [InlineData(nameof(TickerGroupEditorViewModel.SpeedValue))]
    [InlineData(nameof(TickerGroupEditorViewModel.RenderMode))]
    [InlineData(nameof(TickerGroupEditorViewModel.Direction))]
    [InlineData(nameof(TickerGroupEditorViewModel.RowHeight))]
    public void OnEditorChanged_WithPersistedGroupProperty_InvalidatesValidatedState(string propertyName)
    {
        MainWindowViewModel vm = CreateIsolatedViewModel();
        TickerGroupEditorViewModel group = new();
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "fingerprint");
        SetPrivateField(vm, "_allowClose", true);

        InvokePrivate<object?>(vm, "OnEditorChanged", [group, new PropertyChangedEventArgs(propertyName)]);

        Assert.False(vm.IsValidated);
        Assert.Equal(string.Empty, GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.False(GetPrivateField<bool>(vm, "_allowClose"));
    }

    [Fact]
    public void OnStateTimerTickAsync_DoesNotTriggerBackgroundSymbolValidation()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel(new FakeConnectivityService(initiallyAvailable: true));

        TickerItemEditorViewModel ticker = new(new TickerItem { Symbol = "AAPL", Enabled = true });
        ticker.ValidationState = SymbolValidationState.Unknown;
        ticker.ValidationMessage = "Pending validation";
        TickerGroupEditorViewModel group = new();
        group.Tickers.Clear();
        group.Tickers.Add(ticker);
        vm.Groups.Clear();
        vm.Groups.Add(group);

        Task task = InvokePrivate<Task>(vm, "OnStateTimerTickAsync", []);
        PumpDispatcherUntil(task, TimeSpan.FromSeconds(5));

        Assert.Equal(SymbolValidationState.Unknown, ticker.ValidationState);
        Assert.Equal("Pending validation", ticker.ValidationMessage);
    }

    [Fact]
    public void OnStateTimerTickAsync_WhenValidated_DoesNotInvalidateIdleValidatedState()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel(new FakeConnectivityService(initiallyAvailable: true));
        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedFingerprint", "idle-fingerprint");
        vm.StatusMessage = "Validation passed. Click OK to save/apply, or Cancel to discard.";

        Task task = InvokePrivate<Task>(vm, "OnStateTimerTickAsync", []);
        PumpDispatcherUntil(task, TimeSpan.FromSeconds(5));

        Assert.True(vm.IsValidated);
        Assert.Equal("Validate", vm.PrimaryButtonText);
        Assert.False(vm.ShowValidateButton);
        Assert.True(vm.ShowValidatedActionButtons);
        Assert.Equal("idle-fingerprint", GetPrivateField<string>(vm, "_validatedFingerprint"));
        Assert.Equal("Validation passed. Click OK to save/apply, or Cancel to discard.", vm.StatusMessage);
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
    public void EnsureValidationConnectivityAsync_ForcesFreshProbeBeforeBlockingValidation()
    {
        FakeConnectivityService connectivity = new(initiallyAvailable: false);
        MainWindowViewModel vm = CreateIsolatedViewModel(connectivity);
        connectivity.SetAvailable(true);

        Task<bool> probeTask = InvokePrivate<Task<bool>>(vm, "EnsureValidationConnectivityAsync", []);
        bool available = PumpDispatcherUntil(probeTask, TimeSpan.FromSeconds(5));

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
    public void ValidateCommand_RemainsEnabledBeforeFreshConnectivityProbe()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel(new FakeConnectivityService(initiallyAvailable: false));

        Assert.False(vm.IsConfigActive);
        Assert.True(vm.ShowNetworkLockOverlay);
        Assert.True(vm.IsValidationActionEnabled);
    }

    [Fact]
    public void Validation_OffloadsTrustedSymbolProfileSave()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Settings",
            "ViewModels",
            "MainWindowViewModel.cs"));

        Assert.Contains("await SaveTrustedSymbolProfilesAsync(symbolValidation);", source, StringComparison.Ordinal);
        Assert.Contains("private readonly SemaphoreSlim _symbolProfileSaveGate = new(1, 1);", source, StringComparison.Ordinal);
        Assert.Contains("private async Task SaveTrustedSymbolProfilesAsync(YahooSymbolValidationResult validation)", source, StringComparison.Ordinal);
        Assert.Contains("await _symbolProfileSaveGate.WaitAsync();", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(() => SaveTrustedSymbolProfiles(validation));", source, StringComparison.Ordinal);
        Assert.Contains("_symbolProfileSaveGate.Release();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("            SaveTrustedSymbolProfiles(symbolValidation);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureValidationConnectivityAsync_ReturnsFalseWhenFreshProbeStillOffline()
    {
        FakeConnectivityService connectivity = new(initiallyAvailable: false);
        MainWindowViewModel vm = CreateIsolatedViewModel(connectivity);

        Task<bool> probeTask = InvokePrivate<Task<bool>>(vm, "EnsureValidationConnectivityAsync", []);
        bool available = PumpDispatcherUntil(probeTask, TimeSpan.FromSeconds(5));

        Assert.False(available);
        Assert.False(vm.IsNetworkAvailable);
        Assert.Equal(1, connectivity.ForceProbeCalls);
    }

    [Fact]
    public void ConnectivityStateUpdates_FromBackgroundThread_AreRaisedOnUiDispatcher()
    {
        int uiThreadId = Environment.CurrentManagedThreadId;
        FakeConnectivityService connectivity = new(initiallyAvailable: true);
        MainWindowViewModel vm = CreateIsolatedViewModel(connectivity);
        SetPrivateField(vm, "_isNetworkAvailable", false);
        List<int> notificationThreadIds = [];

        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.IsNetworkAvailable) ||
                args.PropertyName == nameof(MainWindowViewModel.IsConfigActive) ||
                args.PropertyName == nameof(MainWindowViewModel.ShowNetworkLockOverlay) ||
                args.PropertyName == nameof(MainWindowViewModel.IsValidationActionEnabled))
            {
                notificationThreadIds.Add(Environment.CurrentManagedThreadId);
            }
        };

        Task updateTask = Task.Run(async () =>
            await InvokePrivate<Task>(vm, "UpdateConnectivityStateAsync", [CancellationToken.None]));

        PumpDispatcherUntil(updateTask, TimeSpan.FromSeconds(5));

        Assert.True(vm.IsNetworkAvailable);
        Assert.True(vm.IsConfigActive);
        Assert.False(vm.ShowNetworkLockOverlay);
        Assert.True(vm.IsValidationActionEnabled);
        Assert.Contains(uiThreadId, notificationThreadIds);
        Assert.All(notificationThreadIds, threadId => Assert.Equal(uiThreadId, threadId));
    }

    [Fact]
    public void EndValidationRun_WhenAlreadyValidated_ShowsOkCancelActions()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel(new FakeConnectivityService(initiallyAvailable: true));
        SetPrivateField(vm, "_isValidated", true);

        InvokePrivate<object?>(vm, "BeginValidationRun", []);
        Assert.True(vm.IsApplying);
        Assert.False(vm.ShowValidatedActionButtons);

        InvokePrivate<object?>(vm, "EndValidationRun", []);

        Assert.False(vm.IsApplying);
        Assert.False(vm.ShowValidateButton);
        Assert.True(vm.ShowValidatedActionButtons);
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
                    LastValidatedUtc = DateTimeOffset.UtcNow.AddMinutes(-6)
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

    [Fact]
    public void MainWindowViewModel_ValidatedWorkflow_UsesOkCancelLanguage()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "src",
            "PortfolioSaver.Settings",
            "ViewModels",
            "MainWindowViewModel.cs"));

        Assert.Contains("Validation passed. Click OK to save/apply, or Cancel to discard.", source, StringComparison.Ordinal);
        Assert.Contains("public bool ShowValidateButton => !IsValidated;", source, StringComparison.Ordinal);
        Assert.Contains("public bool ShowValidatedActionButtons => IsValidated && !_isApplying;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Saving and closing now.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyValidatedConfiguration_SavesSeedsQuotes_AndRequestsClose()
    {
        string localDataRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(localDataRoot);
        string? originalLocalDataRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", localDataRoot);
        RuntimeQuoteSeedStore.ConsumeAll();

        try
        {
            MainWindowViewModel vm = CreateIsolatedViewModel(new FakeConnectivityService(initiallyAvailable: true));
            bool closeRequested = false;
            vm.CloseRequested += () => closeRequested = true;

            AppSettings validated = Defaults.CreateSettings();
            validated.DeepSeekApiKey = "super-secret";
            SetPrivateField(vm, "_validatedCandidateSettings", validated);
            SetPrivateField(vm, "_validatedQuoteSeeds", new List<QuoteSnapshot>
            {
                new()
                {
                    Symbol = "AAPL",
                    Last = 123.45m,
                    ChangePercent = 1.23m,
                    FetchTimestampUtc = DateTimeOffset.UtcNow
                }
            });

            InvokePrivate<object?>(vm, "ApplyValidatedConfiguration", []);

            SettingsFileService service = new();
            Assert.True(File.Exists(service.SettingsPath));
            string savedJson = File.ReadAllText(service.SettingsPath);
            Assert.DoesNotContain("super-secret", savedJson, StringComparison.Ordinal);

            IReadOnlyDictionary<string, QuoteSnapshot> published = RuntimeQuoteSeedStore.ConsumeAll();
            Assert.True(closeRequested);
            Assert.True(GetPrivateField<bool>(vm, "_allowClose"));
            Assert.Contains("saved", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
            Assert.True(published.ContainsKey("AAPL"));
        }
        finally
        {
            RuntimeQuoteSeedStore.ConsumeAll();
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

    [Fact]
    public void ExecuteCancel_RequestsCloseWithoutPublishingValidatedQuotes()
    {
        MainWindowViewModel vm = CreateIsolatedViewModel(new FakeConnectivityService(initiallyAvailable: true));
        bool closeRequested = false;
        vm.CloseRequested += () => closeRequested = true;
        RuntimeQuoteSeedStore.ConsumeAll();

        SetPrivateField(vm, "_isValidated", true);
        SetPrivateField(vm, "_validatedCandidateSettings", Defaults.CreateSettings());

        InvokePrivate<object?>(vm, "ExecuteCancel", []);

        Assert.True(closeRequested);
        Assert.True(GetPrivateField<bool>(vm, "_allowClose"));
        Assert.Equal("Validated changes discarded.", vm.StatusMessage);
        Assert.Empty(RuntimeQuoteSeedStore.ConsumeAll());
    }

    private static MainWindowViewModel CreateIsolatedViewModel(IConnectivityService? connectivity = null)
    {
        MainWindowViewModel vm = new(connectivity);
        DispatcherTimer timer = GetPrivateField<DispatcherTimer>(vm, "_stateTimer");
        timer.Stop();
        return vm;
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PortfolioScreensaver.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root from test AppContext.BaseDirectory.");
    }

    private static void PumpDispatcherUntil(Task task, TimeSpan timeout)
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!task.IsCompleted)
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Dispatcher did not complete the background connectivity update in time.");

            DispatcherFrame frame = new();
            dispatcher.BeginInvoke(
                DispatcherPriority.SystemIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        task.GetAwaiter().GetResult();
    }

    private static T PumpDispatcherUntil<T>(Task<T> task, TimeSpan timeout)
    {
        PumpDispatcherUntil((Task)task, timeout);
        return task.GetAwaiter().GetResult();
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

        public Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_available);

        public void ForceProbe()
        {
            ForceProbeCalls++;
        }

        public void SetAvailable(bool available) => _available = available;
    }
}
