using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using PortfolioSaver.Config.Commands;
using PortfolioSaver.Config.Services;
using PortfolioSaver.Config.Windows;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Core.Validation;
using PortfolioSaver.Shared;
using PortfolioSaver.Shared.Infrastructure;

namespace PortfolioSaver.Config.ViewModels;

public sealed class MainWindowViewModel : BindableBase
{
    private readonly SettingsFileService _settingsFileService;
    private readonly PreviewLauncherService _previewLauncherService;
    private readonly SettingsValidator _settingsValidator;
    private readonly NewsFeedValidationService _newsFeedValidationService;
    private readonly DocumentContentService _documentContentService;
    private readonly ConfigConnectivityService _connectivityService;
    private readonly YahooSymbolValidationService _yahooSymbolValidationService;
    private readonly ApiKeyValidationService _apiKeyValidationService;
    private readonly DispatcherTimer _stateTimer;
    private readonly DispatcherTimer _validatedCloseTimer;
    private readonly HashSet<TickerGroupEditorViewModel> _trackedGroups = [];
    private readonly HashSet<TickerItemEditorViewModel> _trackedTickers = [];
    private readonly HashSet<DataSourcePolicyEditorViewModel> _trackedDataSources = [];

    private AppSettings _settings;
    private string _statusMessage = $"{PortfolioVersion.DisplayName} ready";
    private bool _isApplying;
    private bool _isValidated;
    private bool _isValidationClosePending;
    private bool _isRealtimeValidationRunning;
    private bool _allowClose;
    private bool _isNetworkAvailable;
    private string _validatedFingerprint = string.Empty;
    private string _lastRealtimeSymbolsFingerprint = string.Empty;
    private int _validationCloseCountdownSeconds;
    private AppSettings? _pendingValidatedSettings;

    public MainWindowViewModel()
    {
        _settingsFileService = new SettingsFileService();
        _previewLauncherService = new PreviewLauncherService();
        _settingsValidator = new SettingsValidator();
        _newsFeedValidationService = new NewsFeedValidationService();
        _documentContentService = new DocumentContentService();
        _connectivityService = new ConfigConnectivityService();
        _yahooSymbolValidationService = new YahooSymbolValidationService();
        _apiKeyValidationService = new ApiKeyValidationService();

        _settings = _settingsFileService.Load();
        Groups = new ObservableCollection<TickerGroupEditorViewModel>(
            _settings.Groups.Select(group => new TickerGroupEditorViewModel(group, RemoveGroup)));
        DataSources = new ObservableCollection<DataSourcePolicyEditorViewModel>(
            _settings.DataSources.Select(policy => new DataSourcePolicyEditorViewModel(policy)));

        PrimaryCommand = new RelayCommand(() => _ = ExecutePrimaryAsync(), () => !_isApplying && !_isValidationClosePending);
        RetryNetworkCommand = new RelayCommand(RetryConnectivity);
        PreviewCommand = new RelayCommand(() => _previewLauncherService.LaunchPreview(), () => IsConfigActive);
        AddGroupCommand = new RelayCommand(AddGroup, () => IsConfigActive);
        HelpCommand = new RelayCommand(ShowHelp);
        AboutCommand = new RelayCommand(ShowAbout);
        LicenseCommand = new RelayCommand(ShowLicense);

        _stateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _stateTimer.Tick += async (_, _) => await OnStateTimerTickAsync();

        _validatedCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _validatedCloseTimer.Tick += (_, _) => OnValidatedCloseTimerTick();

        if (Groups.Count == 0)
            AddGroup();

        HookEditors();
        UpdateConnectivityState();
        ResetAllSymbolValidationStates("Pending validation");

        _stateTimer.Start();
    }

    public event Action? CloseRequested;

    public AppSettings Settings
    {
        get => _settings;
        set => SetProperty(ref _settings, value);
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

    public bool IsConfigActive => IsNetworkAvailable && !_isApplying && !_isValidationClosePending;
    public bool ShowNetworkLockOverlay => !IsNetworkAvailable;

    public bool IsValidated
    {
        get => _isValidated;
        private set
        {
            if (!SetProperty(ref _isValidated, value))
                return;

            RaisePropertyChanged(nameof(PrimaryButtonText));
        }
    }

    public string PrimaryButtonText => "Validate";
    public string VersionLabel => $"{PortfolioVersion.BaselineLabel} ({PortfolioVersion.SemanticVersion})";

    public ObservableCollection<TickerGroupEditorViewModel> Groups { get; }
    public ObservableCollection<DataSourcePolicyEditorViewModel> DataSources { get; }

    public RelayCommand PrimaryCommand { get; }
    public RelayCommand RetryNetworkCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand AddGroupCommand { get; }
    public RelayCommand HelpCommand { get; }
    public RelayCommand AboutCommand { get; }
    public RelayCommand LicenseCommand { get; }

    public bool CanCloseWindow()
    {
        if (_allowClose)
            return true;

        MessageBox.Show(
            "Validate the configuration first. After a successful validation, the app saves and closes automatically.",
            "Validation Required",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
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

        if (!IsNetworkAvailable)
        {
            StatusMessage = "Internet connection is required before validation can run.";
            MessageBox.Show(
                "Internet connection is required to validate tickers and API keys.",
                "Internet Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        await ValidateConfigurationAsync();
    }

    private async Task ValidateConfigurationAsync()
    {
        SetApplying(true);
        try
        {
            AppSettings candidate = BuildCandidateSettings();
            ApplyNormalizedAdvancedSettings(candidate);
            Settings = candidate;

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
            }
            else
            {
                candidate.NewsFeedUrl = feedValidation.ResolvedFeedUrl;
            }

            IReadOnlyList<string> configErrors = _settingsValidator.Validate(candidate);
            if (configErrors.Count > 0)
            {
                StatusMessage = configErrors[0];
                MessageBox.Show(
                    string.Join(Environment.NewLine, configErrors),
                    "Settings Need Attention",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            List<string> enabledSymbols = GetEnabledSymbols(candidate).ToList();
            YahooSymbolValidationResult symbolValidation = await ValidateSymbolsAgainstYahooAsync(candidate, enabledSymbols);
            if (symbolValidation.InvalidSymbols.Count > 0)
            {
                int disabledCount = DisableInvalidSymbols(symbolValidation.InvalidSymbols);
                StatusMessage = "Ticker validation failed. Correct invalid symbols and validate again.";
                MessageBox.Show(
                    "These symbols are invalid on Yahoo Finance:" + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, symbolValidation.InvalidSymbols.Select(symbol => $"- {symbol}")) +
                    Environment.NewLine + Environment.NewLine +
                    $"Disabled entries: {disabledCount}.",
                    "Invalid Ticker Symbols",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            int autoNamedCount = ApplyResolvedDisplayNames(symbolValidation);

            ApiKeyValidationResult apiKeyValidation = await _apiKeyValidationService.ValidateAsync(candidate);
            if (!apiKeyValidation.IsValid)
            {
                StatusMessage = "API key validation failed. Update keys and validate again.";
                MessageBox.Show(
                    string.Join(Environment.NewLine, apiKeyValidation.Errors),
                    "API Key Validation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Settings = candidate;
            IsValidated = true;
            _validatedFingerprint = BuildFingerprint(candidate);
            _allowClose = false;
            BeginValidatedCloseSequence(candidate, autoNamedCount);
        }
        finally
        {
            SetApplying(false);
        }
    }

    private async Task<YahooSymbolValidationResult> ValidateSymbolsAgainstYahooAsync(AppSettings settings, IReadOnlyList<string> enabledSymbols)
    {
        MarkSymbolStates(enabledSymbols, SymbolValidationState.Checking, "Checking Yahoo Finance...");

        YahooSymbolValidationResult result = await _yahooSymbolValidationService.ValidateAsync(
            enabledSymbols,
            settings.HttpTimeoutSeconds);

        foreach (string symbol in enabledSymbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string normalized = SymbolProfileHeuristics.Normalize(symbol);
            if (result.Entries.TryGetValue(normalized, out YahooSymbolValidationEntry? entry) && entry.IsValid)
            {
                MarkSymbolState(normalized, SymbolValidationState.Valid, "Validated via Yahoo Finance");
                continue;
            }

            MarkSymbolState(normalized, SymbolValidationState.Invalid, "Yahoo Finance does not recognize this symbol");
        }

        MarkDisabledSymbolsAsUnknown(enabledSymbols);
        return result;
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
            ticker.ValidationMessage = "Disabled because Yahoo Finance validation failed";
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

    private async Task OnStateTimerTickAsync()
    {
        UpdateConnectivityState();
        if (!IsConfigActive || _isRealtimeValidationRunning)
            return;

        if (IsValidated)
        {
            AppSettings current = BuildCandidateSettings();
            string currentFingerprint = BuildFingerprint(current);
            if (!string.Equals(currentFingerprint, _validatedFingerprint, StringComparison.Ordinal))
                InvalidateValidationState("Configuration changed. Click Validate.");
        }

        List<string> symbols = GetEnabledSymbols(BuildCandidateSettings()).ToList();
        string fingerprint = string.Join("|", symbols.Select(SymbolProfileHeuristics.Normalize).OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase));
        if (string.Equals(fingerprint, _lastRealtimeSymbolsFingerprint, StringComparison.Ordinal))
            return;

        _lastRealtimeSymbolsFingerprint = fingerprint;
        _isRealtimeValidationRunning = true;
        try
        {
            await ValidateSymbolsAgainstYahooAsync(BuildCandidateSettings(), symbols);
        }
        catch
        {
            MarkSymbolStates(symbols, SymbolValidationState.Unknown, "Validation unavailable");
        }
        finally
        {
            _isRealtimeValidationRunning = false;
        }
    }

    private AppSettings BuildCandidateSettings()
    {
        AppSettings candidate = AppSettingsNormalizer.Normalize(Settings);
        candidate.Groups = Groups.Select(group => group.ToModel()).ToList();
        candidate.DataSources = DataSources
            .Select(policy => policy.ToModel())
            .ToList();
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
        bool connected = _connectivityService.IsInternetAvailable();
        IsNetworkAvailable = connected;
        if (!connected)
        {
            InvalidateValidationState("Internet connection is required for ticker and key validation.");
            ResetAllSymbolValidationStates("Internet required");
        }
    }

    private void SetApplying(bool applying)
    {
        _isApplying = applying;
        RaisePropertyChanged(nameof(IsConfigActive));
        RaisePropertyChanged(nameof(ShowNetworkLockOverlay));
        RaiseCommandCanExecuteChanged();
    }

    private void RaiseCommandCanExecuteChanged()
    {
        PrimaryCommand.RaiseCanExecuteChanged();
        PreviewCommand.RaiseCanExecuteChanged();
        AddGroupCommand.RaiseCanExecuteChanged();
    }

    private void InvalidateValidationState(string statusMessage)
    {
        CancelValidatedCloseSequence();
        IsValidated = false;
        _allowClose = false;
        _validatedFingerprint = string.Empty;
        if (!string.IsNullOrWhiteSpace(statusMessage))
            StatusMessage = statusMessage;
    }

    private void ShowHelp()
    {
        ShowDocument($"{PortfolioVersion.DisplayName} Help", _documentContentService.GetHelpText());
    }

    private void ShowAbout()
    {
        ShowDocument($"About {PortfolioVersion.DisplayName}", _documentContentService.GetAboutText());
    }

    private void ShowLicense()
    {
        ShowDocument(
            $"{AppIdentity.LicenseName} - {AppIdentity.ApplicationName}",
            _documentContentService.GetLicenseText(),
            AppIdentity.OfficialLicenseUrl,
            "Open Official MIT License");
    }

    private void ShowDocument(string title, string body, string? linkUrl = null, string? linkButtonText = null)
    {
        DocumentWindow window = new(title, body, linkUrl, linkButtonText);
        Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(candidate => candidate.IsActive);
        if (owner is not null)
            window.Owner = owner;

        window.ShowDialog();
    }

    private void HookEditors()
    {
        foreach (TickerGroupEditorViewModel group in Groups)
            HookGroup(group);

        foreach (DataSourcePolicyEditorViewModel dataSource in DataSources)
            HookDataSource(dataSource);
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

    private void HookDataSource(DataSourcePolicyEditorViewModel dataSource)
    {
        if (!_trackedDataSources.Add(dataSource))
            return;

        dataSource.PropertyChanged += OnEditorChanged;
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
        if (string.Equals(e.PropertyName, nameof(TickerItemEditorViewModel.ValidationState), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(TickerItemEditorViewModel.ValidationMessage), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(TickerItemEditorViewModel.ValidationBadgeText), StringComparison.Ordinal))
        {
            return;
        }

        InvalidateValidationState("Configuration changed. Click Validate.");
    }

    private void BeginValidatedCloseSequence(AppSettings candidate, int autoNamedCount)
    {
        CancelValidatedCloseSequence();
        _pendingValidatedSettings = AppSettingsNormalizer.Normalize(candidate);
        _validationCloseCountdownSeconds = 5;
        _isValidationClosePending = true;
        RaisePropertyChanged(nameof(IsConfigActive));
        RaisePropertyChanged(nameof(ShowNetworkLockOverlay));
        RaiseCommandCanExecuteChanged();
        UpdateValidatedCloseStatus(autoNamedCount);
        _validatedCloseTimer.Start();
    }

    private void CancelValidatedCloseSequence()
    {
        _validatedCloseTimer.Stop();
        _pendingValidatedSettings = null;
        _validationCloseCountdownSeconds = 0;
        if (!_isValidationClosePending)
            return;

        _isValidationClosePending = false;
        RaisePropertyChanged(nameof(IsConfigActive));
        RaisePropertyChanged(nameof(ShowNetworkLockOverlay));
        RaiseCommandCanExecuteChanged();
    }

    private void OnValidatedCloseTimerTick()
    {
        if (_pendingValidatedSettings is null)
        {
            CancelValidatedCloseSequence();
            return;
        }

        _validationCloseCountdownSeconds--;
        if (_validationCloseCountdownSeconds > 0)
        {
            UpdateValidatedCloseStatus(
                Groups.SelectMany(group => group.Tickers).Count(ticker => !string.IsNullOrWhiteSpace(ticker.DisplayName)));
            return;
        }

        _validatedCloseTimer.Stop();
        _settingsFileService.Save(_pendingValidatedSettings);
        _allowClose = true;
        _isValidationClosePending = false;
        _pendingValidatedSettings = null;
        RaisePropertyChanged(nameof(IsConfigActive));
        RaisePropertyChanged(nameof(ShowNetworkLockOverlay));
        RaiseCommandCanExecuteChanged();
        StatusMessage = $"{PortfolioVersion.BaselineLabel} saved at {DateTime.Now:T}.";
        CloseRequested?.Invoke();
    }

    private void UpdateValidatedCloseStatus(int autoNamedCount)
    {
        string namingText = autoNamedCount > 0
            ? $"Filled {autoNamedCount} symbol name(s). "
            : string.Empty;
        StatusMessage = $"{namingText}Validation passed. Saving and closing in {_validationCloseCountdownSeconds}s.";
    }

    private void ApplyNormalizedAdvancedSettings(AppSettings candidate)
    {
        IReadOnlyList<DataSourcePolicySettings> normalizedPolicies = DataSourceCatalog.NormalizePolicies(candidate.DataSources);
        candidate.DataSources = [.. normalizedPolicies];

        Dictionary<DataSourceKind, DataSourcePolicySettings> policiesByKind = normalizedPolicies
            .ToDictionary(policy => policy.Kind, policy => policy);
        foreach (DataSourcePolicyEditorViewModel editor in DataSources)
        {
            if (policiesByKind.TryGetValue(editor.Kind, out DataSourcePolicySettings? policy))
                editor.ApplyModel(policy);
        }
    }
}
