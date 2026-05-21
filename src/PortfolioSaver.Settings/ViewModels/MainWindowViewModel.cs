using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using PortfolioSaver.Config.Commands;
using PortfolioSaver.Config.Services;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Core.Validation;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Helpers;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Config.ViewModels;

public sealed class MainWindowViewModel : BindableBase
{
    private readonly SettingsFileService _settingsFileService;
    private readonly SettingsValidator _settingsValidator;
    private readonly NewsFeedValidationService _newsFeedValidationService;
    private readonly IConnectivityService _connectivityService;
    private readonly YahooSymbolValidationService _yahooSymbolValidationService;
    private readonly SymbolProfileStore _symbolProfileStore;
    private readonly DispatcherTimer _stateTimer;
    private readonly HashSet<TickerGroupEditorViewModel> _trackedGroups = [];
    private readonly HashSet<TickerItemEditorViewModel> _trackedTickers = [];

    private AppSettings _settings;
    private string _statusMessage = $"{PortfolioVersion.DisplayName} ready";
    private bool _isApplying;
    private bool _isValidated;
    private bool _isValidationClosePending;
    private bool _allowClose;
    private bool _isNetworkAvailable;
    private string _validatedFingerprint = string.Empty;
    private string _validationLogText = string.Empty;
    private AppSettings? _validatedCandidateSettings;
    private List<QuoteSnapshot> _validatedQuoteSeeds = [];
    private static readonly TimeSpan CachedProfileTrustWindow = TimeSpan.FromMinutes(10);

    public MainWindowViewModel()
        : this(connectivityService: null)
    {
    }

    public MainWindowViewModel(IConnectivityService? connectivityService)
    {
        _settingsFileService = new SettingsFileService();
        _settingsValidator = new SettingsValidator();
        _newsFeedValidationService = new NewsFeedValidationService();
        _connectivityService = connectivityService ?? new ConfigConnectivityService();
        _yahooSymbolValidationService = new YahooSymbolValidationService();
        _symbolProfileStore = new SymbolProfileStore(Path.Combine(PathHelper.GetLocalDataDirectory(), "symbol-profiles.json"));

        _settings = _settingsFileService.Load();
        Groups = new ObservableCollection<TickerGroupEditorViewModel>(
            _settings.Groups.Select(group => new TickerGroupEditorViewModel(group, RemoveGroup)));
        ValidationLogText = string.Empty;

        PrimaryCommand = new RelayCommand(() => _ = ExecutePrimaryAsync(), () => !_isApplying && IsNetworkAvailable);
        CancelCommand = new RelayCommand(ExecuteCancel, () => CanExecuteCancel());
        RetryNetworkCommand = new RelayCommand(RetryConnectivity);
        AddGroupCommand = new RelayCommand(AddGroup, () => IsConfigActive);

        _stateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _stateTimer.Tick += async (_, _) => await OnStateTimerTickAsync();

        if (Groups.Count == 0)
            AddGroup();

        HookEditors();
        UpdateConnectivityState();
        ResetAllSymbolValidationStates("Pending validation");

        _stateTimer.Start();
    }

    public event Action? CloseRequested;
    public event Action<bool>? ValidationActivityChanged;

    public AppSettings Settings
    {
        get => _settings;
        set
        {
            if (!SetProperty(ref _settings, value))
                return;

            RaisePropertyChanged(nameof(IsSummarizedFinancialNewsSelected));
            RaisePropertyChanged(nameof(IsRssFeedSelected));
            RaisePropertyChanged(nameof(IsDouglasAdamsStyleSelected));
            RaisePropertyChanged(nameof(IsWilliamShakespeareStyleSelected));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsNetworkAvailable
    {
        get => _isNetworkAvailable;
        private set
        {
            if (!SetProperty(ref _isNetworkAvailable, value))
                return;

            RaisePropertyChanged(nameof(IsConfigActive));
            RaisePropertyChanged(nameof(ShowNetworkLockOverlay));
            RaiseCommandCanExecuteChanged();
        }
    }

    public bool IsConfigActive => IsNetworkAvailable && !_isApplying;
    public bool ShowNetworkLockOverlay => !IsNetworkAvailable;
    public bool IsApplying => _isApplying;
    public bool IsValidationActionEnabled => !_isApplying && IsNetworkAvailable;

    public bool IsValidated
    {
        get => _isValidated;
        private set
        {
            if (!SetProperty(ref _isValidated, value))
                return;

            RaisePropertyChanged(nameof(PrimaryButtonText));
            RaisePropertyChanged(nameof(ShowValidatedActionButtons));
            RaisePropertyChanged(nameof(IsValidationActionEnabled));
            RaiseCommandCanExecuteChanged();
        }
    }

    public string PrimaryButtonText => _isApplying ? "Validating..." : (IsValidated ? "OK" : "Validate");
    public bool ShowValidatedActionButtons => IsValidated && !_isApplying;
    public string VersionLabel => $"{PortfolioVersion.BaselineLabel} ({PortfolioVersion.SemanticVersion})";
    public string ValidationLogText
    {
        get => _validationLogText;
        private set => SetProperty(ref _validationLogText, value);
    }
    public bool IsSummarizedFinancialNewsSelected
    {
        get => Settings.NewsScrollerMode == NewsScrollerMode.SummarizedFinancialNews;
        set
        {
            if (!value || Settings.NewsScrollerMode == NewsScrollerMode.SummarizedFinancialNews)
                return;

            Settings.NewsScrollerMode = NewsScrollerMode.SummarizedFinancialNews;
            RaisePropertyChanged(nameof(IsSummarizedFinancialNewsSelected));
            RaisePropertyChanged(nameof(IsRssFeedSelected));
            InvalidateValidationState("Configuration changed. Click Validate.");
        }
    }

    public bool IsRssFeedSelected
    {
        get => Settings.NewsScrollerMode == NewsScrollerMode.RssFeed;
        set
        {
            if (!value || Settings.NewsScrollerMode == NewsScrollerMode.RssFeed)
                return;

            Settings.NewsScrollerMode = NewsScrollerMode.RssFeed;
            RaisePropertyChanged(nameof(IsSummarizedFinancialNewsSelected));
            RaisePropertyChanged(nameof(IsRssFeedSelected));
            InvalidateValidationState("Configuration changed. Click Validate.");
        }
    }

    public bool IsDouglasAdamsStyleSelected
    {
        get => Settings.DeepSeekWritingStyle == DeepSeekWritingStyle.DouglasAdams;
        set
        {
            if (!value || Settings.DeepSeekWritingStyle == DeepSeekWritingStyle.DouglasAdams)
                return;

            Settings.DeepSeekWritingStyle = DeepSeekWritingStyle.DouglasAdams;
            RaisePropertyChanged(nameof(IsDouglasAdamsStyleSelected));
            RaisePropertyChanged(nameof(IsWilliamShakespeareStyleSelected));
            InvalidateValidationState("Configuration changed. Click Validate.");
        }
    }

    public bool IsWilliamShakespeareStyleSelected
    {
        get => Settings.DeepSeekWritingStyle == DeepSeekWritingStyle.WilliamShakespeare;
        set
        {
            if (!value || Settings.DeepSeekWritingStyle == DeepSeekWritingStyle.WilliamShakespeare)
                return;

            Settings.DeepSeekWritingStyle = DeepSeekWritingStyle.WilliamShakespeare;
            RaisePropertyChanged(nameof(IsDouglasAdamsStyleSelected));
            RaisePropertyChanged(nameof(IsWilliamShakespeareStyleSelected));
            InvalidateValidationState("Configuration changed. Click Validate.");
        }
    }

    public ObservableCollection<TickerGroupEditorViewModel> Groups { get; }
    public RelayCommand PrimaryCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand RetryNetworkCommand { get; }
    public RelayCommand AddGroupCommand { get; }

    public bool CanCloseWindow()
    {
        if (_allowClose)
            return true;

        if (_isApplying)
        {
            MessageBox.Show(
                "Validation is still running. Wait for the validation loop to finish.",
                "Validation In Progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        return true;
    }

    private void AddGroup()
    {
        if (Groups.Count >= Defaults.MaxTapeCount)
        {
            StatusMessage = $"Only {Defaults.MaxTapeCount} tapes can be configured.";
            MessageBox.Show(
                $"You can configure up to {Defaults.MaxTapeCount} tapes.",
                "Tape Limit Reached",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        TickerGroupEditorViewModel group = new(Defaults.CreateEmptyTickerGroup(Groups.Count), RemoveGroup);
        Groups.Add(group);
        HookGroup(group);
        InvalidateValidationState("Configuration changed. Click Validate.");
    }

    private void RemoveGroup(TickerGroupEditorViewModel group)
    {
        UnhookGroup(group);
        Groups.Remove(group);
        InvalidateValidationState("Configuration changed. Click Validate.");
    }

    private async Task ExecutePrimaryAsync()
    {
        if (_isApplying || _isValidationClosePending)
            return;

        if (IsValidated)
        {
            ApplyValidatedConfiguration();
            return;
        }

        if (!EnsureValidationConnectivity())
        {
            StatusMessage = "Internet connection is required before validation can run.";
            MessageBox.Show(
                "Internet connection is required to validate tickers and refresh the news-source checks.",
                "Internet Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        await ValidateConfigurationAsync();
    }

    private async Task ValidateConfigurationAsync()
    {
        BeginValidationRun();
        try
        {
            AppSettings candidate = BuildCandidateSettings();
            Settings = candidate;
            AppendValidationLog("VALIDATION STARTED");

            if (candidate.NewsScrollerMode == NewsScrollerMode.RssFeed)
            {
                AppendValidationLog("RSS FEED CHECK...");
                NewsFeedValidationResult feedValidation = await _newsFeedValidationService.ValidateAsync(
                    candidate.NewsFeedUrl,
                    candidate.HttpTimeoutSeconds,
                    IsNetworkAvailable);
                if (feedValidation.WasResetToDefault)
                {
                    candidate.NewsFeedUrl = feedValidation.ResolvedFeedUrl;
                    MessageBox.Show(
                        feedValidation.Message,
                        "News Feed Reset",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    AppendValidationLog("RSS FEED RESET TO DEFAULT");
                }
                else
                {
                    candidate.NewsFeedUrl = feedValidation.ResolvedFeedUrl;
                    AppendValidationLog(feedValidation.ValidationSkipped ? "RSS FEED CHECK SKIPPED" : "RSS FEED OK");
                }
            }

            IReadOnlyList<string> configErrors = _settingsValidator.Validate(candidate);
            if (configErrors.Count > 0)
            {
                StatusMessage = configErrors[0];
                foreach (string configError in configErrors)
                    AppendValidationLog($"SETTINGS: {configError}");
                MessageBox.Show(
                    string.Join(Environment.NewLine, configErrors),
                    "Settings Need Attention",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            List<string> enabledSymbols = GetEnabledSymbols(candidate).ToList();
            AppendValidationLog($"TICKER VALIDATION: {enabledSymbols.Count} SYMBOL(S)");
            YahooSymbolValidationResult symbolValidation = await ValidateSymbolsAgainstSourcesAsync(candidate, enabledSymbols);
            int autoNamedCount = ApplyResolvedDisplayNames(symbolValidation);
            candidate = BuildCandidateSettings();
            Settings = candidate;
            SaveTrustedSymbolProfiles(symbolValidation);
            if (symbolValidation.WasRateLimited || symbolValidation.DeferredSymbols.Count > 0)
            {
                string deferredList = string.Join(", ", symbolValidation.DeferredSymbols.Take(8));
                StatusMessage = "YFinance.NET throttled ticker validation. Nothing was disabled; try Validate again shortly.";
                AppendValidationLog("TICKER VALIDATION DEFERRED BY YAHOO RATE LIMITING");
                TraceValidation("TickerValidationDeferred",
                    ("rate_limited", symbolValidation.WasRateLimited),
                    ("deferred_count", symbolValidation.DeferredSymbols.Count),
                    ("invalid_count", symbolValidation.InvalidSymbols.Count));
                MessageBox.Show(
                    "YFinance.NET temporarily throttled ticker validation." + Environment.NewLine + Environment.NewLine +
                    "No ticker entries were disabled during this pass." + Environment.NewLine +
                    "Wait a little and click Validate again." +
                    (string.IsNullOrWhiteSpace(deferredList) ? string.Empty : Environment.NewLine + Environment.NewLine + "Deferred symbols: " + deferredList),
                    "Ticker Validation Rate Limited",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            if (symbolValidation.InvalidSymbols.Count > 0)
            {
                int disabledCount = DisableInvalidSymbols(symbolValidation.InvalidSymbols);
                StatusMessage = "Ticker validation failed. Correct invalid symbols and validate again.";
                AppendValidationLog($"TICKER VALIDATION FAILED: {symbolValidation.InvalidSymbols.Count} INVALID, {disabledCount} DISABLED");
                TraceValidation("TickerValidationInvalid",
                    ("invalid_count", symbolValidation.InvalidSymbols.Count),
                    ("disabled_count", disabledCount));
                MessageBox.Show(
                    "These symbols are invalid on YFinance.NET:" + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, symbolValidation.InvalidSymbols.Select(symbol => $"- {symbol}")) +
                    Environment.NewLine + Environment.NewLine +
                    $"Disabled entries: {disabledCount}.",
                    "Invalid Ticker Symbols",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            AppendValidationLog(autoNamedCount > 0
                ? $"DISPLAY NAMES UPDATED: {autoNamedCount}"
                : "DISPLAY NAMES UNCHANGED");

            AppendValidationLog("YFINANCE.NET-ONLY MODE: NO ADDITIONAL MARKET-DATA API KEY VALIDATION");
            Settings = candidate;
            IsValidated = true;
            _validatedFingerprint = BuildFingerprint(candidate);
            _allowClose = false;
            _validatedCandidateSettings = AppSettingsNormalizer.Normalize(candidate);
            _validatedQuoteSeeds = symbolValidation.ValidatedQuotes.Values
                .Select(CloneQuote)
                .ToList();
            AppendValidationLog("VALIDATION PASSED");
            TraceValidation("ValidationPassed", ("auto_named_count", autoNamedCount));
            UpdateValidatedCloseStatus(autoNamedCount);
        }
        catch (Exception ex)
        {
            StatusMessage = "Validation stopped unexpectedly. Review the details and try again.";
            AppendValidationLog($"VALIDATION ERROR: {ex.Message}");
            TraceLog.Error("Config.Validation", "ValidateConfigurationAsync", ex);
            MessageBox.Show(
                $"Validation stopped unexpectedly:{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            EndValidationRun();
        }
    }

    private async Task<YahooSymbolValidationResult> ValidateSymbolsAgainstSourcesAsync(AppSettings settings, IReadOnlyList<string> enabledSymbols)
    {
        YahooSymbolValidationResult aggregate = new(enabledSymbols);
        List<TrustedValidationEvidence> trustedEvidence = GetTrustedValidationEvidence(enabledSymbols);
        HashSet<string> trustedSymbols = trustedEvidence
            .Select(entry => entry.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        TraceValidation(
            "TickerValidationTrustPlan",
            ("requested_count", enabledSymbols.Count),
            ("trusted_count", trustedEvidence.Count),
            ("trusted_symbols", string.Join(", ", trustedEvidence.Select(entry => entry.Symbol))));

        foreach (TrustedValidationEvidence trusted in trustedEvidence)
        {
            aggregate.MarkValid(trusted.Symbol, trusted.DisplayName, trusted.DisplayName);
            MarkSymbolState(trusted.Symbol, SymbolValidationState.Valid, trusted.Message);
            AppendValidationLog($"{trusted.Symbol} -> {(!string.IsNullOrWhiteSpace(trusted.DisplayName) ? trusted.DisplayName : "VALID")} ({trusted.SourceTag})");
            TraceValidation(
                "TickerValidationProgress",
                ("symbol", trusted.Symbol),
                ("is_valid", true),
                ("message", trusted.Message),
                ("resolved_name", trusted.DisplayName),
                ("source", trusted.SourceTag));
        }

        List<string> networkSymbols = enabledSymbols
            .Select(SymbolProfileHeuristics.Normalize)
            .Where(symbol => !trustedSymbols.Contains(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        TraceValidation(
            "TickerValidationNetworkPlan",
            ("network_symbol_count", networkSymbols.Count),
            ("network_symbols", string.Join(", ", networkSymbols)));

        if (networkSymbols.Count > 0)
        {
            MarkSymbolStates(networkSymbols, SymbolValidationState.Checking, "Checking YFinance.NET...");

            YahooSymbolValidationResult networkResult = await _yahooSymbolValidationService.ValidateAsync(
                networkSymbols,
                settings.HttpTimeoutSeconds,
                new Progress<YahooSymbolValidationProgress>(ReportSymbolValidationProgress));
            aggregate.MergeFrom(networkResult);
        }
        else
        {
            AppendValidationLog("YFINANCE LOOKUP SKIPPED: ALL SYMBOLS ALREADY TRUSTED LOCALLY");
        }

        foreach (string symbol in enabledSymbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string normalized = SymbolProfileHeuristics.Normalize(symbol);
            if (aggregate.Entries.TryGetValue(normalized, out YahooSymbolValidationEntry? entry) && entry.IsValid)
            {
                if (!trustedSymbols.Contains(normalized))
                    MarkSymbolState(normalized, SymbolValidationState.Valid, "Validated via YFinance.NET");
                continue;
            }

            if (aggregate.Entries.TryGetValue(normalized, out entry) && entry.WasChecked)
            {
                MarkSymbolState(normalized, SymbolValidationState.Invalid, "YFinance.NET does not recognize this symbol");
                continue;
            }

            string deferredMessage = aggregate.Entries.TryGetValue(normalized, out entry) && !string.IsNullOrWhiteSpace(entry.FailureReason)
                ? entry.FailureReason
                : "Validation deferred";
            MarkSymbolState(normalized, SymbolValidationState.Unknown, deferredMessage);
        }

        MarkDisabledSymbolsAsUnknown(enabledSymbols);
        return aggregate;
    }

    private int DisableInvalidSymbols(IEnumerable<string> invalidSymbols)
    {
        HashSet<string> invalidNormalized = invalidSymbols
            .Select(SymbolProfileHeuristics.Normalize)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (invalidNormalized.Count == 0)
            return 0;

        int disabled = 0;
        foreach (TickerItemEditorViewModel ticker in EnumerateTickerEditors())
        {
            if (!ticker.Enabled)
                continue;

            string normalized = SymbolProfileHeuristics.Normalize(ticker.Symbol);
            if (!invalidNormalized.Contains(normalized))
                continue;

            ticker.Enabled = false;
            ticker.ValidationState = SymbolValidationState.Invalid;
            ticker.ValidationMessage = "Disabled because YFinance.NET validation failed";
            disabled++;
        }

        return disabled;
    }

    private int ApplyResolvedDisplayNames(YahooSymbolValidationResult validation)
    {
        int updated = 0;
        foreach (TickerGroupEditorViewModel group in Groups)
        {
            foreach (TickerItemEditorViewModel ticker in group.Tickers)
            {
                string normalized = SymbolProfileHeuristics.Normalize(ticker.Symbol);
                if (!validation.Entries.TryGetValue(normalized, out YahooSymbolValidationEntry? entry) ||
                    string.IsNullOrWhiteSpace(entry.DisplayName))
                {
                    continue;
                }

                if (string.Equals((ticker.DisplayName ?? string.Empty).Trim(), entry.DisplayName.Trim(), StringComparison.Ordinal))
                    continue;

                ticker.DisplayName = entry.DisplayName;
                updated++;
            }
        }

        return updated;
    }

    private List<TrustedValidationEvidence> GetTrustedValidationEvidence(IReadOnlyList<string> enabledSymbols)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        IReadOnlyDictionary<string, SymbolProfile> cachedProfiles = _symbolProfileStore.Load();
        Dictionary<string, string> currentDisplayNames = EnumerateTickerEditors()
            .Where(ticker => ticker.Enabled && !string.IsNullOrWhiteSpace(ticker.Symbol))
            .GroupBy(ticker => SymbolProfileHeuristics.Normalize(ticker.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(ticker => (ticker.DisplayName ?? string.Empty).Trim())
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        List<TrustedValidationEvidence> trusted = [];
        foreach (string symbol in enabledSymbols.Select(SymbolProfileHeuristics.Normalize).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (cachedProfiles.TryGetValue(symbol, out SymbolProfile? profile) &&
                profile.LastValidatedUtc > DateTimeOffset.MinValue &&
                nowUtc - profile.LastValidatedUtc <= CachedProfileTrustWindow)
            {
                string profileName = !string.IsNullOrWhiteSpace(profile.DisplayName)
                    ? profile.DisplayName.Trim()
                    : currentDisplayNames.GetValueOrDefault(symbol, string.Empty);
                trusted.Add(new TrustedValidationEvidence(
                    symbol,
                    profileName,
                    "Validated from cached symbol profile",
                    "LOCAL-PROFILE"));
            }
        }

        return trusted;
    }

    private void SaveTrustedSymbolProfiles(YahooSymbolValidationResult validation)
    {
        IReadOnlyDictionary<string, SymbolProfile> existing = _symbolProfileStore.Load();
        Dictionary<string, SymbolProfile> merged = existing.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        foreach (YahooSymbolValidationEntry entry in validation.Entries.Values.Where(entry => entry.IsValid))
        {
            if (!merged.TryGetValue(entry.Symbol, out SymbolProfile? profile))
            {
                profile = new SymbolProfile
                {
                    Symbol = entry.Symbol,
                    CanonicalSymbol = entry.Symbol
                };
            }

            profile.Symbol = entry.Symbol;
            profile.CanonicalSymbol = string.IsNullOrWhiteSpace(profile.CanonicalSymbol) ? entry.Symbol : profile.CanonicalSymbol;
            if (!string.IsNullOrWhiteSpace(entry.DisplayName))
                profile.DisplayName = entry.DisplayName.Trim();
            profile.LastValidatedUtc = nowUtc;
            if (string.IsNullOrWhiteSpace(profile.ValidationSummary))
                profile.ValidationSummary = "Validated during config apply.";

            merged[entry.Symbol] = profile;
        }

        _symbolProfileStore.Save(merged.Values);
    }

    private async Task OnStateTimerTickAsync()
    {
        UpdateConnectivityState();
        if (!IsConfigActive)
            return;

        AppSettings candidateSettings = BuildCandidateSettings();

        if (IsValidated)
        {
            string currentFingerprint = BuildFingerprint(candidateSettings);
            if (!string.Equals(currentFingerprint, _validatedFingerprint, StringComparison.Ordinal))
                InvalidateValidationState("Configuration changed. Click Validate.");
        }
    }

    private AppSettings BuildCandidateSettings()
    {
        AppSettings candidate = AppSettingsNormalizer.Normalize(Settings);
        candidate.Groups = Groups.Select(group => group.ToModel()).ToList();
        return AppSettingsNormalizer.Normalize(candidate);
    }

    private static string BuildFingerprint(AppSettings settings)
        => JsonSerializer.Serialize(settings);

    private static IEnumerable<string> GetEnabledSymbols(AppSettings settings)
    {
        return settings.Groups
            .Where(group => group.Enabled)
            .SelectMany(group => group.Tickers)
            .Where(ticker => ticker.Enabled && !string.IsNullOrWhiteSpace(ticker.Symbol))
            .Select(ticker => ticker.Symbol.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void MarkSymbolStates(IEnumerable<string> symbols, SymbolValidationState state, string message)
    {
        HashSet<string> normalizedSymbols = symbols
            .Select(SymbolProfileHeuristics.Normalize)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (TickerItemEditorViewModel ticker in EnumerateTickerEditors())
        {
            string normalized = SymbolProfileHeuristics.Normalize(ticker.Symbol);
            if (!normalizedSymbols.Contains(normalized))
                continue;

            ticker.ValidationState = state;
            ticker.ValidationMessage = message;
        }
    }

    private void MarkSymbolState(string symbol, SymbolValidationState state, string message)
    {
        string normalized = SymbolProfileHeuristics.Normalize(symbol);
        foreach (TickerItemEditorViewModel ticker in EnumerateTickerEditors())
        {
            if (!string.Equals(SymbolProfileHeuristics.Normalize(ticker.Symbol), normalized, StringComparison.OrdinalIgnoreCase))
                continue;

            ticker.ValidationState = state;
            ticker.ValidationMessage = message;
        }
    }

    private void MarkDisabledSymbolsAsUnknown(IReadOnlyList<string> enabledSymbols)
    {
        HashSet<string> normalizedEnabled = enabledSymbols
            .Select(SymbolProfileHeuristics.Normalize)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (TickerItemEditorViewModel ticker in EnumerateTickerEditors())
        {
            string normalized = SymbolProfileHeuristics.Normalize(ticker.Symbol);
            if (!ticker.Enabled || string.IsNullOrWhiteSpace(normalized))
            {
                ticker.ValidationState = SymbolValidationState.Unknown;
                ticker.ValidationMessage = "Disabled";
                continue;
            }

            if (!normalizedEnabled.Contains(normalized))
            {
                ticker.ValidationState = SymbolValidationState.Unknown;
                ticker.ValidationMessage = "Pending validation";
            }
        }
    }

    private void ResetAllSymbolValidationStates(string message)
    {
        foreach (TickerItemEditorViewModel ticker in EnumerateTickerEditors())
        {
            ticker.ValidationState = SymbolValidationState.Unknown;
            ticker.ValidationMessage = message;
        }
    }

    private IEnumerable<TickerItemEditorViewModel> EnumerateTickerEditors()
        => Groups.SelectMany(group => group.Tickers);

    private void RetryConnectivity()
    {
        _connectivityService.ForceProbe();
        UpdateConnectivityState();
        StatusMessage = IsNetworkAvailable
            ? "Internet connection detected. Continue with Validate."
            : "Internet connection not detected yet.";
    }

    private void UpdateConnectivityState()
    {
        bool wasNetworkAvailable = _isNetworkAvailable;
        bool connected = _connectivityService.IsInternetAvailable();
        IsNetworkAvailable = connected;
        if (!connected)
        {
            InvalidateValidationState("Internet connection is required for ticker validation.");
            ResetAllSymbolValidationStates("Internet required");
            return;
        }

        if (!wasNetworkAvailable &&
            (string.IsNullOrWhiteSpace(StatusMessage) ||
             StatusMessage.StartsWith("Internet connection", StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "Internet connection detected. Continue with Validate.";
        }
    }

    private bool EnsureValidationConnectivity()
    {
        if (IsNetworkAvailable)
            return true;

        _connectivityService.ForceProbe();
        UpdateConnectivityState();
        return IsNetworkAvailable;
    }

    private void SetApplying(bool applying)
    {
        _isApplying = applying;
        RaisePropertyChanged(nameof(IsApplying));
        RaisePropertyChanged(nameof(IsConfigActive));
        RaisePropertyChanged(nameof(IsValidationActionEnabled));
        RaisePropertyChanged(nameof(ShowNetworkLockOverlay));
        RaisePropertyChanged(nameof(PrimaryButtonText));
        RaiseCommandCanExecuteChanged();
        ValidationActivityChanged?.Invoke(applying);
    }

    private void RaiseCommandCanExecuteChanged()
    {
        PrimaryCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        AddGroupCommand.RaiseCanExecuteChanged();
    }

    private void InvalidateValidationState(string statusMessage)
    {
        CancelValidatedCloseSequence();
        IsValidated = false;
        _allowClose = false;
        _validatedFingerprint = string.Empty;
        _validatedCandidateSettings = null;
        _validatedQuoteSeeds = [];
        if (!string.IsNullOrWhiteSpace(statusMessage))
            StatusMessage = statusMessage;
    }

    private void HookEditors()
    {
        foreach (TickerGroupEditorViewModel group in Groups)
            HookGroup(group);
    }

    private void HookGroup(TickerGroupEditorViewModel group)
    {
        if (!_trackedGroups.Add(group))
            return;

        group.PropertyChanged += OnEditorChanged;
        group.Tickers.CollectionChanged += OnGroupTickersChanged;
        foreach (TickerItemEditorViewModel ticker in group.Tickers)
            HookTicker(ticker);
    }

    private void UnhookGroup(TickerGroupEditorViewModel group)
    {
        if (!_trackedGroups.Remove(group))
            return;

        group.PropertyChanged -= OnEditorChanged;
        group.Tickers.CollectionChanged -= OnGroupTickersChanged;
        foreach (TickerItemEditorViewModel ticker in group.Tickers)
            UnhookTicker(ticker);
    }

    private void HookTicker(TickerItemEditorViewModel ticker)
    {
        if (!_trackedTickers.Add(ticker))
            return;

        ticker.PropertyChanged += OnEditorChanged;
    }

    private void UnhookTicker(TickerItemEditorViewModel ticker)
    {
        if (!_trackedTickers.Remove(ticker))
            return;

        ticker.PropertyChanged -= OnEditorChanged;
    }

    private void OnGroupTickersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TickerItemEditorViewModel ticker in e.OldItems.OfType<TickerItemEditorViewModel>())
                UnhookTicker(ticker);
        }

        if (e.NewItems is not null)
        {
            foreach (TickerItemEditorViewModel ticker in e.NewItems.OfType<TickerItemEditorViewModel>())
                HookTicker(ticker);
        }

        InvalidateValidationState("Configuration changed. Click Validate.");
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplying)
            return;

        if (string.Equals(e.PropertyName, nameof(TickerItemEditorViewModel.ValidationState), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(TickerItemEditorViewModel.ValidationMessage), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(TickerItemEditorViewModel.ValidationBadgeText), StringComparison.Ordinal))
        {
            return;
        }

        if (IsValidated)
        {
            string currentFingerprint = BuildFingerprint(BuildCandidateSettings());
            if (string.Equals(_validatedFingerprint, currentFingerprint, StringComparison.Ordinal))
                return;
        }

        InvalidateValidationState("Configuration changed. Click Validate.");
    }

    private void CancelValidatedCloseSequence()
    {
        if (!_isValidationClosePending)
            return;

        _isValidationClosePending = false;
        RaisePropertyChanged(nameof(IsConfigActive));
        RaisePropertyChanged(nameof(ShowNetworkLockOverlay));
        RaiseCommandCanExecuteChanged();
    }

    private void UpdateValidatedCloseStatus(int autoNamedCount)
    {
        string namingText = autoNamedCount > 0
            ? $"Filled {autoNamedCount} symbol name(s). "
            : string.Empty;
        StatusMessage = $"{namingText}Validation passed. Click OK to save/apply, or Cancel to discard.";
    }

    private bool CanExecuteCancel()
        => !_isApplying && IsValidated;

    private void ExecuteCancel()
    {
        if (!CanExecuteCancel())
            return;

        _allowClose = true;
        StatusMessage = "Validated changes discarded.";
        CloseRequested?.Invoke();
    }

    private void ApplyValidatedConfiguration()
    {
        if (_validatedCandidateSettings is null)
            return;

        RuntimeQuoteSeedStore.Publish(_validatedQuoteSeeds);
        AppendValidationLog($"RUNTIME QUOTE SEED: {_validatedQuoteSeeds.Count} SYMBOL(S)");
        _settingsFileService.Save(_validatedCandidateSettings);
        _allowClose = true;
        StatusMessage = $"{PortfolioVersion.BaselineLabel} saved at {DateTime.Now:T}.";
        CloseRequested?.Invoke();
    }

    private static QuoteSnapshot CloneQuote(QuoteSnapshot quote) => new()
    {
        Symbol = quote.Symbol,
        Last = quote.Last,
        Change = quote.Change,
        ChangePercent = quote.ChangePercent,
        PreviousClose = quote.PreviousClose,
        FetchTimestampUtc = quote.FetchTimestampUtc,
        ProviderTimestampUtc = quote.ProviderTimestampUtc,
        Currency = quote.Currency,
        MarketSession = quote.MarketSession,
        IsStale = quote.IsStale
    };

    private void BeginValidationRun()
    {
        ValidationLogText = string.Empty;
        SetApplying(true);
    }

    private void EndValidationRun()
    {
        SetApplying(false);
    }

    private void AppendValidationLog(string line)
    {
        string normalized = (line ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        ValidationLogText = string.IsNullOrWhiteSpace(ValidationLogText)
            ? normalized
            : $"{ValidationLogText}{Environment.NewLine}{normalized}";
    }

    private void ReportSymbolValidationProgress(YahooSymbolValidationProgress progress)
    {
        string message = progress.IsValid
            ? $"{progress.Symbol} -> {(!string.IsNullOrWhiteSpace(progress.ResolvedName) ? progress.ResolvedName : "VALID")}"
            : $"{progress.Symbol} -> {progress.Message.ToUpperInvariant()}";
        AppendValidationLog(message);
        TraceValidation("TickerValidationProgress",
            ("symbol", progress.Symbol),
            ("is_valid", progress.IsValid),
            ("message", progress.Message),
            ("resolved_name", progress.ResolvedName));
    }

    private static void TraceValidation(string eventName, params (string Key, object? Value)[] fields)
        => TraceLog.InfoState(
            "Config.Validation",
            eventName,
            fields.Select(field => new KeyValuePair<string, object?>(field.Key, field.Value)).ToArray());

    private sealed record TrustedValidationEvidence(
        string Symbol,
        string DisplayName,
        string Message,
        string SourceTag);
}
