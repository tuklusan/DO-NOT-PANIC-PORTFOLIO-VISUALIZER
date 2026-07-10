// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
// ============================================================================
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Forms = System.Windows.Forms;
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

public sealed class MainWindowViewModel : BindableBase, IDisposable
{
    private readonly SettingsFileService _settingsFileService;
    private readonly SettingsValidator _settingsValidator;
    private readonly NewsFeedValidationService _newsFeedValidationService;
    private readonly IAiNewsAccessValidationService _aiNewsAccessValidationService;
    private readonly IConfigDialogService _dialogService;
    private readonly IConnectivityService _connectivityService;
    private readonly bool _ownsConnectivityService;
    private readonly IYahooSymbolValidationService _yahooSymbolValidationService;
    private readonly SymbolProfileStore _symbolProfileStore;
    private AppSettings _loadedSettingsSnapshot;
    private readonly Dispatcher _uiDispatcher;
    private readonly SemaphoreSlim _symbolProfileSaveGate = new(1, 1);
    private readonly object _connectivityRefreshGate = new();
    private readonly HashSet<TickerGroupEditorViewModel> _trackedGroups = [];
    private readonly HashSet<TickerItemEditorViewModel> _trackedTickers = [];
    private readonly Dictionary<TickerGroupEditorViewModel, HashSet<TickerItemEditorViewModel>> _trackedTickersByGroup = [];
    private CancellationTokenSource? _validationCancellation;
    private bool _connectivityRefreshRunning;
    private bool _connectivityRefreshQueued;
    private bool _isDisposed;

    private AppSettings _settings;
    private string _statusMessage = $"{PortfolioVersion.DisplayName} ready";
    private bool _isApplying;
    private bool _isValidated;
    private bool _isValidationClosePending;
    private bool _allowClose;
    private bool _isNetworkAvailable;
    private string _validatedFingerprint = string.Empty;
    private readonly Dictionary<TickerGroupEditorViewModel, TickerGroup> _validatedGroupSnapshots = [];
    private readonly Dictionary<TickerItemEditorViewModel, TickerItem> _validatedTickerSnapshots = [];
    private string _validationLogText = string.Empty;
    private AppSettings? _validatedCandidateSettings;
    private List<QuoteSnapshot> _validatedQuoteSeeds = [];
    private static readonly TimeSpan CachedProfileTrustWindow = TimeSpan.FromMinutes(10);

    public MainWindowViewModel()
        : this(connectivityService: null, aiNewsAccessValidationService: null, yahooSymbolValidationService: null, dialogService: null)
    {
    }

    public MainWindowViewModel(IConnectivityService? connectivityService)
        : this(connectivityService, aiNewsAccessValidationService: null, yahooSymbolValidationService: null, dialogService: null)
    {
    }

    public MainWindowViewModel(
        IConnectivityService? connectivityService,
        IAiNewsAccessValidationService? aiNewsAccessValidationService = null,
        IYahooSymbolValidationService? yahooSymbolValidationService = null,
        IConfigDialogService? dialogService = null)
    {
        _settingsFileService = new SettingsFileService();
        _settingsValidator = new SettingsValidator();
        _newsFeedValidationService = new NewsFeedValidationService();
        _aiNewsAccessValidationService = aiNewsAccessValidationService ?? new AiNewsAccessValidationService();
        _dialogService = dialogService ?? new WpfConfigDialogService();
        if (connectivityService is null)
        {
            _connectivityService = new ConfigConnectivityService();
            _ownsConnectivityService = true;
        }
        else
        {
            _connectivityService = connectivityService;
        }

        _yahooSymbolValidationService = yahooSymbolValidationService ?? new YahooSymbolValidationService();
        _symbolProfileStore = new SymbolProfileStore(Path.Combine(PathHelper.GetLocalDataDirectory(), "symbol-profiles.json"));
        _uiDispatcher = Dispatcher.CurrentDispatcher;

        _settings = _settingsFileService.Load();
        // This snapshot is scoped to one settings window instance. If the app ever adds
        // in-window reload-from-disk, refresh this snapshot at the same time.
        _loadedSettingsSnapshot = AppSettingsNormalizer.Normalize(_settings).Clone();
        Groups = new ObservableCollection<TickerGroupEditorViewModel>(
            _settings.Groups.Select(group => new TickerGroupEditorViewModel(group, RemoveGroup)));
        ValidationLogText = string.Empty;

        PrimaryCommand = new RelayCommand(() => _ = ExecutePrimaryAsync(), () => CanExecuteValidate());
        OkCommand = new RelayCommand(ExecuteOk, () => CanExecuteOk());
        CancelCommand = new RelayCommand(ExecuteCancel, () => CanExecuteCancel());
        RetryNetworkCommand = new RelayCommand(RetryConnectivity);
        AddGroupCommand = new RelayCommand(AddGroup, () => IsConfigActive);
        ChooseBackgroundFolderCommand = new RelayCommand(ChooseBackgroundFolder);

        _connectivityService.ConnectivityChanged += OnConnectivityChanged;

        if (Groups.Count == 0)
            AddGroup();

        Groups.CollectionChanged += OnGroupsChanged;
        HookEditors();
        RunConnectivityUpdateInBackground();
        ResetAllSymbolValidationStates("Pending validation");
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
    public bool IsValidationActionEnabled => CanExecuteValidate();

    public bool IsValidated
    {
        get => _isValidated;
        private set
        {
            if (!SetProperty(ref _isValidated, value))
                return;

            RaisePropertyChanged(nameof(PrimaryButtonText));
            RaisePropertyChanged(nameof(ShowValidateButton));
            RaisePropertyChanged(nameof(ShowValidatedActionButtons));
            RaisePropertyChanged(nameof(IsValidationActionEnabled));
            RaiseCommandCanExecuteChanged();
        }
    }

    public string PrimaryButtonText => _isApplying ? "Validating..." : "Validate";
    public bool ShowValidateButton => !IsValidated;
    public bool ShowValidatedActionButtons => IsValidated && !_isApplying;
    public string VersionLabel => PortfolioVersion.Version;
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
        get => Settings.AiWritingStyle == AiWritingStyle.DouglasAdams;
        set
        {
            if (!value || Settings.AiWritingStyle == AiWritingStyle.DouglasAdams)
                return;

            Settings.AiWritingStyle = AiWritingStyle.DouglasAdams;
            RaisePropertyChanged(nameof(IsDouglasAdamsStyleSelected));
            RaisePropertyChanged(nameof(IsWilliamShakespeareStyleSelected));
            InvalidateValidationState("Configuration changed. Click Validate.");
        }
    }

    public bool IsWilliamShakespeareStyleSelected
    {
        get => Settings.AiWritingStyle == AiWritingStyle.WilliamShakespeare;
        set
        {
            if (!value || Settings.AiWritingStyle == AiWritingStyle.WilliamShakespeare)
                return;

            Settings.AiWritingStyle = AiWritingStyle.WilliamShakespeare;
            RaisePropertyChanged(nameof(IsDouglasAdamsStyleSelected));
            RaisePropertyChanged(nameof(IsWilliamShakespeareStyleSelected));
            InvalidateValidationState("Configuration changed. Click Validate.");
        }
    }

    public ObservableCollection<TickerGroupEditorViewModel> Groups { get; }
    public RelayCommand PrimaryCommand { get; }
    public RelayCommand OkCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand RetryNetworkCommand { get; }
    public RelayCommand AddGroupCommand { get; }
    public RelayCommand ChooseBackgroundFolderCommand { get; }

    public bool CanCloseWindow()
    {
        if (_allowClose)
            return true;

        if (_isApplying)
        {
            CancelActiveValidation();
            StatusMessage = "Close requested; cancelling validation. Try closing again shortly.";
            return false;
        }

        return true;
    }

    private void AddGroup()
    {
        if (Groups.Count >= Defaults.MaxTapeCount)
        {
            StatusMessage = $"Only {Defaults.MaxTapeCount} tapes can be configured.";
            _dialogService.Show(
                $"You can configure up to {Defaults.MaxTapeCount} tapes.",
                "Tape Limit Reached",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        TickerGroupEditorViewModel group = new(Defaults.CreateEmptyTickerGroup(Groups.Count), RemoveGroup);
        Groups.Add(group);
    }

    private void RemoveGroup(TickerGroupEditorViewModel group)
    {
        Groups.Remove(group);
    }

    private async Task ExecutePrimaryAsync()
    {
        if (_isApplying || _isValidationClosePending)
            return;

        if (!await EnsureValidationConnectivityAsync())
        {
            StatusMessage = "Internet connection is required before validation can run.";
            _dialogService.Show(
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
        CancellationToken validationCancellationToken = _validationCancellation?.Token ?? CancellationToken.None;
        IsValidated = false;
        bool completedValidatedState = false;
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
                    IsNetworkAvailable,
                    validationCancellationToken);
                validationCancellationToken.ThrowIfCancellationRequested();
                if (feedValidation.WasResetToDefault)
                {
                    candidate.NewsFeedUrl = feedValidation.ResolvedFeedUrl;
                    _dialogService.Show(
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
            else if (ShouldValidateAiNewsAccess(candidate))
            {
                AppendValidationLog("AI NEWS ACCESS CHECK...");
                AiNewsAccessValidationResult aiValidation = await _aiNewsAccessValidationService.ValidateAsync(
                    candidate,
                    IsNetworkAvailable,
                    validationCancellationToken);
                validationCancellationToken.ThrowIfCancellationRequested();
                if (!aiValidation.IsValid)
                {
                    StatusMessage = "AI news access validation failed. Correct AI settings or switch Finance News to RSS Feed.";
                    AppendValidationLog($"AI NEWS ACCESS FAILED: {aiValidation.Message}");
                    TraceValidation("AiNewsAccessValidationFailed", ("message", aiValidation.Message));
                    EndValidationRun();
                    completedValidatedState = true;
                    _dialogService.Show(
                        aiValidation.Message,
                        "AI News Access Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                AppendValidationLog(aiValidation.ValidationSkipped ? "AI NEWS ACCESS CHECK SKIPPED" : "AI NEWS ACCESS OK");
                TraceValidation(aiValidation.ValidationSkipped ? "AiNewsAccessValidationSkipped" : "AiNewsAccessValidationSucceeded");
            }
            else
            {
                AppendValidationLog("AI NEWS ACCESS CHECK SKIPPED: AI ACCESS SETTINGS UNCHANGED");
                TraceValidation("AiNewsAccessValidationSkippedUnchanged");
            }

            IReadOnlyList<string> configErrors = _settingsValidator.Validate(candidate);
            if (configErrors.Count > 0)
            {
                StatusMessage = configErrors[0];
                foreach (string configError in configErrors)
                    AppendValidationLog($"SETTINGS: {configError}");
                _dialogService.Show(
                    string.Join(Environment.NewLine, configErrors),
                    "Settings Need Attention",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            List<string> enabledSymbols = GetEnabledSymbols(candidate).ToList();
            AppendValidationLog($"TICKER VALIDATION: {enabledSymbols.Count} SYMBOL(S)");
            YahooSymbolValidationResult symbolValidation = await ValidateSymbolsAgainstSourcesAsync(candidate, enabledSymbols, validationCancellationToken);
            validationCancellationToken.ThrowIfCancellationRequested();
            int autoNamedCount = ApplyResolvedDisplayNames(symbolValidation);
            candidate = BuildCandidateSettings();
            Settings = candidate;
            await SaveTrustedSymbolProfilesAsync(symbolValidation);
            if (symbolValidation.WasRateLimited || symbolValidation.DeferredSymbols.Count > 0)
            {
                string deferredList = string.Join(", ", symbolValidation.DeferredSymbols.Take(8));
                bool wasRateLimited = symbolValidation.WasRateLimited;
                string statusMessage = wasRateLimited
                    ? "YFinance.NET throttled ticker validation. Nothing was disabled; try Validate again shortly."
                    : "YFinance.NET validation is unavailable. Nothing was disabled; check connection and try Validate again.";
                string logMessage = wasRateLimited
                    ? "TICKER VALIDATION DEFERRED BY YAHOO RATE LIMITING"
                    : "TICKER VALIDATION DEFERRED: YFINANCE.NET UNAVAILABLE";
                string dialogTitle = wasRateLimited
                    ? "Ticker Validation Rate Limited"
                    : "Ticker Validation Unavailable";
                string dialogLead = wasRateLimited
                    ? "YFinance.NET temporarily throttled ticker validation."
                    : "YFinance.NET could not validate tickers right now.";
                string retryInstruction = wasRateLimited
                    ? "Wait a little and click Validate again."
                    : "Check the connection and click Validate again.";
                StatusMessage = statusMessage;
                AppendValidationLog(logMessage);
                TraceValidation("TickerValidationDeferred",
                    ("rate_limited", symbolValidation.WasRateLimited),
                    ("deferred_count", symbolValidation.DeferredSymbols.Count),
                    ("invalid_count", symbolValidation.InvalidSymbols.Count));
                EndValidationRun();
                completedValidatedState = true;
                _dialogService.Show(
                    dialogLead + Environment.NewLine + Environment.NewLine +
                    "No ticker entries were disabled during this pass." + Environment.NewLine +
                    retryInstruction +
                    (string.IsNullOrWhiteSpace(deferredList) ? string.Empty : Environment.NewLine + Environment.NewLine + "Deferred symbols: " + deferredList),
                    dialogTitle,
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
                EndValidationRun();
                completedValidatedState = true;
                _dialogService.Show(
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
            _loadedSettingsSnapshot = _validatedCandidateSettings.Clone();
            CaptureValidatedEditorSnapshots(_validatedCandidateSettings);
            _validatedQuoteSeeds = symbolValidation.ValidatedQuotes.Values
                .Select(CloneQuote)
                .ToList();
            AppendValidationLog("VALIDATION PASSED");
            TraceValidation("ValidationPassed", ("auto_named_count", autoNamedCount));
            EndValidationRun();
            completedValidatedState = true;
            UpdateValidatedCloseStatus(autoNamedCount);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Validation cancelled.";
            AppendValidationLog("VALIDATION CANCELLED");
            TraceValidation("ValidationCancelled");
        }
        catch (Exception ex)
        {
            StatusMessage = "Validation stopped unexpectedly. Review the details and try again.";
            AppendValidationLog($"VALIDATION ERROR: {ex.Message}");
            TraceLog.Error("Config.Validation", "ValidateConfigurationAsync", ex);
            _dialogService.Show(
                $"Validation stopped unexpectedly:{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (!completedValidatedState)
                EndValidationRun();
        }
    }

    private void ChooseBackgroundFolder()
    {
        if (!Settings.UseCustomBackgroundImageFolder)
            return;

        using Forms.FolderBrowserDialog dialog = new()
        {
            Description = "Choose a background image folder",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(Settings.CustomBackgroundImageFolder)
                ? Settings.CustomBackgroundImageFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK ||
            string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        Settings.CustomBackgroundImageFolder = dialog.SelectedPath;
        RaisePropertyChanged(nameof(Settings));
        InvalidateValidationState("Configuration changed. Click Validate.");
    }

    private async Task<YahooSymbolValidationResult> ValidateSymbolsAgainstSourcesAsync(
        AppSettings settings,
        IReadOnlyList<string> enabledSymbols,
        CancellationToken cancellationToken = default)
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
                new Progress<YahooSymbolValidationProgress>(ReportSymbolValidationProgress),
                cancellationToken);
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

    private async Task SaveTrustedSymbolProfilesAsync(YahooSymbolValidationResult validation)
    {
        await _symbolProfileSaveGate.WaitAsync();
        try
        {
            await Task.Run(() => SaveTrustedSymbolProfiles(validation));
        }
        finally
        {
            _symbolProfileSaveGate.Release();
        }
    }

    private void OnConnectivityChanged(object? sender, EventArgs e)
    {
        try
        {
            RunConnectivityUpdateInBackground(forceProbe: true);
        }
        catch (Exception ex)
        {
            TraceLog.Error("Config.Connectivity", "Connectivity change notification failed.", ex);
        }
    }

    private AppSettings BuildCandidateSettings()
    {
        AppSettings candidate = AppSettingsNormalizer.Normalize(Settings);
        candidate.Groups = Groups.Select(group => group.ToModel()).ToList();
        return AppSettingsNormalizer.Normalize(candidate);
    }

    private bool ShouldValidateAiNewsAccess(AppSettings candidate)
    {
        if (candidate.NewsScrollerMode != NewsScrollerMode.SummarizedFinancialNews)
            return false;

        if (_loadedSettingsSnapshot.NewsScrollerMode != NewsScrollerMode.SummarizedFinancialNews)
            return true;

        return !AiAccessFieldsEqual(_loadedSettingsSnapshot, candidate);
    }

    private static bool AiAccessFieldsEqual(AppSettings left, AppSettings right)
        => string.Equals(left.AiApiKey?.Trim(), right.AiApiKey?.Trim(), StringComparison.Ordinal) &&
           string.Equals(NormalizeComparableEndpoint(left.AiEndpointUrl), NormalizeComparableEndpoint(right.AiEndpointUrl), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(left.AiModelId?.Trim(), right.AiModelId?.Trim(), StringComparison.Ordinal);

    private static string NormalizeComparableEndpoint(string? endpoint)
        => (endpoint ?? string.Empty).Trim().TrimEnd('/');

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
        => RunConnectivityUpdateInBackground(forceProbe: true);

    private async Task RetryConnectivityAsync()
    {
        bool connected = await ProbeConnectivityStateAsync(forceProbe: true).ConfigureAwait(false);
        await ApplyRetryConnectivityStateOnUiThreadAsync(connected).ConfigureAwait(false);
    }

    private void UpdateConnectivityState()
        => ApplyConnectivityStateOnUiThread(_connectivityService.IsInternetAvailable());

    private async Task UpdateConnectivityStateAsync(CancellationToken cancellationToken = default)
        => await ApplyConnectivityStateOnUiThreadAsync(
            await _connectivityService.IsInternetAvailableAsync(cancellationToken).ConfigureAwait(false));

    private async Task<bool> ProbeConnectivityStateAsync(bool forceProbe, CancellationToken cancellationToken = default)
    {
        if (forceProbe)
            _connectivityService.ForceProbe();

        return await _connectivityService.IsInternetAvailableAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RunConnectivityUpdateInBackground(bool forceProbe = false)
        => _ = RunSerializedConnectivityUpdateInBackgroundAsync(forceProbe);

    private async Task RunSerializedConnectivityUpdateInBackgroundAsync(bool forceProbe)
    {
        lock (_connectivityRefreshGate)
        {
            if (_isDisposed)
                return;

            if (_connectivityRefreshRunning)
            {
                _connectivityRefreshQueued |= forceProbe;
                return;
            }

            _connectivityRefreshRunning = true;
            _connectivityRefreshQueued = false;
        }

        bool nextForceProbe = forceProbe;
        while (true)
        {
            await RunConnectivityUpdateInBackgroundAsync(nextForceProbe).ConfigureAwait(false);

            lock (_connectivityRefreshGate)
            {
                if (_isDisposed || !_connectivityRefreshQueued)
                {
                    _connectivityRefreshRunning = false;
                    _connectivityRefreshQueued = false;
                    return;
                }

                nextForceProbe = _connectivityRefreshQueued;
                _connectivityRefreshQueued = false;
            }
        }
    }

    private async Task RunConnectivityUpdateInBackgroundAsync(bool forceProbe)
    {
        try
        {
            if (forceProbe)
                await RetryConnectivityAsync().ConfigureAwait(false);
            else
                await UpdateConnectivityStateAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            TraceLog.Error("Config.Connectivity", "Background connectivity update failed.", ex);
        }
    }

    private void ApplyConnectivityState(bool connected)
    {
        bool wasNetworkAvailable = _isNetworkAvailable;
        IsNetworkAvailable = connected;
        TraceValidation("ConnectivityStateUpdated",
            ("connected", connected),
            ("was_connected", wasNetworkAvailable),
            ("is_applying", _isApplying),
            ("is_validated", _isValidated));
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

    private void ApplyConnectivityStateOnUiThread(bool connected)
    {
        if (_uiDispatcher.CheckAccess())
        {
            ApplyConnectivityState(connected);
            return;
        }

        _uiDispatcher.Invoke(() => ApplyConnectivityState(connected), DispatcherPriority.Send);
    }

    private async Task ApplyConnectivityStateOnUiThreadAsync(bool connected)
    {
        if (_uiDispatcher.CheckAccess())
        {
            ApplyConnectivityState(connected);
            return;
        }

        await _uiDispatcher.InvokeAsync(
            () => ApplyConnectivityState(connected),
            DispatcherPriority.Send);
    }

    private async Task ApplyRetryConnectivityStateOnUiThreadAsync(bool connected)
    {
        if (_uiDispatcher.CheckAccess())
        {
            ApplyRetryConnectivityState(connected);
            return;
        }

        await _uiDispatcher.InvokeAsync(
            () => ApplyRetryConnectivityState(connected),
            DispatcherPriority.Send);
    }

    private void ApplyRetryConnectivityState(bool connected)
    {
        bool wasValidated = _isValidated;
        ApplyConnectivityState(connected);
        if (wasValidated && !connected)
            return;

        if (_isValidated)
            return;

        StatusMessage = IsNetworkAvailable
            ? "Internet connection detected. Continue with Validate."
            : "Internet connection not detected yet.";
    }

    private async Task<bool> EnsureValidationConnectivityAsync()
    {
        // CR-214 deliberately removes periodic connectivity polling. OS network-change
        // events keep the passive UI fresh, while every Validate click forces a live
        // internet probe so stale adapter state cannot let validation proceed.
        bool connected = await ProbeConnectivityStateAsync(forceProbe: true);
        await ApplyConnectivityStateOnUiThreadAsync(connected);
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
        RaisePropertyChanged(nameof(ShowValidateButton));
        RaisePropertyChanged(nameof(ShowValidatedActionButtons));
        RaiseCommandCanExecuteChanged();
        ValidationActivityChanged?.Invoke(applying);
    }

    private void RaiseCommandCanExecuteChanged()
    {
        PrimaryCommand.RaiseCanExecuteChanged();
        OkCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        AddGroupCommand.RaiseCanExecuteChanged();
    }

    private void InvalidateValidationState(string statusMessage)
    {
        CancelValidatedCloseSequence();
        TraceValidation(
            "ValidationStateInvalidated",
            ("status", statusMessage),
            ("was_validated", IsValidated),
            ("is_applying", _isApplying));
        IsValidated = false;
        _allowClose = false;
        _validatedFingerprint = string.Empty;
        _validatedGroupSnapshots.Clear();
        _validatedTickerSnapshots.Clear();
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
        _trackedTickersByGroup[group] = [];
        foreach (TickerItemEditorViewModel ticker in group.Tickers)
            HookTicker(group, ticker);
    }

    private void UnhookGroup(TickerGroupEditorViewModel group)
    {
        if (!_trackedGroups.Remove(group))
            return;

        group.PropertyChanged -= OnEditorChanged;
        group.Tickers.CollectionChanged -= OnGroupTickersChanged;
        if (_trackedTickersByGroup.Remove(group, out HashSet<TickerItemEditorViewModel>? tickers))
        {
            foreach (TickerItemEditorViewModel ticker in tickers.ToArray())
                UnhookTicker(ticker);
        }
        else
        {
            foreach (TickerItemEditorViewModel ticker in group.Tickers)
                UnhookTicker(ticker);
        }
    }

    private void HookTicker(TickerGroupEditorViewModel group, TickerItemEditorViewModel ticker)
    {
        if (!_trackedTickers.Add(ticker))
            return;

        if (!_trackedTickersByGroup.TryGetValue(group, out HashSet<TickerItemEditorViewModel>? tickers))
        {
            tickers = [];
            _trackedTickersByGroup[group] = tickers;
        }

        tickers.Add(ticker);
        ticker.PropertyChanged += OnEditorChanged;
    }

    private void UnhookTickerFromGroup(TickerGroupEditorViewModel group, TickerItemEditorViewModel ticker)
    {
        if (_trackedTickersByGroup.TryGetValue(group, out HashSet<TickerItemEditorViewModel>? tickers))
            tickers.Remove(ticker);

        UnhookTicker(ticker);
    }

    private void UnhookTicker(TickerItemEditorViewModel ticker)
    {
        if (!_trackedTickers.Remove(ticker))
            return;

        foreach (HashSet<TickerItemEditorViewModel> tickers in _trackedTickersByGroup.Values)
            tickers.Remove(ticker);

        ticker.PropertyChanged -= OnEditorChanged;
    }

    private TickerGroupEditorViewModel? FindGroupForTickerCollection(object? collection)
    {
        foreach (TickerGroupEditorViewModel group in Groups)
        {
            if (ReferenceEquals(group.Tickers, collection))
                return group;
        }

        return null;
    }

    private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (TickerGroupEditorViewModel group in _trackedGroups.ToArray())
                UnhookGroup(group);

            _trackedTickers.Clear();
            _trackedTickersByGroup.Clear();
            foreach (TickerGroupEditorViewModel group in Groups)
                HookGroup(group);
        }
        else if (e.Action == NotifyCollectionChangedAction.Move)
        {
            // Move still invalidates below; subscriptions remain attached to the same group instances.
        }
        else
        {
            if (e.OldItems is not null)
            {
                foreach (TickerGroupEditorViewModel group in e.OldItems.OfType<TickerGroupEditorViewModel>())
                    UnhookGroup(group);
            }

            if (e.NewItems is not null)
            {
                foreach (TickerGroupEditorViewModel group in e.NewItems.OfType<TickerGroupEditorViewModel>())
                    HookGroup(group);
            }
        }

        InvalidateValidationState("Configuration changed. Click Validate.");
    }

    private void OnGroupTickersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            TickerGroupEditorViewModel? group = FindGroupForTickerCollection(sender);
            if (group is not null)
            {
                if (_trackedTickersByGroup.TryGetValue(group, out HashSet<TickerItemEditorViewModel>? oldTickers))
                {
                    foreach (TickerItemEditorViewModel ticker in oldTickers.ToArray())
                        UnhookTicker(ticker);
                }

                foreach (TickerItemEditorViewModel ticker in group.Tickers)
                    HookTicker(group, ticker);
            }
        }
        else
        {
            TickerGroupEditorViewModel? group = FindGroupForTickerCollection(sender);
            if (e.OldItems is not null)
            {
                foreach (TickerItemEditorViewModel ticker in e.OldItems.OfType<TickerItemEditorViewModel>())
                    if (group is not null)
                        UnhookTickerFromGroup(group, ticker);
                    else
                        UnhookTicker(ticker);
            }

            if (e.NewItems is not null)
            {
                foreach (TickerItemEditorViewModel ticker in e.NewItems.OfType<TickerItemEditorViewModel>())
                {
                    if (group is not null)
                        HookTicker(group, ticker);
                }
            }
        }

        InvalidateValidationState("Configuration changed. Click Validate.");
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplying)
        {
            TraceValidation(
                "EditorChangeIgnored",
                ("reason", "applying"),
                ("property", e.PropertyName),
                ("sender_type", sender?.GetType().Name ?? "<null>"));
            return;
        }

        if (IsValidationStatusProperty(e.PropertyName) ||
            !IsPersistedEditorProperty(sender, e.PropertyName))
        {
            TraceValidation(
                "EditorChangeIgnored",
                ("reason", "non-persisted-property"),
                ("property", e.PropertyName),
                ("sender_type", sender?.GetType().Name ?? "<null>"));
            return;
        }

        if (IsValidated && IsCurrentEditorPropertyEqualToValidatedSnapshot(sender, e.PropertyName))
        {
            TraceValidation(
                "EditorChangeIgnored",
                ("reason", "validated-property-match"),
                ("property", e.PropertyName),
                ("sender_type", sender?.GetType().Name ?? "<null>"));
            return;
        }

        TraceValidation(
            "EditorChangeInvalidatesValidation",
            ("property", e.PropertyName),
            ("sender_type", sender?.GetType().Name ?? "<null>"),
            ("is_validated", IsValidated));

        // Keep typing cheap: compare only the changed field; collection handlers invalidate ordering changes.
        InvalidateValidationState("Configuration changed. Click Validate.");
    }

    private bool IsCurrentEditorPropertyEqualToValidatedSnapshot(object? sender, string? propertyName)
    {
        if (sender is TickerGroupEditorViewModel group)
            return _validatedGroupSnapshots.TryGetValue(group, out TickerGroup? validated) &&
                IsGroupPropertyEqual(group, validated, propertyName);

        if (sender is TickerItemEditorViewModel ticker)
            return _validatedTickerSnapshots.TryGetValue(ticker, out TickerItem? validated) &&
                IsTickerPropertyEqual(ticker, validated, propertyName);

        return false;
    }

    private void CaptureValidatedEditorSnapshots(AppSettings validatedSettings)
    {
        _validatedGroupSnapshots.Clear();
        _validatedTickerSnapshots.Clear();

        for (int groupIndex = 0; groupIndex < Groups.Count && groupIndex < validatedSettings.Groups.Count; groupIndex++)
        {
            TickerGroupEditorViewModel groupEditor = Groups[groupIndex];
            TickerGroup groupSnapshot = validatedSettings.Groups[groupIndex];
            _validatedGroupSnapshots[groupEditor] = groupSnapshot;

            for (int tickerIndex = 0; tickerIndex < groupEditor.Tickers.Count && tickerIndex < groupSnapshot.Tickers.Count; tickerIndex++)
                _validatedTickerSnapshots[groupEditor.Tickers[tickerIndex]] = groupSnapshot.Tickers[tickerIndex];
        }
    }

    private static bool IsGroupPropertyEqual(TickerGroupEditorViewModel current, TickerGroup validated, string? propertyName)
        => propertyName switch
        {
            nameof(TickerGroupEditorViewModel.Name) => string.Equals(NormalizeString(current.Name), NormalizeString(validated.Name), StringComparison.Ordinal),
            nameof(TickerGroupEditorViewModel.Enabled) => current.Enabled == validated.Enabled,
            nameof(TickerGroupEditorViewModel.SpeedValue) => current.SpeedValue.Equals(validated.Speed),
            nameof(TickerGroupEditorViewModel.RenderMode) => current.RenderMode == validated.RenderMode,
            nameof(TickerGroupEditorViewModel.Direction) => current.Direction == validated.Direction,
            nameof(TickerGroupEditorViewModel.RowHeight) => current.RowHeight.Equals(validated.RowHeight),
            _ => false
        };

    private static bool IsTickerPropertyEqual(TickerItemEditorViewModel current, TickerItem validated, string? propertyName)
        => propertyName switch
        {
            nameof(TickerItemEditorViewModel.Symbol) => string.Equals(NormalizeString(current.Symbol), NormalizeString(validated.Symbol), StringComparison.OrdinalIgnoreCase),
            nameof(TickerItemEditorViewModel.DisplayName) => string.Equals(NormalizeString(current.DisplayName), NormalizeString(validated.DisplayName), StringComparison.Ordinal),
            nameof(TickerItemEditorViewModel.Quantity) => current.Quantity == validated.Quantity,
            nameof(TickerItemEditorViewModel.CostBasis) => current.CostBasis == validated.CostBasis,
            nameof(TickerItemEditorViewModel.Currency) => string.Equals(NormalizeString(current.Currency), NormalizeString(validated.Currency), StringComparison.OrdinalIgnoreCase),
            nameof(TickerItemEditorViewModel.Enabled) => current.Enabled == validated.Enabled,
            _ => false
        };

    private static string NormalizeString(string? value)
        => (value ?? string.Empty).Trim();

    private bool IsPersistedEditorProperty(object? sender, string? propertyName)
        => sender switch
        {
            TickerItemEditorViewModel => propertyName is
                nameof(TickerItemEditorViewModel.Symbol) or
                nameof(TickerItemEditorViewModel.DisplayName) or
                nameof(TickerItemEditorViewModel.Quantity) or
                nameof(TickerItemEditorViewModel.CostBasis) or
                nameof(TickerItemEditorViewModel.Currency) or
                nameof(TickerItemEditorViewModel.Enabled),
            TickerGroupEditorViewModel => propertyName is
                nameof(TickerGroupEditorViewModel.Name) or
                nameof(TickerGroupEditorViewModel.Enabled) or
                nameof(TickerGroupEditorViewModel.SpeedValue) or
                nameof(TickerGroupEditorViewModel.RenderMode) or
                nameof(TickerGroupEditorViewModel.Direction) or
                nameof(TickerGroupEditorViewModel.RowHeight),
            _ => true
        };

    private static bool IsValidationStatusProperty(string? propertyName)
        => propertyName is
            nameof(TickerItemEditorViewModel.ValidationState) or
            nameof(TickerItemEditorViewModel.ValidationMessage) or
            nameof(TickerItemEditorViewModel.ValidationBadgeText);

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

    private bool CanExecuteValidate()
        => !_isApplying && !IsValidated;

    private bool CanExecuteOk()
        => !_isApplying && IsValidated;

    private void ExecuteOk()
    {
        if (!CanExecuteOk())
            return;

        ApplyValidatedConfiguration();
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
        StatusMessage = $"{PortfolioVersion.Version} saved at {DateTime.Now:T}.";
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
        CancelActiveValidation();
        _validationCancellation?.Dispose();
        _validationCancellation = new CancellationTokenSource();
        ValidationLogText = string.Empty;
        SetApplying(true);
    }

    private void EndValidationRun()
    {
        _validationCancellation?.Dispose();
        _validationCancellation = null;
        SetApplying(false);
    }

    private void CancelActiveValidation()
    {
        try
        {
            _validationCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        lock (_connectivityRefreshGate)
        {
            _isDisposed = true;
            _connectivityRefreshQueued = false;
        }

        _connectivityService.ConnectivityChanged -= OnConnectivityChanged;
        if (_ownsConnectivityService && _connectivityService is IDisposable disposableConnectivityService)
            disposableConnectivityService.Dispose();
        CancelActiveValidation();
        _validationCancellation?.Dispose();
        _validationCancellation = null;
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

