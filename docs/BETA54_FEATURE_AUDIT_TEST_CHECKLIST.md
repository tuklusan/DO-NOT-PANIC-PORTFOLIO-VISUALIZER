# BETA-5.4 Feature Audit + Automated Test Checklist

Date: 2026-04-10  
Scope: line-by-line carry-forward audit against latest code on main (`BETA-5.4` development lane, following final baseline tag `BETA-5.3.2-final`).

Note: historical sections below preserve older baseline/version evidence where that history is useful.

Automated suite run:

```powershell
dotnet test .\PortfolioScreensaver.sln -c Release --nologo
```

Result: **Passed 95 / Failed 0 / Skipped 0**

## Status Legend

- **Implemented**: feature exists in code and appears wired.
- **Partial**: feature exists but has notable gaps/mismatches.
- **Missing**: not found in code.
- **Superseded**: older note replaced by newer direction.

## 1) Feature Verification Matrix (Scratch Notes -> Code)

| ID | Scratch-note feature | Status | Evidence (file:line) | Automated test IDs |
|---|---|---|---|---|
| F-001 | Config screen ships placeholder key formats (user must replace) | Implemented | `src/PortfolioSaver.Config/Services/SettingsFileService.cs:38-45`, `src/PortfolioSaver.Config/Services/ApiKeyValidationService.cs:12-17,39-41` | T-001, T-002 |
| F-002 | Add Tiingo/FMP/EODHD API key fields in config UI | Implemented | `src/PortfolioSaver.Config/Windows/MainWindow.xaml:104-109` | T-003 |
| F-003 | Validate button changes to OK only after full validation | Implemented | `src/PortfolioSaver.Config/ViewModels/MainWindowViewModel.cs:127,196-230,298-303` | T-004 |
| F-004 | Enforce internet connection for configuration screen activity | Implemented | `src/PortfolioSaver.Config/ViewModels/MainWindowViewModel.cs:112-114,201-210,576-585`, `src/PortfolioSaver.Config/Windows/MainWindow.xaml:65,481-506` | T-005 |
| F-005 | Real-time Yahoo validation for entered symbols | Implemented | `src/PortfolioSaver.Config/ViewModels/MainWindowViewModel.cs:376-409`, `src/PortfolioSaver.Config/Services/YahooSymbolValidationService.cs:33` | T-006 |
| F-006 | Apply should fail when invalid ticker symbols are present | Implemented | `src/PortfolioSaver.Config/ViewModels/MainWindowViewModel.cs:269-281` | T-007 |
| F-007 | Auto-fill ticker display names after successful validation | Implemented | `src/PortfolioSaver.Config/ViewModels/MainWindowViewModel.cs:283-374`, `src/PortfolioSaver.Config/Windows/MainWindow.xaml:234,314` | T-008 |
| F-008 | Invalid ticker auto-uncheck | Implemented | invalids are auto-disabled in `src/PortfolioSaver.Config/ViewModels/MainWindowViewModel.cs:275,340-379`; stale help text note in `src/PortfolioSaver.Config/Content/help.txt:7` | T-009 |
| F-009 | Max tapes configurable = 4 | Implemented | `src/PortfolioSaver.Core/Constants/Defaults.cs:15`, `src/PortfolioSaver.Config/ViewModels/MainWindowViewModel.cs:157-166` | T-010 |
| F-010 | Max tickers per tape = 8 | Implemented | `src/PortfolioSaver.Core/Constants/Defaults.cs:16`, `src/PortfolioSaver.Config/ViewModels/TickerGroupEditorViewModel.cs:35-37,100-107`, `src/PortfolioSaver.Core/Services/AppSettingsNormalizer.cs:121` | T-011 |
| F-011 | Old note "16 per tape" | Superseded | superseded by F-010 | T-011 |
| F-012 | Help badges ("?" blobs) on config screen | Implemented | `src/PortfolioSaver.Config/Windows/MainWindow.xaml:35-45,81-84,153-154,186-187,228-229,418-419` | T-012 |
| F-013 | Help/About buttons + initial documents | Implemented (content exists) | `src/PortfolioSaver.Config/Windows/MainWindow.xaml:526-527`, `src/PortfolioSaver.Config/ViewModels/MainWindowViewModel.cs:69-70,612-630`, `src/PortfolioSaver.Config/Content/help.txt`, `src/PortfolioSaver.Config/Content/about.txt` | T-013 |
| F-014 | RSS/XML feed validate; reset to Yahoo default on invalid feed | Implemented | `src/PortfolioSaver.Config/Services/NewsFeedValidationService.cs:20-22,55-57,67-68`, `src/PortfolioSaver.Config/ViewModels/MainWindowViewModel.cs:239-255` | T-014 |
| F-015 | Advanced tab "Data Sources" policy table (per hour/day + single/multiple + known limits) | Implemented | `src/PortfolioSaver.Config/Windows/MainWindow.xaml:427-474`, `src/PortfolioSaver.Config/ViewModels/DataSourcePolicyEditorViewModel.cs:29-64`, `src/PortfolioSaver.Core/Constants/DataSourceCatalog.cs:16-47` | T-015 |
| F-016 | Advanced tab hard widget widths fixed | Partial | `src/PortfolioSaver.Config/Windows/MainWindow.xaml:441-473` uses star sizing + min widths, but still hard mins in places | T-016 |
| F-017 | Config responsiveness (general + advanced tabs) | Partial | `src/PortfolioSaver.Config/Windows/MainWindow.xaml` uses scroll viewers, star columns, min sizes (`10-13`, `67-69`, `413-478`) but needs automated resize coverage | T-017 |
| F-018 | "Show indexes on main config" behavior clarified | Implemented-as-removed | `src/PortfolioSaver.Config/Windows/MainWindow.xaml:228` ("there is no separate Show Indexes toggle in this build") | T-018 |
| F-019 | Startup Yahoo warm-fill, batched, 5-second gap | Implemented | `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs:182-227` | T-019 |
| F-020 | Yahoo as primary provider; rotate backups; fallback handling | Implemented | `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs:745-785,320-473` | T-020 |
| F-021 | Per-source minimum reuse 15s | Implemented | `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs:24,397-401`, `src/PortfolioSaver.Screensaver/Services/ProviderBudgetLedgerService.cs:39-43` | T-021 |
| F-022 | Provider hourly/daily budgets enforced | Implemented | `src/PortfolioSaver.Screensaver/Services/ProviderBudgetLedgerService.cs:45-50`, policy bounds in `src/PortfolioSaver.Core/Validation/SettingsValidator.cs:71-85` | T-022 |
| F-023 | Source-specific symbol eligibility filtering | Implemented | `src/PortfolioSaver.Core/Services/DataSourceSymbolEligibility.cs`, usage in `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs:884-895` | T-023 |
| F-024 | Yahoo session cookie + crumb required and handled | Implemented | `src/PortfolioSaver.Data/Services/YahooFinanceSessionService.cs:58-88,111-117` | T-024 |
| F-025 | Yahoo live quotes via `v8/spark` batch + `v8/chart` fallback | Implemented | `src/PortfolioSaver.Data/Providers/YahooFinanceQuoteProvider.cs:89-126,128-196` | T-025 |
| F-026 | Historical: Yahoo `v8/spark` batch + chart fallback | Implemented | `src/PortfolioSaver.Data/Providers/HybridHistoricalDataProvider.cs:212-261,344-400` | T-026 |
| F-027 | Tapes alternate left/right | Implemented | defaults in `src/PortfolioSaver.Core/Constants/Defaults.cs:174`, legacy fix in `src/PortfolioSaver.Core/Services/AppSettingsNormalizer.cs:97-108` | T-027 |
| F-028 | Tape content wraps/repeats to prevent empty gaps | Implemented | `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs:934-950`, `src/PortfolioSaver.Render/Controls/TickerTapeControl.xaml.cs:223-227,204-221` | T-028 |
| F-029 | News headlines repeat to prevent blank space | Implemented | `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs:952-978`, `src/PortfolioSaver.Render/Controls/NewsFlasherControl.xaml.cs:193-197,175-191` | T-029 |
| F-030 | Symbol stays yellow when stale/not available | Implemented | `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs:668` | T-030 |
| F-031 | Tape values flash once when updated (all repeated instances) | Implemented | update trigger `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs:1592-1608`; render flash `src/PortfolioSaver.Render/Controls/TickerTapeControl.xaml.cs:296-320`; state bump `src/PortfolioSaver.Render/ViewModels/TapeItemViewModel.cs:65-69` | T-031 |
| F-032 | Graph card background flashes twice on update | Implemented | trigger `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs:1654-1655`; animation `src/PortfolioSaver.Render/Controls/FloatingGraphControl.xaml.cs:52-58` | T-032 |
| F-033 | "Waiting for network" bouncing overlay | Implemented | overlay + motion: `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml:62-87`, `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs:645-657,680-681,1463-1487` | T-033 |
| F-034 | World clock -> local + 11 exchange cells with weather/time/index + time-of-day card colors | Implemented | builder list: `src/PortfolioSaver.Render/Services/FloatingClockBuilder.cs:8-21`; layout: `src/PortfolioSaver.Render/Controls/FloatingClockControl.xaml:34-118`; market/weather/theme: `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs:810-868,896-921,1007-1019` | T-034 |
| F-035 | NY status text with "(New York)" + countdown open/close | Implemented | `src/PortfolioSaver.Core/Services/NewYorkMarketStatusService.cs:30-55` | T-035 |
| F-036 | Holiday-aware trading calendar with live sync + cache + offline fallback | Implemented | NY snapshot + fallback `src/PortfolioSaver.Core/Services/NyseTradingCalendarSnapshot.cs`; live sync/cache `src/PortfolioSaver.Screensaver/Services/ExchangeMarketCalendarService.cs:62-97,185-288,290-397` | T-036 |
| F-037 | International exchange market status in clock cards | Implemented | `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs:967-987` | T-037 |
| F-038 | Exchange photos from internet + local cache + starter bundled images + custom folder override | Implemented | `src/PortfolioSaver.Media/Services/ExchangePhotoCacheService.cs:13-75,77-98,125-153` | T-038 |
| F-039 | Uninstall removes AppData caches (background/history/derived cache files) | Implemented | `build/installer/Uninstall-PortfolioSaverScreensaver.ps1:47-52,96-133` | T-039 |
| F-040 | Installer elevates before install | Implemented | bootstrapper `src/PortfolioSaver.Installer/Program.cs:20-24,46-60`, manifest `src/PortfolioSaver.Installer/app.manifest:7` | T-040 |
| F-041 | "No network" detection by pinging baidu 5x | Implemented | `src/PortfolioSaver.Shared/Services/InternetProbeService.cs:18-77`, `src/PortfolioSaver.Config/Services/ConfigConnectivityService.cs:5-11`, `src/PortfolioSaver.Screensaver/Services/NetworkAvailabilityService.cs:5-11` | T-041 |
| F-042 | If no update for 15 minutes -> yellow + blank values + card removed | Implemented | stale threshold + blank value handling in `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs:663-699`; graph hide/blank behavior in `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs:1735-1742` | T-042 |
| F-043 | Round macro meters (VIX/VIX3M/yield spread/rate flows) | Implemented | `src/PortfolioSaver.Render/ViewModels/MacroMeterViewModel.cs`, `src/PortfolioSaver.Render/Controls/StatusBarControl.xaml:47`, `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs:736-793` | T-043 |
| F-044 | Visual polish across very small to ultrawide screens | Partial | responsive logic exists (`src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs:515-550`) but no automated multi-resolution regression suite yet | T-044 |
| F-045 | Baseline/version text reflects BETA-3 | Superseded | baseline is now BETA-5; see `src/PortfolioSaver.Shared/PortfolioVersion.cs:6-7`, `src/PortfolioSaver.Config/Windows/MainWindow.xaml:9`, `src/PortfolioSaver.Config/Content/about.txt:3` | T-045 |

## 2) Automated Test Checklist (Pass/Fail Tracking)

### Existing automated tests (already in repo)

| Test ID | Scope | Covers feature IDs | Current result |
|---|---|---|---|
| T-001 | Unit (`AppSettingsNormalizerTests`) | F-010, F-027 | PASS |
| T-002 | Unit (`SettingsValidatorTests`) | F-001, F-015, F-022 | PASS |
| T-003 | Unit (`DataSourceSymbolEligibilityTests`) | F-023 | PASS |
| T-004 | Unit (`NewYorkMarketStatusServiceTests`) | F-035, F-036 | PASS |
| T-005 | Unit (`SymbolProfileHeuristicsTests`) | F-023 | PASS |
| T-006 | Unit (`FinnhubQuoteProviderTests`) | provider key enforcement subset | PASS |
| T-007 | Unit (`QuoteSchedulerTests`) | scheduler baseline plumbing | PASS |
| T-008 | Unit (`MarketSessionResolverTests`) | session resolver baseline | PASS |

### New automated tests to add (for full scratch-note coverage)

| Test ID | Scope | Covers feature IDs | Proposed check | Status |
|---|---|---|---|---|
| T-009 | Unit (Config VM) | F-008 | `MainWindowViewModelValidationTests.DisableInvalidSymbols_AutoDisablesMatchingTickersAndBenchmarks`; filter: `FullyQualifiedName~MainWindowViewModelValidationTests` | PASS |
| T-010 | Unit (Config VM) | F-003 | `MainWindowViewModelValidationTests.OnEditorChanged_*` verifies validated->invalidated transition and validation-property no-op behavior | PASS |
| T-011 | Unit (Config VM + group editor) | F-009, F-010 | `TickerGroupEditorViewModelTests` + `AppSettingsNormalizerTests.Normalize_ClampsTapeCountToFour` enforce 8 tickers/tape and 4-tape max | PASS |
| T-012 | UI automation (WinAppDriver) | F-012 | Verify all main sections display help badges and tooltips | TODO |
| T-013 | UI automation | F-013 | Help/About buttons open document dialog with non-empty content | TODO |
| T-014 | Unit (NewsFeedValidationService) | F-014 | `NewsFeedValidationServiceTests` validates invalid/non-http/unreachable feeds reset to default, and no-network path skips without reset; filter: `FullyQualifiedName~NewsFeedValidationServiceTests` | PASS |
| T-015 | Unit (DataSourcePolicy editor + validator) | F-015 | `DataSourcePolicyValidationTests` validates editor clamping and validator rejection of out-of-bounds + unsupported batch mode | PASS |
| T-016 | UI automation (resize) | F-016, F-017 | Resize config window across breakpoints and assert advanced grid columns remain visible/usable | TODO |
| T-017 | UI automation (resize) | F-017 | General tab controls remain usable without clipped primary actions | TODO |
| T-018 | Unit/UI text check | F-018 | `ConfigTextConsistencyTests.MainWindowXaml_HasBeta5Title_AndNoShowIndexesToggleNote` asserts no-toggle explanatory text in config XAML | PASS |
| T-019 | Integration (mock Yahoo provider) | F-019 | `StartupCoordinatorAdvancedTests.WarmStartupYahooQuotesAsync_ChunksSymbolsAndUsesFiveSecondInterBatchDelay`; filter: `FullyQualifiedName~StartupCoordinatorAdvancedTests` | PASS |
| T-020 | Integration (mock providers) | F-020 | `StartupCoordinatorAdvancedTests.BuildQuoteProviders_PrioritizesYahooThenRotatesBackupProviders` + `LoadQuotesAsync_WhenYahooFails_FallsBackToBackupProvider`; filter: `FullyQualifiedName~StartupCoordinatorAdvancedTests` | PASS |
| T-021 | Unit (ProviderBudgetLedgerService) | F-021 | `ProviderBudgetLedgerServiceTests.TryReserve_EnforcesMinimumReuseIntervalPerProvider`; filter: `FullyQualifiedName~ProviderBudgetLedgerServiceTests` | PASS |
| T-022 | Unit (ProviderBudgetLedgerService) | F-022 | `ProviderBudgetLedgerServiceTests.TryReserve_EnforcesHourlyAndDailyBudgets` + `NoteRateLimit_BlocksDuringCooldown_ThenAllows`; filter: `FullyQualifiedName~ProviderBudgetLedgerServiceTests` | PASS |
| T-023 | Unit | F-023 | Covered by `DataSourceSymbolEligibilityTests` (mixed symbols + profile-supported source constraints) | PASS |
| T-024 | Integration (mock HTTP handler) | F-024 | `YahooFinanceSessionServiceTests.GetAsync_InvalidCrumbResponse_RefreshesSessionAndRetries` validates cookie+crumb refresh/retry flow | PASS |
| T-025 | Integration (mock HTTP handler) | F-025 | `YahooFinanceQuoteProviderTests.GetQuotesAsync_UsesSparkBatchAndFallsBackToChartForUnresolvedSymbols` | PASS |
| T-026 | Integration (mock HTTP handler) | F-026 | `HybridHistoricalDataProviderTests.GetHistoryAsync_UsesYahooSparkBatch_ThenChartFallbackForUnresolvedSymbols` | PASS |
| T-027 | Render integration | F-027, F-028 | `StartupCoordinatorAdvancedTests.DefaultAndNormalizedTapeDirections_AlternateLeftRight` + `BuildTapesForQuotes_RepeatsItemsToAvoidEmptyGaps`; filter: `FullyQualifiedName~StartupCoordinatorAdvancedTests` | PASS |
| T-028 | Render integration | F-028 | `StartupCoordinatorAdvancedTests.TickerTapeContentSignature_IgnoresValueOnlyUpdates`; filter: `FullyQualifiedName~StartupCoordinatorAdvancedTests` | PASS |
| T-029 | Render integration | F-029 | `StartupCoordinatorNewsTests` verifies repeated headline fill and non-empty fallback text for no-news scenarios | PASS |
| T-030 | Unit (StartupCoordinator) | F-030 | `StartupCoordinatorTapeItemTests` verifies null/stale quote path yields gold (yellow) symbol and blank value texts | PASS |
| T-031 | Render integration | F-031 | `ScreensaverRenderBehaviorTests.UpdateTapeItem_TriggersSingleFlashWhenLiveValueChanges`; filter: `FullyQualifiedName~ScreensaverRenderBehaviorTests` | PASS |
| T-032 | Render integration | F-032 | `ScreensaverRenderBehaviorTests.FloatingGraphControl_FlashAnimationUsesTwoColorPulses`; filter: `FullyQualifiedName~ScreensaverRenderBehaviorTests` | PASS |
| T-033 | UI automation | F-033 | `ScreensaverRenderBehaviorTests.NetworkWaitingOverlay_DefinesOverlayTemplateAndBounceMotion`; filter: `FullyQualifiedName~ScreensaverRenderBehaviorTests` | PASS |
| T-034 | Unit/UI | F-034 | `FloatingClockBuilderTests` verifies local summary + 11 exchange cards and world index symbol mapping; filter: `FullyQualifiedName~FloatingClockBuilderTests` | PASS |
| T-035 | Unit | F-035 | Covered by `NewYorkMarketStatusServiceTests` (includes `(New York)` plus opening/closing countdown assertions) | PASS |
| T-036 | Integration (calendar service) | F-036, F-037 | `ExchangeMarketCalendarIntegrationTests` verifies offline cache overlay + refresh-due live FMP/EOD merge + cache persistence; filter: `FullyQualifiedName~ExchangeMarketCalendarIntegrationTests|FullyQualifiedName~ExchangeMarketCalendarServiceTests` | PASS |
| T-037 | Unit (calendar parser) | F-036 | `ExchangeMarketCalendarServiceTests` verifies ambiguous holiday payloads do not auto-close; filter: `FullyQualifiedName~ExchangeMarketCalendarServiceTests` | PASS |
| T-038 | Integration (photo cache service) | F-038 | `ExchangePhotoCacheServiceTests` verifies custom-folder override, managed attribution file generation, and one-by-one runtime fetch progression; filter: `FullyQualifiedName~ExchangePhotoCacheServiceTests` | PASS |
| T-039 | Installer integration (sandbox/VM) | F-039, F-040 | `Guest-InstallerUninstallProbe.ps1` in-guest probe now verifies install artifact creation + uninstall cleanup of System32/ProgramData/%LOCALAPPDATA% caches; latest pass artifact: `build/vm/artifacts/vm-results/t039-probe/t039-probe-20260410-184303.json` | PASS |
| T-040 | Integration | F-041 | `InternetProbeServiceTests` validates default host/attempts and cache invalidation forcing a fresh probe | PASS |
| T-041 | Integration/UI | F-042 | `StartupCoordinatorTapeItemTests` + `ScreensaverRenderBehaviorTests.ApplyQuoteToGraph_StaleQuoteHidesCardAndClearsValues`; filter: `FullyQualifiedName~StartupCoordinatorTapeItemTests|FullyQualifiedName~ScreensaverRenderBehaviorTests` | PASS |
| T-042 | UI/Render | F-043 | `ScreensaverRenderBehaviorTests.MacroMeter_ProducesArcPathAndStatusBarBindsRoundMeterFields`; filter: `FullyQualifiedName~ScreensaverRenderBehaviorTests` | PASS |
| T-043 | UI automation (multi-resolution) | F-044 | VM runs executed at attempted `1366x768`, `1920x1080`, `3440x1440` with exported artifacts (`build/vm/artifacts/vm-results/20260410-182354-1366x768`, `...182659-1920x1080`, `...183004-3440x1440`), but guest framebuffer remained fixed at `1366x768` in all captures | BLOCKED |
| T-044 | Unit/UI text checks | F-045 | `ConfigTextConsistencyTests` validates BETA-5 version constants, About text metadata, and config window title consistency | PASS |

## 3) Immediate Risks Found During Audit

1. `IsClosedHoliday` ambiguity risk remains mitigated by parser tests plus explicit-only close detection; refresh-due live calendar sync is now covered by integration tests.
2. Multi-resolution and resize regressions still rely on VM-hosted UI automation for full confidence (`T-016`, `T-017`, `T-043`).
3. True multi-resolution UI validation is still constrained by current VM display mode lock (captures remain `1366x768` despite resolution hints), so `T-043` remains blocked.

## 4) Automated Test-Debug Loop (Operational Runbook)

Run this sequence for each cycle before updating any checklist status rows:

```powershell
.\build\build-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300
.\build\publish-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300
dotnet test .\tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --nologo --no-build
```

Timeout policy:

1. All host-shell commands are capped at `300s` per invocation.
2. Build/publish commands should normally complete well below that cap; investigate any single step that exceeds `180s`.
3. Long VM operations must be launched asynchronously and polled with bounded checks.
4. If a command exceeds its cap, kill it and continue from artifacts/log evidence.

For focused re-runs while debugging a specific item:

```powershell
dotnet test .\tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~<TestClassOrMethod>"
```

### Existing Coverage Command Map

| Checklist ID | Current automated anchor | Suggested filter |
|---|---|---|
| T-001 | `AppSettingsNormalizerTests` | `FullyQualifiedName~AppSettingsNormalizerTests` |
| T-002 | `SettingsValidatorTests` | `FullyQualifiedName~SettingsValidatorTests` |
| T-003 | `DataSourceSymbolEligibilityTests` | `FullyQualifiedName~DataSourceSymbolEligibilityTests` |
| T-004 | `NewYorkMarketStatusServiceTests` | `FullyQualifiedName~NewYorkMarketStatusServiceTests` |
| T-005 | `SymbolProfileHeuristicsTests` | `FullyQualifiedName~SymbolProfileHeuristicsTests` |
| T-006 | `FinnhubQuoteProviderTests` | `FullyQualifiedName~FinnhubQuoteProviderTests` |
| T-007 | `QuoteSchedulerTests` | `FullyQualifiedName~QuoteSchedulerTests` |
| T-008 | `MarketSessionResolverTests` | `FullyQualifiedName~MarketSessionResolverTests` |
| T-009 | `MainWindowViewModelValidationTests` | `FullyQualifiedName~MainWindowViewModelValidationTests` |
| T-010 | `MainWindowViewModelValidationTests` | `FullyQualifiedName~MainWindowViewModelValidationTests` |
| T-011 | `TickerGroupEditorViewModelTests` + `AppSettingsNormalizerTests` | `FullyQualifiedName~TickerGroupEditorViewModelTests|FullyQualifiedName~AppSettingsNormalizerTests` |
| T-014 | `NewsFeedValidationServiceTests` | `FullyQualifiedName~NewsFeedValidationServiceTests` |
| T-015 | `DataSourcePolicyValidationTests` | `FullyQualifiedName~DataSourcePolicyValidationTests` |
| T-018 | `ConfigTextConsistencyTests` | `FullyQualifiedName~ConfigTextConsistencyTests` |
| T-019 | `StartupCoordinatorAdvancedTests` | `FullyQualifiedName~StartupCoordinatorAdvancedTests` |
| T-020 | `StartupCoordinatorAdvancedTests` | `FullyQualifiedName~StartupCoordinatorAdvancedTests` |
| T-023 | `DataSourceSymbolEligibilityTests` | `FullyQualifiedName~DataSourceSymbolEligibilityTests` |
| T-024 | `YahooFinanceSessionServiceTests` | `FullyQualifiedName~YahooFinanceSessionServiceTests` |
| T-025 | `YahooFinanceQuoteProviderTests` | `FullyQualifiedName~YahooFinanceQuoteProviderTests` |
| T-026 | `HybridHistoricalDataProviderTests` | `FullyQualifiedName~HybridHistoricalDataProviderTests` |
| T-027 | `StartupCoordinatorAdvancedTests` | `FullyQualifiedName~StartupCoordinatorAdvancedTests` |
| T-028 | `StartupCoordinatorAdvancedTests` | `FullyQualifiedName~StartupCoordinatorAdvancedTests` |
| T-029 | `StartupCoordinatorNewsTests` | `FullyQualifiedName~StartupCoordinatorNewsTests` |
| T-030 | `StartupCoordinatorTapeItemTests` | `FullyQualifiedName~StartupCoordinatorTapeItemTests` |
| T-031 | `ScreensaverRenderBehaviorTests` | `FullyQualifiedName~ScreensaverRenderBehaviorTests` |
| T-032 | `ScreensaverRenderBehaviorTests` | `FullyQualifiedName~ScreensaverRenderBehaviorTests` |
| T-033 | `ScreensaverRenderBehaviorTests` | `FullyQualifiedName~ScreensaverRenderBehaviorTests` |
| T-034 | `FloatingClockBuilderTests` | `FullyQualifiedName~FloatingClockBuilderTests` |
| T-035 | `NewYorkMarketStatusServiceTests` | `FullyQualifiedName~NewYorkMarketStatusServiceTests` |
| T-036 | `ExchangeMarketCalendarIntegrationTests` + `ExchangeMarketCalendarServiceTests` | `FullyQualifiedName~ExchangeMarketCalendarIntegrationTests|FullyQualifiedName~ExchangeMarketCalendarServiceTests` |
| T-038 | `ExchangePhotoCacheServiceTests` | `FullyQualifiedName~ExchangePhotoCacheServiceTests` |
| T-039 | `Guest-InstallerUninstallProbe.ps1` (VM) | in-guest probe artifact `build/vm/artifacts/vm-results/t039-probe/t039-probe-20260410-184303.json` |
| T-040 | `InternetProbeServiceTests` | `FullyQualifiedName~InternetProbeServiceTests` |
| T-041 | `StartupCoordinatorTapeItemTests` + `ScreensaverRenderBehaviorTests` | `FullyQualifiedName~StartupCoordinatorTapeItemTests|FullyQualifiedName~ScreensaverRenderBehaviorTests` |
| T-042 | `ScreensaverRenderBehaviorTests` | `FullyQualifiedName~ScreensaverRenderBehaviorTests` |
| T-043 | VM multi-resolution run export (`Guest-ExportLatestVmUxResult.ps1`) | attempted runs exported under `build/vm/artifacts/vm-results/*-(1366x768|1920x1080|3440x1440)`; all screensaver captures remained `1366x768` |
| T-021 | `ProviderBudgetLedgerServiceTests` | `FullyQualifiedName~ProviderBudgetLedgerServiceTests` |
| T-022 | `ProviderBudgetLedgerServiceTests` | `FullyQualifiedName~ProviderBudgetLedgerServiceTests` |
| T-037 | `ExchangeMarketCalendarServiceTests` | `FullyQualifiedName~ExchangeMarketCalendarServiceTests` |
| T-044 | `ConfigTextConsistencyTests` | `FullyQualifiedName~ConfigTextConsistencyTests` |

## 5) Item-by-Item Update Rules

When progressing checklist items (`T-009` onward), update the row immediately after each run using these conventions:

1. `TODO` -> `PASS` only when a deterministic automated check exists and passes in this branch.
2. `TODO` -> `FAIL` when the check exists and fails reproducibly; include brief failure signature in commit/PR notes.
3. `TODO` -> `BLOCKED` for VM/UI dependencies not executable in current environment.
4. Add/update the "Proposed check" text with exact command/filter once implemented.
5. Keep evidence references in section 1 aligned with any behavior changes discovered during debugging.

## 6) Multi-Pass UX Issue Audit (2026-04-11, Issue Identification Only)

Passes executed:

1. Artifact visual pass over VM UX captures in `build/vm/artifacts/vm-results/*`.
2. Layout-constraint pass over config/saver XAML and screensaver placement logic.
3. Harness-validity pass over VM UX runner script to confirm whether all tabs/resolution variants were truly exercised.

Numbered findings:

1. `UX-001` Config footer command cluster wraps into multiple rows at compact width, causing severe crowding and unreadable status area.
   Evidence: `src/PortfolioSaver.Config/Windows/MainWindow.xaml:523-531` (`WrapPanel` + six fixed-min-width buttons) and compact capture `build/vm/artifacts/vm-results/20260410-182354-1366x768/config-compact-main.png`.
2. `UX-002` Config tab header text appears clipped/corrupted in current VM captures, reducing navigability and confidence.
   Evidence: `build/vm/artifacts/vm-results/20260410-182354-1366x768/config-compact-main.png`, `.../config-medium-main.png`, `.../config-large-main.png`.
3. `UX-003` General-tab slider value labels and section notes are visibly truncated in compact/medium layouts.
   Evidence: `src/PortfolioSaver.Config/Windows/MainWindow.xaml:115-143,171-178` (tight fixed value columns), captures `config-compact-main.png` and `config-medium-main.png`.
4. `UX-004` Tape/benchmark editors are over-constrained for compact widths; users must horizontally scroll to reach key controls (validation badge/remove), which degrades usability.
   Evidence: `src/PortfolioSaver.Config/Windows/MainWindow.xaml:297-343,361-405` and horizontal-scroll wrappers at `:297` and `:361`.
5. `UX-005` Advanced-tab UX is not currently evidenced in artifacts; no advanced screenshots are generated despite script intent.
   Evidence: no `config-*-advanced.png` under `build/vm/artifacts/vm-results/`; script mismatch in `build/vm/Run-VmUxValidation.ps1:218-220,237-241` (old window title and `Main` tab target).
6. `UX-006` Screensaver top-band collisions: market-status text, warmup text, macro chips, and clock subtitle crowd/overlap in the same visual lane.
   Evidence: `screensaver-12s.png` and `screensaver-36s.png`; layout anchors in `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml:85-90` and sizing/margins in `.xaml.cs:541-545`.
7. `UX-007` Network waiting overlay competes with the floating clock card (both occupy top half during warmup), causing readability loss.
   Evidence: `screensaver-12s.png`; placement logic in `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs:628-657`.
8. `UX-008` Tape content clips at lane edges during motion (partial symbols at right edge and cropped left labels), indicating insufficient safe insets/masking behavior.
   Evidence: `screensaver-36s.png`, `screensaver-66s.png`; tape block starts at `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml:85-96`.
9. `UX-009` Multi-resolution validation remains invalid: output directories tagged `1920x1080` and `3440x1440` still contain `1366x768` screensaver captures.
   Evidence: capture dimensions under `build/vm/artifacts/vm-results/20260410-182659-1920x1080/*` and `.../20260410-183004-3440x1440/*` are `1366x768`.
10. `UX-010` Config-capture text artifacts may be partially capture-pipeline-related (`PrintWindow`/window-composition path), not only layout logic; must be cross-checked with in-session UIA/WinAppDriver screenshots.
    Evidence: repeated glyph corruption in config captures; capture path in `build/vm/Run-VmUxValidation.ps1:71-107`.
11. `UX-011` Warmup behavior regressed: cached values are not surfaced immediately as yellow placeholders while live batches are still loading.
    Evidence: user-observed during live VM run (2026-04-11); initial warmup states delay visible per-symbol value continuity.
12. `UX-012` Warmup batching UX regressed: screens are not incrementally refreshed per completed batch; user perception is "wait for too much completion before useful updates".
    Evidence: user-observed during live VM run (2026-04-11).
13. `UX-013` Tape motion jitter during quote updates regressed; lanes visually twitch when new values arrive.
    Evidence: user-observed during live VM run (2026-04-11).
14. `UX-014` Update flash regressions: ticker flash and graph-card flash are inconsistent/absent on new data.
    Evidence: user-observed during live VM run (2026-04-11).
15. `UX-015` Default tape names/ticker sets appear regressed from prior approved baseline.
    Evidence: user-observed during live VM run (2026-04-11); requires baseline expectation reconciliation.
16. `UX-016` Default per-tape speed differentiation appears regressed; tapes look too uniform in motion.
    Evidence: user-observed during live VM run (2026-04-11).
17. `UX-017` Global exchange clock card truncates index labels/sparkline micro-graphs.
    Evidence: user-observed during live VM run (2026-04-11); corroborated by top-card crowding in `build/vm/artifacts/live-ux-20260411/deep-run3-monitor-t8m.png`.
18. `UX-018` Severe top-of-screen overlap/corruption during runtime (status text + macro chips + clock + floating graphs) remains unresolved.
    Evidence: user-observed during live VM run (2026-04-11); visible in `build/vm/artifacts/live-ux-20260411/deep-run3-monitor-t8m.png`.
19. `UX-019` Background transition currently jitters foreground layers noticeably; transition style appears to disturb perceived stability of tapes/cards.
    Evidence: user-observed during live VM run (2026-04-11).
20. `UX-020` Slow background zoom effect appears regressed/absent relative to prior expected behavior.
    Evidence: user-observed during live VM run (2026-04-11).
21. `UX-021` Market macro indicators should render as speedometer-style gauges.
    Evidence: user requirement during live UX run (2026-04-11).

## 7) Current Debug Status (2026-04-12)

1. `UX-034` Ticker values not populating: partially resolved.
   Evidence: VM deep run `build/vm/artifacts/vm-results/ux-deep-20260411-214047` on staged `BETA-5.3.2` shows live tape values present from the first saver captures onward (`screensaver-001.png`, `screensaver-026.png`, `screensaver-072.png`).
   Technical change:
   Yahoo provider now stops cascading into chart/quote fallback spam after a `429`, surfaces partial results explicitly, and startup/runtime continue into backup providers for unresolved symbols.
2. Recovery refresh cadence after partial startup failure: resolved.
   Evidence: same VM run shows provider labels changing across the 6-minute window (`Finnhub + Tiingo + Twelve Data (partial)` then `Tiingo + Cache (partial)`), which confirms follow-up refresh attempts are occurring instead of idling for the full steady-state interval.
3. Remaining data-path blockers after this fix:
   `UX-035` Macro strip values are still blank in saver captures (`VIX`, `VIX3M`, `UST10Y`, `YLD SPRD`, `DXY` chips show `--`).
   `UX-043` Global Markets exchange cards still lack populated index values/sparklines in the 2026-04-12 VM captures.
4. Existing issue mappings retained:
   clock typography regression remains tracked under `UX-042`.
   top-right/top-band corruption remains tracked under `UX-018`.
22. `UX-022` All clocks should use a fixed-size seven-segment LED style font.
    Evidence: user requirement during live UX run (2026-04-11).

## 7) UX Test Backlog Additions (No Code Changes Yet)

| Test ID | Scope | Related UX IDs | Proposed check | Status |
|---|---|---|---|---|
| T-045 | WinAppDriver / UIA | UX-001, UX-002, UX-003 | Per-size (`900x620`, `1200x760`, `1600x900`) open `General` and `Advanced`, assert tab headers/buttons/value labels are fully visible and non-overlapping | TODO |
| T-046 | WinAppDriver bounds check | UX-001, UX-004 | Enumerate footer button bounding rectangles and assert no intersections, no clipping beyond window client rect | TODO |
| T-047 | Screensaver geometry check | UX-006, UX-007 | Runtime overlap assertions between status bar/macro chip region, clock bounds, and network-waiting card bounds | TODO |
| T-048 | Screensaver lane clipping check | UX-008 | Detect text clipping at tape lane boundaries over timed captures (12s/36s/66s) | TODO |
| T-049 | Capture-pipeline A/B | UX-010 | Compare `PrintWindow` capture vs full-screen capture vs WinAppDriver screenshot for same config state | TODO |
| T-050 | Resolution-truth check | UX-009 | Before each run, assert guest desktop resolution and saved screenshot dimensions match requested profile label | TODO |
| T-051 | Warmup incremental rendering | UX-011, UX-012 | Assert cached/yellow placeholders appear immediately and per-batch updates become visible without full warmup completion | TODO |
| T-052 | Tape motion stability | UX-013, UX-016 | During quote-update bursts, assert tape lane transform deltas stay smooth and preserve per-tape speed offsets | TODO |
| T-053 | Update flash verification | UX-014 | Validate ticker + graph-card flash triggers fire on value changes and are visually observable in captures | TODO |
| T-054 | Default baseline reconciliation | UX-015 | Compare current defaults (tape names/symbol lists/speeds) against approved baseline document and flag diffs | TODO |
| T-055 | Clock-card truncation/overlap bounds | UX-017, UX-018 | Check clock inner text and index sparkline bounds for clipping and assert no overlap with top overlays/graph cards | TODO |
| T-056 | Background transition stability | UX-019 | Measure foreground element motion deltas during background crossfade/transition and assert no jitter injection into tapes/cards | TODO |
| T-057 | Background slow-zoom regression check | UX-020 | Validate long-horizon background scale interpolation remains active and smooth across image rotation cycles | TODO |
| T-058 | Macro indicator visual style | UX-021 | Verify macro widgets render as speedometer gauges (needle/arc semantics) and remain legible at runtime scales | TODO |
| T-059 | Clock typography conformance | UX-022 | Verify all clock readouts use fixed-size 7-segment LED font styling without fallback/mixed fonts | TODO |

## 8) Targeted Patch Recommendations (Pre-Coding)

Focus: `UX-011` ("tickers remain yellow too long") and related warmup/staleness UX.

1. Decouple "refresh due" from "display stale":
   - Keep a quote visually live until it exceeds hard stale age (`15m`) or provider explicitly returns unusable data.
   - Avoid setting `QuoteSnapshot.IsStale=true` just because the symbol was due but not refreshed in the current pass.
2. Introduce two-stage stale states:
   - Soft stale (`age > refresh window`): keep last value visible, apply muted styling.
   - Hard stale (`age >= 15m`): existing yellow + blank behavior.
3. Prioritize stale symbols in recovery:
   - Add a "stale-first" queue in refresh selection so stale-visible symbols are always attempted before fresh symbols.
4. Warmup immediacy improvements:
   - Ensure cache-backed values are emitted on frame 1, and each completed warmup batch re-renders immediately (no gating on later batches).
5. Configurable UX tuning:
   - Add optional warmup aggressiveness settings (default safe): startup burst size / stale-retry cadence to reduce yellow dwell without violating provider budgets.

Patch impact map:

- Primary files likely touched:
  - `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs`
  - `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs`
  - `src/PortfolioSaver.Core/Models/QuoteSnapshot.cs` (if introducing nuanced stale state metadata)
- Verification anchors:
  - Existing: `T-051`, `T-052`, `T-053`
  - New/updated for yellow dwell: add assertion on max yellow dwell time for cache-backed symbols during warmup.

## 9) Coding Cycle Progress Tracker (BETA-5.x)

Legend:
- `[ ]` Open
- `[x]` Completed (coded + unit tested)

1. [x] ~~`UX-001` Config footer command cluster wrap/crowding~~
2. [x] ~~`UX-002` Config tab header clipping/corruption~~
3. [x] ~~`UX-003` General-tab slider/notes truncation~~
4. [x] ~~`UX-004` Tape/benchmark editor compact over-constraint~~
5. [x] ~~`UX-005` Advanced-tab evidence gap in harness~~
6. [x] ~~`UX-006` Top-band collision (status/warmup/macro/clock)~~
7. [x] ~~`UX-007` Network waiting overlay vs clock conflict~~
8. [x] ~~`UX-008` Tape edge clipping in motion~~
9. [x] ~~`UX-009` Multi-resolution validation mismatch~~
10. [x] ~~`UX-010` Config capture artifact pipeline issue~~
11. [x] ~~`UX-011` Yellow stale-state dwell too long~~
12. [x] ~~`UX-012` Warmup not visibly incremental enough~~
13. [x] ~~`UX-013` Tape jitter during quote updates~~
14. [x] ~~`UX-014` Ticker/card flash regression~~
15. [x] ~~`UX-015` Default tape names/symbol lists regression~~
16. [x] ~~`UX-016` Default per-tape speed differentiation regression~~
17. [x] ~~`UX-017` Clock card index/sparkline truncation~~
18. [x] ~~`UX-018` Top-of-screen overlap/corruption~~
19. [x] ~~`UX-019` Background transition jitters foreground perception~~
20. [x] ~~`UX-020` Slow background zoom regression~~
21. [x] ~~`UX-021` Macro indicators should be speedometer-style~~
22. [x] ~~`UX-022` All clocks use fixed-size seven-segment LED font~~
23. [x] ~~`UX-023` Startup/warmup floating batch-progress card removed; top status progress retained~~
24. [x] ~~`UX-024` Seven-segment clock rendering is not guaranteed on clean VM images (font fallback to non-LED look).~~
25. [x] ~~`UX-025` Flash behaviors are hard to validate visually in warm-start sessions; need deterministic example pulses or explicit visual test mode.~~

## 10) Incremental Fix Batch (Post BETA-5.3.1)

Resolved in this batch:

1. `UX-026` Macro section jerk/distortion reduced by throttling macro-meter rebuild cadence (`8s`) and forcing immediate refresh only on scene updates.
2. `UX-027` Macro chip truncation reduced by wider chip minimums and horizontal overflow support in status bar meter strip.
3. `UX-028` Global Markets card truncation reduced by larger clock card geometry, larger text bounds, and 3-column no-scroll layout sized for full 12 cards.
4. `UX-029` Warmup "no sources" startup regression guarded by defensive Yahoo provider fallback when policies are empty/disabled.
5. `UX-030` Config `More` menu reduced to essential actions (Help, License, About) to lower action noise and accidental flow breaks.

Code evidence:

- `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs`
- `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs`
- `src/PortfolioSaver.Render/Controls/StatusBarControl.xaml`
- `src/PortfolioSaver.Render/Controls/FloatingClockControl.xaml`
- `src/PortfolioSaver.Config/Windows/MainWindow.xaml`

Automated verification:

- `StartupCoordinatorAdvancedTests.BuildQuoteProviders_WhenPoliciesDisableAllSources_StillKeepsYahooFallback`
- `ScreensaverRenderBehaviorTests.ScreensaverLayout_UsesDedicatedClockAndWaitingBounds_AndExpandedClockGridCards`
- Targeted suite run:
  `dotnet test .\tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --filter "FullyQualifiedName~StartupCoordinatorAdvancedTests|FullyQualifiedName~ScreensaverRenderBehaviorTests|FullyQualifiedName~ConfigTextConsistencyTests"`
  -> **PASS (31/31)**

## 11) UX Cycle Wrap-Up (2026-04-11)

Primary artifact reviewed in this wrap-up:

- `build/vm/artifacts/vm-results/ux-deep-20260411-094524`

Evidence highlights:

1. Config pass captured both tabs plus per-widget images (`config-tab-001-General.png`, `config-tab-002-Advanced.png`, `config-*.png`).
2. Screensaver pass captured `screensaver-001.png` through `screensaver-240.png`, but foreground continuity is broken mid-sequence.
3. Ticker values remain unpopulated (yellow symbols with blank value fields) in sampled screensaver frames.

Open issues confirmed/added in this cycle:

1. `UX-031` Config first-paint corruption remains severe (garbled/truncated labels, duplicated text fragments) on both tabs.
2. `UX-032` Footer/button area still appears action-cluttered in captures, with multiple validate-like actions visible.
3. `UX-033` Advanced Data Sources grid text/header readability is degraded by clipping/corruption in capture evidence.
4. `UX-034` Ticker values are not populating in screensaver runtime (user-reported and confirmed in captures).
5. `UX-035` Global Markets detail rows show non-progressing placeholder-style time/value rendering in sampled frames.
6. `UX-036` Screensaver persistence is unstable in this run: sequence shifts to desktop/cmd foreground before expected uninterrupted 20-minute visual session.
7. `UX-037` Harness foreground contamination remains possible; later `screensaver-*.png` frames can include desktop/shell instead of saver content.

Backlog additions from this wrap-up:

1. `T-060` Data population gate: assert at least one non-empty numeric ticker value appears and persists during early runtime.
2. `T-061` Saver persistence gate: assert no foreground desktop/shell frames during planned screensaver capture duration.
3. `T-062` Capture-purity gate: fail run if `screensaver-*.png` contains sustained non-screensaver foreground windows.

## 12) Current Coding Pass (2026-04-11)

### 12.1 Trace Facility Feature Added

New feature:

- `F-046` Runtime tracing facility
  - Writes to a `4MB` circular trace file in `%APPDATA%\PortfolioSaver\Trace\trace.circular.log`.
  - Streams each trace line over UDP to `sanyalnet-oracle-vps2.duckdns.org:65514`.
  - Includes hostname + local IP + process id in every trace line/payload.
  - Includes thread id plus structured `event=... | key=value` state snapshots for quote warmup/refresh, macro meters, Global Markets clock data, and scene/timer state.

Code evidence:

- `src/PortfolioSaver.Shared/Diagnostics/TraceLog.cs`
- `src/PortfolioSaver.Config/App.xaml.cs`
- `src/PortfolioSaver.Screensaver/App.xaml.cs`
- `src/PortfolioSaver.Screensaver/Program.cs`
- `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs`
- `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs`
- `src/PortfolioSaver.Screensaver/Services/InputExitMonitor.cs`

Automated checks:

1. `T-063` `TraceLogTests.TraceLog_WritesToFourMegCircularFileUnderAppData` -> PASS
2. `T-064` `TraceLogTests.TraceLog_InfoState_WritesStructuredFields` -> PASS
3. Full suite run after trace integration -> PASS (`119/119`)

Trace retrieval/debug workflow:

1. Collect `%APPDATA%\PortfolioSaver\Trace\trace.circular.log` and `%APPDATA%\PortfolioSaver\Trace\trace.circular.idx`.
2. Reconstruct chronological trace text using the runbook recipe in:
   - `build/vm/VM_OPERATIONS_RUNBOOK.md` section `12) Runtime Trace Retrieval (for Debug/Defect Analysis)`.
3. Use targeted filters first (`StartupCoordinator`, `Warmup`, `Quotes resolved`, `InputExitMonitor`, `ERROR`) before deeper frame-by-frame UX investigation.
4. For current UX blockers, inspect structured trace events first:
   - `event=QuoteRefreshPlan`
   - `event=ProviderReturnedQuotes`
   - `event=ProviderFailed`
   - `event=QuoteResolutionSummary`
   - `event=MacroSnapshot`
   - `event=ClockMarketDataSummary`
   - `event=ClockDataRefresh`

### 12.2 UX-034 First (Ticker Values Not Populating)

Implemented fixes:

1. Do not blanket-mark cached quotes stale when network probe is unavailable; stale now remains based on **last successful fetch timestamp + usable value presence**.
2. Preserve warmup/live quote continuity in scene updates by merging incoming quotes into existing quote state before tape/benchmark rebuild.
3. Persist warmup Yahoo batches to local quote cache so recent successful fetches survive transient network-probe failures.
4. Keep fallback behavior compatible with closed markets: latest fetched values remain visible until hard stale threshold.

Evidence:

- `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs`
- `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs`
- `tests/PortfolioSaver.Tests/Services/StartupCoordinatorAdvancedTests.cs`
  - `LoadQuotesAsync_WhenNetworkUnavailable_RecentCachedQuoteRemainsVisible` -> PASS

### 12.3 UX-031..UX-037 (excluding UX-034) Coding Pass

Implemented in code/harness:

1. `UX-031`/`UX-033`: enable software rendering automatically in config app for VM/virtualized environments to reduce first-paint glyph corruption and advanced-tab artifacts.
2. `UX-032`: one primary validation action remains in config footer; menu kept minimal (`Help/License/About`).
3. `UX-035`: seven-segment converter now auto-falls back to plain digits when segment glyphs are unsupported, preventing placeholder/tofu rendering.
4. `UX-036`: added `PORTFOLIOSAVER_DISABLE_INPUT_EXIT` support so automation can keep saver alive through full capture window.
5. `UX-037`: deep UX harness now detects early saver exit and records failure; also writes both `ux-deep-summary.json` and `vm-ux-summary.json` for compatibility.

Evidence:

- `src/PortfolioSaver.Config/App.xaml.cs`
- `src/PortfolioSaver.Render/Converters/SevenSegmentDigitConverter.cs`
- `src/PortfolioSaver.Screensaver/Services/InputExitMonitor.cs`
- `build/vm/Guest-UxDeepExercise.ps1`

## 13) Code Review Findings (2026-04-11)

Scope of this pass:

1. Full build + full test run.
2. Static grep sweep for dead paths / placeholder exceptions / suppressed warnings.
3. Manual inspection of startup, provider-budget, tracing, and settings plumbing.

Resolution status (BETA-5.3.2):

1. `CR-001` Test isolation defect in provider fallback test (nondeterministic full-suite behavior).
   - Fix: `StartupCoordinator` now accepts injected `ProviderBudgetLedgerService`; advanced tests now create isolated temporary ledgers per coordinator instance.
   - Evidence:
     - `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs`
     - `src/PortfolioSaver.Screensaver/Services/ProviderBudgetLedgerService.cs`
     - `tests/PortfolioSaver.Tests/Services/StartupCoordinatorAdvancedTests.cs`
2. `CR-002` Trace UDP delivery can silently stay disabled after one transient DNS failure.
   - Fix: UDP endpoint resolution now uses retry/backoff and invalidates endpoint on send failures; subsequent traces re-attempt DNS resolution.
   - Evidence:
     - `src/PortfolioSaver.Shared/Diagnostics/TraceLog.cs`
3. `CR-003` Dead/legacy settings surface in `AppSettings` with no runtime consumers.
   - Fix: removed unused settings and legacy enum from runtime model and defaults.
   - Removed keys: `Provider`, `ApiKeyProtected`, `EnableBlur`, `BlurRadius`, `EnableRoaming`, `MaxFinnhubRequestsPerMinuteSafe`, `MaxTwelveDataCreditsPerMinuteSafe`, `MaxTwelveDataCreditsPerDaySafe`, `EnableProviderFailover`, `ShowLocalAndNewYorkTime`, `ProtectedApiKey`.
   - Evidence:
     - `src/PortfolioSaver.Core/Models/AppSettings.cs`
     - `src/PortfolioSaver.Core/Constants/Defaults.cs`
     - `src/PortfolioSaver.Core/Validation/SettingsValidator.cs`
     - `build/vm-settings.example.json`
     - `build/sandbox/Run-PortfolioSaverSandboxUiValidation.ps1`

Sanity checks passed in this pass:

1. `dotnet build .\PortfolioScreensaver.sln -c Release --nologo` -> `0 warnings / 0 errors`.
2. No `NotImplementedException`, `#pragma warning disable`, or source-level TODO markers found in `src/` and `tests/`.
3. No obvious unused private fields/methods found via declaration/reference sweeps in `src/` and `tests/`.

## 14) Baseline BETA-5.3.2

Baseline updates:

1. Version bumped to `0.9.0-beta5.3.2`.
2. CR-001/CR-002/CR-003 changes integrated.
3. Trace retrieval and reconstruction workflow documented in VM runbook section 12.

## 15) Active Runtime Blockers (Reopened 2026-04-11)

Scope note:
These blockers are active **in addition to** unresolved/failing BETA-5.3.1 UX fixes seen in live VM verification.

1. `UX-031` Config first-paint corruption still appears intermittently on VM startup.
2. `UX-032` Config footer still presents action clutter in some runtime captures.
3. `UX-033` Advanced-tab readability still regresses in VM capture path.
4. `UX-034` Ticker values can remain blank/yellow for extended runtime windows.
5. `UX-035` Global Markets details still show stale/placeholder-style rows in some runs.
6. `UX-036` Screensaver persistence still intermittently exits before the full planned capture duration.
7. `UX-037` Harness can still capture desktop foreground after saver disruption.
8. `UX-038` Trace forwarding runtime verification gap: confirm live `program=PortfolioSaver.Screensaver` payloads arrive at `sanyalnet-oracle-vps2.duckdns.org:65514` with `program` and `function`.
9. `UX-039` New York countdown text must always display days+hours format when day component exists (suppress day part when `0`).
10. `UX-040` Default ticker-feed retrieval cadence must remain `20 minutes` across normal/off-hours profiles.
11. `UX-041` Global Markets card must never use a scroller; all 12 sub-cards must be visible via sizing/layout.
12. `UX-042` Clock typography regression: all clock readouts must consistently render with fixed-size seven-segment LED styling.
13. `UX-043` Global Markets layout concept is still wrong for the current content density.
    - The twelve exchange cards are too large to live inside one dedicated container card.
    - Required redesign: remove the enclosing container-card treatment, make each exchange card narrower, and arrange the set as a floating line/band that moves vertically.
14. `UX-044` Global Markets exchange index values are still missing at runtime.
    - International exchange indices are available through Yahoo Finance, but retrieval must be folded into warmup/refresh without reintroducing Yahoo anti-flooding violations.
15. `UX-045` Top-right corruption likely includes direct clash between macro chips and clock region.
    - Treat as a focused follow-on to the broader top-band overlap issue (`UX-018`) and validate exact bounding-box collisions.
16. `UX-046` Remove benchmark-card configuration surface from the product.
    - Config app still exposes benchmark refresh plus floating benchmark-card editing UI.
    - Screensaver surface no longer shows a separate benchmark strip in current XAML, but runtime/settings plumbing for benchmarks still exists and should be audited for full removal.

## 16) Additional User-Reported Issue Logging (2026-04-12)

Mapped to existing issues:

1. Clock fonts regressed from 7-segment LED -> already tracked as `UX-042`.
2. Macro values at top are empty -> already tracked as `UX-035`.
3. Top-right corruption / possible `VIX` vs clock clash -> already covered broadly by `UX-018`, with focused follow-on logged as `UX-045`.
4. Remove benchmark surface from screensaver -> visually appears already removed from current screensaver XAML, but config/runtime removal is not complete; tracked as `UX-046`.
5. Initial paint corruption of the config screen -> already tracked as `UX-031`.

Newly logged in this pass:

1. `UX-043` Global Markets cards are too large for a single enclosing container-card concept and need a different moving layout.
2. `UX-044` Global Markets exchange index data is missing and must be fetched without breaking Yahoo anti-flood logic.
3. `UX-045` Top-right corruption needs explicit collision analysis between macro chips and clock region.
4. `UX-046` Benchmark-card configuration/runtime plumbing still needs full removal from the product.

Current code changes in this pass target `UX-034` first by allowing opportunistic live fetch even when ICMP probe is unavailable (common VM condition), while preserving provider budget/cooldown logic.

## 17) VM UX + Trace Pass (2026-04-18, Stable BETA-5.3.2)

Artifacts:

1. Clean UX result bundle: `build/vm/artifacts/vm-results/ux-deep-20260418-071817`
2. Live screenshots:
   - `build/vm/artifacts/live-ux-20260418/live-112302.png`
   - `build/vm/artifacts/live-ux-20260418/live-112325.png`
3. Reconstructed trace:
   - `build/vm/artifacts/trace/ux-deep-20260418-112153-live/trace.reconstructed.log`

Run summary:

1. `ConfigPhaseStatus=Completed`
2. `ScreensaverPhaseStatus=Completed`
3. `ConfigVersionCheck=Passed`
4. `ScreensaverVersionCheck=Passed`
5. Screensaver version observed in UI: `0.9.0-beta5.3.2`
6. Capture set:
   - `232` config screenshots
   - `72` screensaver screenshots

Positive findings from this pass:

1. `UX-034` is now **partially resolved** rather than a full blocker.
   - Ticker values are visibly populating during runtime.
   - Provider status now shows useful recovery context, for example `Finnhub + Tiingo + Cache (Partial), ARKK Updated 0s ago`.
2. `UX-039` countdown formatting is behaving as intended in this run.
   - Observed text: `Closed | Opening in 02d06h06m`
3. Version watermark/version verification is working in both config and screensaver surfaces.
4. Graph cards can now appear during the run before global warmup is fully complete when cached history is available.

Trace-confirmed data-path findings:

1. Yahoo macro/world-index warmup is still failing immediately with `429` rate limiting.
   - First warmup batch `[ ^VIX, ^VIX3M ]` fails before any macro values land.
   - Evidence: `trace.reconstructed.log` entries around `07:19:44` to `07:19:47`.
2. Main ETF/equity fallback is healthy enough to recover runtime value population.
   - Finnhub, Twelve Data, and Tiingo all return quotes in the same session.
   - Evidence: `event=ProviderReturnedQuotes` and `event=QuoteResolutionSummary`.
3. Macro symbols remain completely unresolved in this pass.
   - Missing symbols: `^VIX`, `^VIX3M`, `^TNX`, `^FVX`, `DX-Y.NYB`
   - Evidence: repeated `event=MacroSnapshot` lines show all macro chips as `--`.
4. Global Markets symbols remain completely unresolved in this pass.
   - `populated_exchange_count=0`
   - Missing symbols include `^NYA`, `^FTSE`, `^N225`, `^SSEC`, `^HSI`, `^NSEI`, `^GDAXI`, `^FCHI`
   - Evidence: repeated `event=ClockMarketDataSummary`.
5. Backup-provider pacing is now its own runtime defect.
   - Twelve Data hits minute-credit exhaustion during the same 6-minute run.
   - Evidence: `Provider Twelve Data failed ... run out of API credits for the current minute.`

Human-visible UX findings from screenshots:

1. `UX-031` remains open.
   - Config first-paint corruption is still severe on both tabs.
   - Labels and section text are visibly garbled/truncated in `config-tab-001-General.png` and `config-tab-002-Advanced.png`.
2. `UX-032` remains open.
   - The footer still presents duplicated/ghosted validate affordances and corrupted footer text in config captures.
3. `UX-033` remains open.
   - Advanced-tab policy grid headers and explanatory copy are still clipped/corrupted in `config-tab-002-Advanced.png`.
4. `UX-035` remains open.
   - Macro strip stays empty even while live ticker values are updating elsewhere on screen.
5. `UX-041` remains open.
   - The Global Markets surface still behaves like a single large card and continues to occupy too much vertical space.
6. `UX-043` remains open and visually severe.
   - The Global Markets container still collides with and obscures tape lanes.
   - This is clearly visible in `live-112302.png`, `screensaver-024.png`, `screensaver-051.png`, and `screensaver-064.png`.
7. `UX-044` remains open.
   - Global Markets exchange index values/sparklines are still absent even though the card renders.
8. `UX-045` remains open.
   - The top-right lane is still overcrowded; macro chips, provider text, countdown text, and clock all compete in the same band.
9. `UX-042` remains open.
   - The clock readouts in this run do not reflect the intended fixed-size seven-segment LED presentation.

New issue logged from this pass:

1. `UX-047` Backup-provider pacing/credit exhaustion.
   - After main-ticker recovery begins, Twelve Data still overruns its minute-credit budget and drops out mid-cycle.
   - The scheduler should treat provider-minute credits as a first-class pacing constraint instead of learning only after a failed request.

Priority order for the next code/debug cycle:

1. `UX-044` Restore macro/global-index data population without violating Yahoo rate limits.
2. `UX-047` Add explicit minute-budget pacing for Twelve Data so fallback stays productive through the full run.
3. `UX-043` Redesign Global Markets layout so it no longer masks the tape lanes.
4. `UX-031` to `UX-033` Continue config-surface corruption cleanup with live-capture validation.

## 18) Post-Patch VM Validation (2026-04-18, After Yahoo/Pacing Scheduler Changes)

Artifacts:

1. Clean post-patch UX result bundle: `build/vm/artifacts/vm-results/ux-deep-20260418-074142`
2. Launcher report: `build/vm/artifacts/launcher-runs/20260418-114039/launcher-report.json`
3. Representative saver frames:
   - `screensaver-001.png`
   - `screensaver-012.png`
   - `screensaver-036.png`
   - `screensaver-072.png`

Host verification before VM run:

1. `dotnet build .\PortfolioScreensaver.sln -c Release --no-restore /p:UseSharedCompilation=false /nodeReuse:false` -> `0 warnings / 0 errors`
2. `dotnet test .\tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --no-build` -> `Passed 140 / Failed 0`
3. `publish-safe-temp.ps1` completed successfully and regenerated both release manifests.

VM result summary:

1. `ConfigPhaseStatus=Completed`
2. `ScreensaverPhaseStatus=Completed`
3. `ConfigVersionCheck=Passed`
4. `ScreensaverVersionCheck=Passed`
5. Version on screen remained `0.9.0-beta5.3.2`

What improved:

1. Main ticker population remains healthy and stable across the full 6-minute run.
2. The new publish workflow fix (`publish-safe-temp` restore with runtime identifier) is now validated end-to-end by a successful staged VM run.
3. No harness crash/desktop-drop regression occurred in this pass; the saver phase completed normally.

What is still not fixed in the UI:

1. `UX-035` Macro strip is still empty in the post-patch run.
   - `VIX`, `VIX3M`, `UST10Y`, `YLD SPRD`, and `DXY` all remain `--` in sampled frames.
2. `UX-043` Global Markets layout is still visually wrong.
   - The large container card still masks tape lanes and competes with graph cards.
3. `UX-044` Global Markets data population is still not visually credible.
   - The card is present, but the useful exchange/index content is still obscured or absent in runtime captures.
4. `UX-031` to `UX-033` remain open.
   - Config first-paint and advanced-tab text corruption are still clearly visible in the same VM cycle.
5. `UX-042` remains open.
   - Clock typography still does not match the intended fixed-size seven-segment LED presentation.

Interpretation:

1. The scheduler/provider changes are build-valid and test-valid.
2. They are not yet sufficient to resolve the macro/global-index population path in live UX.
3. The next debugging pass must use reconstructed trace from this exact post-patch run before more scheduler changes are made.

Post-patch trace findings from the exact same run:

1. Yahoo dedicated-symbol throttling is still failing on the very first macro symbol.
   - Warmup now correctly starts with a single-symbol batch: `[^VIX]`.
   - Yahoo still responds with `429` on that first direct-chart request.
2. The current dedicated-Yahoo runtime lane is starved by design after that first failure.
   - Each refresh pass only gives Yahoo one dedicated symbol attempt.
   - Because `^VIX` is always highest priority and always fails/gets cooled down, later macro symbols (`^VIX3M`, `^TNX`, `^FVX`, `DX-Y.NYB`) and all world indices never get a turn in the same pass.
   - This explains why macros and Global Markets remain completely blank despite the scheduler changes.
3. Twelve Data pacing is improved in code but still mismatched to real provider accounting.
   - Trace now shows `cost=8` for 8-symbol Twelve Data batches, which reflects the new local ledger behavior.
   - Twelve Data still returns: `9 API credits were used, with the current limit being 8`.
   - That implies the provider is charging more than the local model currently assumes for these requests (or uses a stricter minute bucket than the local ledger model).
4. Tiingo can also rate-limit under the same recovery path.
   - In this run Tiingo returned `429` after Twelve Data dropped out on one recovery cycle.
5. Main-ticker recovery remains healthy.
   - Finnhub continues to refill the portfolio/ETF lane successfully even while the macro/global-index lane is stuck.

Refined next-step requirement from trace:

1. The next code pass for data recovery must change dedicated-Yahoo scheduling from "retry the single top-priority symbol each pass" to "advance through the dedicated-symbol queue across passes even after one symbol is rate-limited."
2. Twelve Data budget modeling must be recalibrated from live evidence instead of assuming `requestedSymbolCount` equals actual minute-credit consumption.

## 19) VM UX + Trace Validation (2026-04-18, Config Safety + Global Markets Layout Pass)

Artifacts:

1. Launcher report: `build/vm/artifacts/launcher-runs/20260418-222017/launcher-report.json`
2. UX result bundle: `build/vm/artifacts/vm-results/ux-deep-20260418-182122`
3. Trace bundle: `build/vm/artifacts/trace/ux-deep-20260418-182122-trace`
4. Reconstructed trace: `build/vm/artifacts/trace/ux-deep-20260418-182122-trace/trace.reconstructed.log`
5. Representative captures:
   - `config-tab-001-General.png`
   - `config-tab-002-Advanced.png`
   - `screensaver-001.png`
   - `screensaver-036.png`
   - `screensaver-072.png`

VM result summary:

1. `ConfigPhaseStatus=Completed`
2. `ScreensaverPhaseStatus=Completed`
3. `ConfigVersionCheck=Passed`
4. `ScreensaverVersionCheck=Passed`

Confirmed improvements from this pass:

1. Provider API key fields are now masked in the configurator.
   - The startup-corruption capture still garbles the surface, but the actual secret values are no longer exposed in clear text.
2. Ticker spacing is improved.
   - In saver frames, value groups are visually tighter within a symbol and wider between symbols, with vertical separators helping separation.
3. Global Markets now starts near the bottom-right/bottom lane instead of spawning high on the screen.
   - This is clearly visible in `screensaver-001.png` and `screensaver-036.png`.
4. Dedicated Yahoo scheduling no longer stalls forever on `^VIX`.
   - Trace evidence shows the queue advancing across passes: `^VIX` during warmup, then `^NYA`, then `^FTSE`, and later `^TNX`.

Trace-backed runtime findings:

1. Dedicated Yahoo queue advancement is confirmed.
   - `event=WarmupPlan` shows the interleaved dedicated queue: `^VIX, ^NYA, ^VIX3M, ^FTSE, ^TNX, ^N225, ...`
   - Runtime attempts later include `[^NYA]`, `[^FTSE]`, and `[^TNX]`, proving the queue is rotating instead of sticking on `^VIX`.
2. The core macro/world-index blocker remains Yahoo single-symbol `429` rate limiting.
   - `^VIX`, `^NYA`, and `^FTSE` all hit `429` in this run.
3. Macro chips remain unresolved for the full 6-minute run.
   - Repeated `event=MacroSnapshot` lines still show `VIX`, `VIX3M`, `UST10Y`, `YLD SPRD`, and `DXY` as `--`.
4. Global Markets exchange indices remain unresolved for the full run.
   - Repeated `event=ClockMarketDataSummary` lines still show `populated_exchange_count=0`.
5. Twelve Data minute pacing is improved but still not fully aligned to live provider accounting.
   - This run still produced: `run out of API credits for the current minute ... 9 API credits were used, with the current limit being 8`.

Human-visible UX findings from this run:

1. `UX-031` remains open and severe.
   - Config first-paint corruption is still present on both tabs even after the startup masking/fade-in changes.
2. `UX-032` remains open.
   - The footer still shows a ghosted duplicate-Validate appearance in startup captures, even though the actual footer markup is simplified.
3. `UX-033` remains open.
   - Advanced-tab explanatory text and grid headers are still visibly corrupted/clipped in `config-tab-002-Advanced.png`.
4. `UX-035` remains open.
   - Macro meters are still empty while the main ticker lanes are live.
5. `UX-043` remains open but shifted.
   - Global Markets is now more visible because it starts lower, but it still behaves like a large grouped surface rather than a light lane of exchange cards.
6. `UX-044` remains open.
   - Global Markets cards still show no useful exchange index values or sparklines in this run.
7. `UX-045` remains open.
   - The top status band still suffers from ghosting/corruption; in late frames the upper-left and upper-right text lanes visibly smear or retain stale text.
8. `UX-048` new.
   - Floating graph cards now collide with the lowered Global Markets lane.
   - In `screensaver-036.png` and `screensaver-072.png`, graph cards overlap or visually crowd the Global Markets cards.
9. `UX-049` new.
   - Some top status text becomes truncated or corrupted while values are updating (`Provider`, macro labels, and clock zone strings can smear into neighboring regions).
10. `UX-050` new.
   - The top-right clock and Global Markets clocks still flicker and appear to be overwritten by neighboring macro/status updates before the next time refresh repaints them.
   - The most likely root cause is shared invalidation or overlapping status-band redraw pressure while the macro strip is failing and repainting placeholder content.
11. `UX-051` new.
   - Macro indicator design/data contract is incomplete because `YLD SPRD` currently lacks a dedicated `US2Y` input, so the displayed spread cannot become semantically correct even after quote population is fixed.
   - Deep analysis must cover symbol selection, meter update timing, and whether macro/status repaint traffic is corrupting both the macro chips and nearby clocks.

Priority order for the next code/debug cycle:

1. `UX-044` Restore macro/global-index data population under Yahoo throttling pressure.
2. `UX-047` Continue tightening provider-minute pacing, especially for Twelve Data.
3. `UX-043` Complete the Global Markets redesign so it behaves like a lighter lane, not a bulky grouped block.
4. `UX-048` Add lane-reservation/collision avoidance between floating graph cards and the Global Markets surface.
5. `UX-050` Deep analyze and eliminate clock flicker/corruption around the macro/status band.
6. `UX-051` Add `US2Y` and correct the macro/yield-spread model.
7. `UX-031` to `UX-033` Continue config-surface first-paint cleanup using live VM evidence.

## 19) VM Validation Update (2026-04-18 late)

Validation target:

- Local build/test baseline after macro redraw stabilization, stricter Yahoo/Twelve Data pacing, and reserved Global Markets lane.
- Publish source: `build/artifacts/publish-safe-temp`
- VM run: `build/vm/artifacts/vm-results/ux-deep-20260418-190301`
- Trace bundle: `build/vm/artifacts/trace/ux-deep-20260418-190301-trace`

Local verification:

1. `dotnet build` passed on the pinned local `.NET 8.0.420` toolchain.
2. `dotnet test` passed: `145/145`.

VM run status:

1. `ConfigPhaseStatus=Completed`
2. `ScreensaverPhaseStatus=Completed`
3. `ConfigVersionCheck=Passed`
4. `ScreensaverVersionCheck=Passed`

Confirmed improvements from this pass:

1. `UX-047` improved materially.
   - In the reconstructed trace for `ux-deep-20260418-190301`, no Twelve Data minute-credit failure was observed.
   - Twelve Data requests stayed at 4-symbol batches (`IJH, MTUM, VIG, XLV`, `VWO, USMV, VYMI, XLE`, `VYM, XLF, VEA, SIZE`, etc.), which is more conservative than the earlier failing 7-8 symbol behavior.
2. Dedicated Yahoo queue advancement remains confirmed.
   - This run advanced across `^VIX`, `^NYA`, `^VIX3M`, `^FTSE`, and `^TNX`, so the queue is definitely no longer stalled on `^VIX`.
3. `UX-048` improved materially.
   - The lowered Global Markets lane is now actually visible and the graph cards stayed out of that lower lane in the sampled saver captures (`screensaver-001.png`, `screensaver-012.png`, `screensaver-036.png`).
4. Macro redraw churn is reduced technically.
   - Macro meters are now updated in place rather than clearing/rebuilding the collection every refresh, which should reduce top-band invalidation pressure even though the clocks still visibly flicker in the VM.

Trace-backed runtime findings:

1. `UX-044` remains the primary data-path blocker.
   - Repeated `event=MacroSnapshot` lines still show `VIX`, `VIX3M`, `UST10Y`, `YLD SPRD`, and `DXY` as `--`.
   - Repeated `event=ClockMarketDataSummary` lines still show `populated_exchange_count=0`.
2. The current dedicated-Yahoo retry strategy is functioning, but it is still not productive enough to populate macro/world-index data under current Yahoo throttling.
   - Warmup attempted `[^VIX]` and immediately hit Yahoo `429`.
   - Later runtime passes rotated to `[^NYA]`, `[^VIX3M]`, `[^FTSE]`, and `[^TNX]`, but those attempts were either skipped by cooldown or failed with Yahoo rate limiting.
3. Backup providers are now behaving more sanely, but they still cannot satisfy the Yahoo-only tail.
   - Finnhub, Tiingo, and Twelve Data kept the main tape symbols fresh.
   - Macro symbols and world-index symbols remained unresolved because backup providers correctly filter out that dedicated Yahoo-only set.
4. Current root cause hypothesis for `UX-044` is now narrower:
   - the issue is no longer "queue rotation" or "Twelve Data credit blow-up"
   - it is now "dedicated Yahoo-only tail remains unresolved because Yahoo still rate-limits the single-symbol lane, and no alternate source currently covers that symbol class"

Human-visible UX findings from this run:

1. `UX-031` remains open and severe.
   - Config first-paint corruption is still obvious on both tabs (`config-tab-001-General.png`, `config-tab-002-Advanced.png`).
2. `UX-032` remains open.
   - Ghost duplicate-Validate appearance is still visible in the footer startup capture.
3. `UX-033` remains open.
   - Advanced-tab explanatory copy and grid headers still render garbled or clipped.
4. `UX-043` improved but remains open.
   - The lower Global Markets lane is much more usable spatially, but the content still reads like a large grouped block rather than a light moving exchange lane.
5. `UX-048` moves from "new/open" to "retest after future layout changes".
   - This pass did not reproduce the earlier graph-vs-Global-Markets collision in the sampled captures.
6. `UX-050` remains open.
   - The clocks still visibly flicker/corrupt in the VM even after the macro collection rebuild was removed, so additional analysis is still required around status-bar invalidation and overlapping text redraw.
7. `UX-051` remains open.
   - The macro/yield-spread contract is still incomplete because the model still lacks a dedicated `US2Y` input.

Priority order for the next code/debug cycle:

1. `UX-044` Add a more productive dedicated-Yahoo retry/caching strategy for macro/world-index symbols so the Yahoo-only tail can populate.
2. `UX-050` Continue deep analysis of top-band/clock flicker now that macro collection churn has been reduced.
3. `UX-051` Add `US2Y` and correct the yield-spread macro model.
4. `UX-043` Continue slimming/reformatting Global Markets now that lane reservation is in place.
5. `UX-031` to `UX-033` Fix configurator first-paint/footer ghosting using the latest captures as the baseline.

## 20) VM Validation Update (2026-04-19 morning)

Validation target:

- Fresh local code after:
  - conservative Yahoo-internal quote-endpoint fallback for direct index-symbol rate limiting
  - clock/status ancillary redraw throttling
  - `US2Y` macro slot and 10Y-2Y spread model update
- Publish source: `build/artifacts/publish-safe-temp`
- Fresh VM run: `build/vm/artifacts/vm-results/ux-deep-20260419-041532`
- Fresh trace bundle: `build/vm/artifacts/trace/ux-deep-20260419-041532-trace`

Local verification:

1. Focused validation slice passed: `80/80`.
2. Full suite passed: `150/150`.

Important workflow finding:

1. The combined host launcher path was still capable of reusing a stale guest payload after a fresh publish.
2. The reliable path for this run was:
   - publish on host with `publish-safe-temp`
   - run `Guest-PrepareVmUxFromShare.ps1` directly
   - verify a brand-new staged manifest under `build/vm/artifacts/staged-builds`
   - only then run `Guest-UxDeepExercise.ps1`
3. Fresh staging proof for this run:
   - `build/vm/artifacts/staged-builds/staged-build-20260419-041506.json`
   - screensaver length `71921039`
   - config length `71838016`

VM run status:

1. `ConfigPhaseStatus=Completed`
2. `ScreensaverPhaseStatus=Completed`
3. `ConfigVersionCheck=Passed`
4. `ScreensaverVersionCheck=Passed`

Confirmed improvements from this pass:

1. Fresh build execution is now confirmed end-to-end.
   - Config screenshots show the new symbol-name guidance text.
   - Screensaver screenshots show `US2Y` in the macro strip.
   - Trace lines now reference `US2Y` instead of the old `^FVX` placeholder model.
2. `UX-051` is partially implemented in code and verified in runtime wiring.
   - Macro strip now includes `US2Y`.
   - Yield spread model now expects 10Y-2Y inputs.
3. `UX-050` improved technically.
   - The per-second clock loop no longer rebuilds the floating exchange card details and status ancillary text every tick.
   - In sampled frames from the fresh run, the top-right clock text is visually cleaner than the earlier smeared captures.
4. `UX-043` lower-lane visibility remains improved.
   - Global Markets starts low and stays visible above the news band.

Trace-backed runtime findings:

1. The fresh run is definitely on the new payload.
   - `event=MacroSnapshot` now shows `meters=[VIX:--:, VIX3M:--:, US2Y:--:, UST10Y:--:, YLD SPRD:--:pts, DXY:--:]`
   - `event=WarmupPlan` now includes `US2Y`
2. `UX-044` remains the main blocker.
   - Macro symbols still remained unresolved for the full 6-minute run.
   - `ClockMarketDataSummary` still reports `populated_exchange_count=0`.
3. Dedicated Yahoo rotation continues to advance.
   - Fresh trace shows runtime attempts across `^VIX`, `^NYA`, `^VIX3M`, `^FTSE`, and `US2Y`.
4. Yahoo throttling is still the decisive runtime bottleneck.
   - `^VIX` and `^VIX3M` still hit Yahoo `429`.
   - `US2Y` reached the dedicated queue and was then skipped by cooldown before a productive fetch occurred.
5. Main ETF/equity fallback remains healthy.
   - Finnhub, Twelve Data, and Tiingo kept the tapes live with 36 resolved quotes while the Yahoo-only tail stayed empty.

Human-visible UX findings from this run:

1. `UX-031` remains open and severe.
   - Config first-paint corruption is still obvious in `config-tab-001-General.png`.
2. `UX-032` remains open.
   - The duplicate-Validate footer appearance is still present.
3. `UX-033` remains open.
   - Advanced-tab explanatory text/grid headers are still garbled in `config-tab-002-Advanced.png`.
4. `UX-035` remains open.
   - Macro chips are still empty even though the strip now includes `US2Y`.
5. `UX-044` remains open.
   - Global Markets cards still show no populated exchange index values or sparklines.
6. `UX-050` is improved but not closed.
   - The sampled fresh frames do not show the same degree of top-right clock smear as before, but live flicker still needs more reduction and confirmation.
7. `UX-051` remains partially open.
   - The semantic model is corrected in code, but no live `US2Y` quote is arriving yet.

Priority order for the next code/debug cycle:

1. `UX-044` Make the dedicated Yahoo-only tail productive under current throttling.
2. `UX-050` Continue reducing residual clock/macro-band flicker now that redraw cadence is improved.
3. `UX-051` Make `US2Y` actually populate, not just exist in the model.
4. `UX-031` to `UX-033` Fix configurator first-paint/footer corruption.
5. `UX-043` Continue slimming Global Markets once it has real data to render.

## 21) VM Validation Update (2026-04-19 midday)

Validation target:

- Fresh local code after a new dedicated-Yahoo provider patch:
  - dedicated macro/world-index symbols now prefer Yahoo quote endpoint before chart fallback
  - `US2Y` is explicitly in that quote-first lane
- Publish source: `build/artifacts/publish-safe-temp`
- Fresh VM run: `build/vm/artifacts/vm-results/ux-deep-20260419-080346`
- Fresh trace bundle: `build/vm/artifacts/trace/ux-deep-20260419-080346-trace`

Local verification:

1. Focused validation slice passed: `52/52`.
2. Full suite passed: `153/153`.
3. Safe publish completed successfully before VM staging.

VM run status:

1. `ConfigPhaseStatus=Completed`
2. `ScreensaverPhaseStatus=Completed`
3. `ConfigVersionCheck=Passed`
4. `ScreensaverVersionCheck=Passed`

What this run proved:

1. The latest quote-first Yahoo patch really ran in the VM.
   - Fresh staged manifest: `build/vm/artifacts/staged-builds/staged-build-20260419-075924.json`
   - It confirms `publish-safe-temp` and `0.9.0-beta5.3.2 (BETA-5.3.2)` on both apps.
2. Dedicated Yahoo queue advancement still works.
   - Warmup started with `[^VIX]`.
   - Runtime later advanced to `[^NYA]`, `[^VIX3M]`, `[^FTSE]`, `US2Y`, and then `[^N225]`.
3. Main ETF/equity fallback remains healthy.
   - The saver again reached `36` populated non-macro quotes through Finnhub/Tiingo/Twelve Data fallback.
4. Global Markets lane positioning remains improved.
   - It stays visible low on screen above the news band in this run.

Trace-backed runtime findings:

1. The quote-first Yahoo patch did not solve `UX-044`.
   - The dedicated symbols now hit Yahoo quote endpoint first, but those single-symbol quote requests are still being rate-limited.
   - Examples from the reconstructed trace:
   - `[^VIX]` hit `Quote endpoint preferred batch rate-limited`
   - `[^NYA]` hit the same
   - `[^VIX3M]` hit the same
   - `US2Y` hit the same
2. Macro strip stayed empty for the full run.
   - Repeated `event=MacroSnapshot` lines still show `VIX`, `VIX3M`, `US2Y`, `UST10Y`, `YLD SPRD`, and `DXY` as `--`.
3. Global Markets exchange indices stayed empty for the full run.
   - Repeated `event=ClockMarketDataSummary` lines still show `populated_exchange_count=0`.
4. The current bottleneck is now clearer:
   - queue rotation is working
   - quote-endpoint-first routing is working
   - Yahoo still rate-limits the entire dedicated symbol class under current live behavior, so no productive macro/world-index fetch lands on screen
5. Twelve Data pacing remains materially improved.
   - This run did not reproduce the earlier live `9 credits used with limit 8` failure.

Human-visible UX findings from this run:

1. `UX-031` remains open and severe.
   - `config-tab-001-General.png` still shows strong first-paint corruption and text ghosting.
2. `UX-032` remains open.
   - The footer still visibly presents a duplicate/ghost Validate appearance.
3. `UX-033` remains open.
   - `config-tab-002-Advanced.png` still shows clipped or corrupted explanatory text and column headers.
4. `UX-035` remains open.
   - Macro chips are still empty even though the strip now includes `US2Y`.
5. `UX-044` remains the primary blocker.
   - Global Markets cards still show no populated exchange index values or sparklines.
6. `UX-050` remains open.
   - Clock stability is improved versus earlier smeared runs, but the top band and exchange clocks still need flicker reduction.
7. `UX-043` remains open.
   - The Global Markets lane is spatially better, but still reads like a grouped block rather than a lighter moving exchange lane.

Priority order for the next code/debug cycle:

1. `UX-044` Add a dedicated-Yahoo strategy that can actually land macro/world-index data under current Yahoo `429` behavior.
   - Queue rotation and quote-first routing are now proven.
   - The next fix must change productivity, not just ordering.
2. `UX-050` Continue reducing residual clock/macro-band flicker.
3. `UX-031` to `UX-033` Fix configurator first-paint/footer corruption once the data lane is no longer blocking UX interpretation.
4. `UX-043` Continue slimming Global Markets after real exchange data becomes available.

## 22) VM UX + Trace Validation (2026-04-20, Full 20-Minute Screensaver Run)

Validation target:

- Current local working tree published via `build/artifacts/publish-safe-temp`
- Full guest deep-UX run with:
  - `ScreensaverDurationMinutes=20`
  - `CaptureIntervalSeconds=5`
- Result bundle: `build/vm/artifacts/vm-results/ux-deep-20260420-175642`
- Trace bundle: `build/vm/artifacts/trace/ux-deep-20260420-175642-trace`
- Reconstructed trace: `build/vm/artifacts/trace/ux-deep-20260420-175642-trace/trace.reconstructed.log`

Run summary:

1. `ConfigPhaseStatus=Completed`
2. `ScreensaverPhaseStatus=Completed`
3. `ConfigVersionCheck=Passed`
4. `ScreensaverVersionCheck=Passed`
5. Planned saver duration completed:
   - `PlannedScreensaverDurationMinutes=20`
   - `ScreensaverShots=240`
6. Config exercise completed:
   - `ConfigShots=232`

Representative captures:

1. Config first paint:
   - `build/vm/artifacts/vm-results/ux-deep-20260420-175642/config-002-001-button-Minimize.png`
2. Config later-in-session repaint state:
   - `build/vm/artifacts/vm-results/ux-deep-20260420-175642/config-002-150-edit-unnamed.png`
3. Screensaver early:
   - `build/vm/artifacts/vm-results/ux-deep-20260420-175642/screensaver-001.png`
4. Screensaver mid-run:
   - `build/vm/artifacts/vm-results/ux-deep-20260420-175642/screensaver-120.png`
5. Screensaver final frame:
   - `build/vm/artifacts/vm-results/ux-deep-20260420-175642/screensaver-240.png`

Trace-backed mapping to existing known issues:

1. `UX-031` remains open and severe.
   - Config first paint is still badly corrupted on launch.
   - Labels, helper text, and footer text fragment into overlapping glyph runs in `config-002-001-button-Minimize.png`.
   - Important nuance from this long run: the configurator becomes materially more readable after repaint/scroll/exercise, as seen in `config-002-150-edit-unnamed.png`, so the defect is increasingly isolated to startup/first-paint behavior rather than permanent layout failure.
2. `UX-032` remains open.
   - The duplicate/ghost Validate affordance is still visible in both the startup and later config captures.
3. `UX-033` remains open.
   - This pass exercised all tabs, but the first-paint corruption still prevents closing the advanced-tab readability issue with confidence.
4. `UX-035` remains open, but the scope is now narrower.
   - Treasury-backed macros are now healthy for the full 20-minute run:
     - repeated trace lines show `UST2M:3.73`, `UST10Y:4.26`, `YLD SPRD:+0.53`
   - The unresolved macro tail is now specifically `^VIX`, `^VIX3M`, and `DX-Y.NYB`.
5. `UX-036` was not reproduced in this run.
   - The saver remained active for the full planned 20-minute window and wrote all `240` capture frames.
6. `UX-037` was not reproduced in this run.
   - The result bundle stayed on the saver; the desktop/cmd foreground contamination seen in earlier cycles did not recur here.
7. `UX-043` remains open.
   - The Global Markets surface sits slightly higher and is no longer buried by the news band, but it still reads like a grouped block rather than a lighter moving exchange lane.
8. `UX-044` remains the main runtime blocker.
   - `ClockMarketDataSummary` stays at `populated_exchange_count=0` for the full 20-minute run.
   - World-index symbols remain unresolved throughout:
     - `^NYA`, `^FTSE`, `^N225`, `^SSEC`, `^HSI`, `^NSEI`, `^GDAXI`, `^FCHI`, and peers.
9. `UX-047` remains relevant, although it presents differently now.
   - This run did not reproduce the older explicit Twelve Data minute-credit exception.
   - However, once the run reaches the world-index tail, Twelve Data and Tiingo are still repeatedly skipped by budget/reuse and never become productive for Global Markets recovery.
10. `UX-050` remains open.
   - Static screenshots cannot prove or disprove flicker, but the top band is still under heavy redraw pressure and the clocks remain adjacent to unresolved macro content.
   - Keep this open until a motion-focused pass confirms the clock regions no longer flicker.
11. `UX-051` is partially resolved in behavior and terminology.
   - The product now shows a meaningful Treasury spread again via `UST2M`, `UST10Y`, and a computed `YLD SPRD`.
   - The old unresolved blocker has narrowed from "yield spread model is wrong" to "macro tail is still incomplete because `VIX`, `VIX3M`, and `DXY` never populate."

Trace-confirmed runtime findings from the full 20-minute run:

1. Warmup is now intentionally narrow for Yahoo dedicated symbols.
   - `WarmupPlan` shows only `DX-Y.NYB`.
2. `DX-Y.NYB` still fails immediately in warmup.
   - Yahoo returns `429` on the first dedicated warmup attempt.
3. The new official macro source is architecturally present but not productive yet.
   - `OfficialMacroProviderFailed` repeats through the run because `^VIX` and `^VIX3M` time out under the current shared `HttpClient.Timeout of 10 seconds`.
4. Treasury macro sourcing is the clear success story from this pass.
   - `TreasuryMacroQuotesApplied` repeats successfully through the run, and the on-screen meters match the trace.
5. Dedicated Yahoo queue rotation is still working.
   - Over the long run, Yahoo attempts rotate across world-index symbols instead of sticking on one entry.
   - Examples observed in trace: `^NYA`, `^FTSE`, `^N225`, `^KS11`, `^AXJO`, `^SSEC`.
6. The long-run failure mode is now very specific:
   - official VIX/VIX3M source times out
   - Yahoo `DX-Y.NYB` and world indices are rate-limited
   - fallback providers keep the ETF/equity lanes alive but do not recover Global Markets

Human-visible UX findings from the long run:

1. Main tape population is healthy for the full session.
   - By mid-run and late-run frames, the tapes are visibly live and diverse.
2. Treasury macro chips are readable and stable.
   - `UST2M`, `UST10Y`, and `YLD SPRD` stay populated from early runtime through the final frame.
3. `VIX`, `VIX3M`, and `DXY` remain blank the entire session.
   - This matches the trace and is plainly visible in `screensaver-120.png` and `screensaver-240.png`.
4. Global Markets cards still show clocks/weather/market-status scaffolding but no usable index values.
   - This remains visible through the final frame.
5. Config startup no longer leaks provider secrets in clear text.
   - Keys are masked in the corrupted first-paint frame, which confirms the earlier safety fix is still holding.

New issues logged from this 20-minute pass:

1. `UX-052` Official macro-source timeout for `^VIX`/`^VIX3M`.
   - The new official provider path is not landing any values in runtime because its calls repeatedly time out at the current shared `10s` HTTP timeout.
   - This is now a distinct blocker inside the broader `UX-044` data-path problem.
2. `UX-053` Floating graph cards still intrude into active ticker lanes during normal runtime.
   - In `screensaver-120.png` and `screensaver-240.png`, multiple graph cards overlap or visually crowd the ticker tapes, obscuring ticker/value readability even when Global Markets is lower on screen.

What this long run de-risked:

1. The saver can now stay alive and capture cleanly for a full 20-minute UX cycle.
2. Treasury-based macro yields are fixed enough to treat as a stable baseline.
3. The unresolved macro/global-market problem is no longer vague.
   - It is now concentrated in:
     - official macro provider timeout (`UX-052`)
     - Yahoo rate-limited `DX-Y.NYB`
     - unresolved world-index tail feeding `UX-044`

Priority order after this 20-minute run:

1. `UX-052` Make the official macro provider productive for `^VIX` and `^VIX3M`.
   - Increase or decouple timeout and confirm the official path can actually land values in the VM.
2. `UX-044` Restore real Global Markets population with a provider strategy that does not depend on Yahoo succeeding for every world index.
3. `UX-047` Keep alias/world-index fallback from being starved by shared provider budgets.
4. `UX-053` Add collision avoidance between floating graph cards and active ticker lanes.
5. `UX-031` to `UX-033` Revisit the config corruption cycle after the data lane is less noisy.

### 22.1 Code Response Prepared (Pending VM Validation)

Code changes prepared after the 20-minute run:

1. `UX-052` official macro source changed from FRED text downloads to Cboe official volatility-index CSV history.
   - New provider: `src/PortfolioSaver.Data/Providers/CboeVolatilityIndexQuoteProvider.cs`
   - `^VIX` maps to Cboe `VIX_History.csv`
   - `^VIX3M` maps to Cboe `VIX3M_History.csv`
   - Rationale: local endpoint testing showed the FRED text/graph endpoints timing out at `20s`, while the Cboe CSV endpoints returned in roughly `1-2s`.
2. Official macro provider label changed from `FRED` to `Cboe`.
   - Trace event names remain `OfficialMacroQuotesApplied` / `OfficialMacroProviderFailed`.
3. `UX-044` / `UX-047` alias-prefetch diagnostics improved.
   - Added trace events:
     - `AliasQuotesPlan`
     - `AliasQuotesApplied`
     - `AliasQuotesNoData`
     - `AliasQuotesFailed`
     - `AliasQuotesSkippedByBudget`
   - Each event includes requested Yahoo-style symbols and mapped provider-native symbols.
4. Twelve Data alias-prefetch request size was reduced from the general sequential pass size to a dedicated two-symbol alias batch.
   - Goal: give world-index aliases an earlier, clearer chance without spending the whole normal fallback minute budget.

Local validation completed after these code changes:

1. Focused provider/coordinator/render slice passed: `69/69`.
2. Full test suite passed: `160/160`.
3. Safe publish completed successfully to `build/artifacts/publish-safe-temp`.

Next VM validation requirement:

1. Stage the latest `publish-safe-temp` output into the VM.
2. Run at least one 6-minute UX/trace cycle first.
3. Confirm whether:
   - `OfficialMacroQuotesApplied` appears for `^VIX` and `^VIX3M`
   - `VIX` and `VIX3M` become populated on screen
   - alias-prefetch emits `AliasQuotesApplied`, `AliasQuotesNoData`, or `AliasQuotesSkippedByBudget`
   - any Global Markets exchange card receives index values
4. If the 6-minute pass is promising, follow with another 20-minute run.

### 22.2 VM Validation Result (2026-04-21, Cboe Macro Provider Pass)

Validation target:

- Latest `publish-safe-temp` after the Cboe volatility provider and alias-prefetch trace changes.
- VM run: `build/vm/artifacts/vm-results/ux-deep-20260421-073531`
- Trace bundle: `build/vm/artifacts/trace/ux-deep-20260421-073531-trace`
- Reconstructed trace: `build/vm/artifacts/trace/ux-deep-20260421-073531-trace/trace.reconstructed.log`

Run summary:

1. `ConfigPhaseStatus=Completed`
2. `ScreensaverPhaseStatus=Completed`
3. `ConfigVersionCheck=Passed`
4. `ScreensaverVersionCheck=Passed`
5. Planned saver duration completed:
   - `PlannedScreensaverDurationMinutes=6`
   - `ScreensaverShots=72`

Confirmed improvements:

1. `UX-052` is fixed in the 6-minute VM pass.
   - `OfficialMacroQuotesApplied` appears repeatedly.
   - `^VIX` and `^VIX3M` are fetched through the Cboe provider.
   - Macro snapshots now show:
     - `VIX:18.87:+8.0%`
     - `VIX3M:21.24:+3.6%`
     - `UST2M:3.72:-0.3%`
     - `UST10Y:4.26:+0.0%`
     - `YLD SPRD:+0.54:pts`
   - Representative frame: `build/vm/artifacts/vm-results/ux-deep-20260421-073531/screensaver-001.png`
2. The provider label now reflects useful macro sourcing.
   - Example: `Provider: Cboe + Finnhub + Tiingo + US Treasury + Cache (Partial), XLY Updated 0s ago`
3. The run confirmed the Cboe path is materially faster and more reliable than the FRED path in this environment.

Remaining blockers:

1. `UX-044` remains open.
   - `ClockMarketDataSummary` still reports `populated_exchange_count=0` throughout the 6-minute run.
   - Global Markets exchange cards still show clocks/weather/status scaffolding but no usable index values or sparklines.
2. `DX-Y.NYB` remains unresolved.
   - Warmup still targets `DX-Y.NYB`, but Yahoo returns `429`.
   - Macro snapshots now only list `DX-Y.NYB` as missing.
3. Alias-prefetch is now observable but not productive.
   - `AliasQuotesPlan` appears with provider-native Twelve Data mappings, for example:
     - `^NYA -> NYA`
     - `^FTSE -> FTSE`
     - `^N225 -> N225`
     - `^SSEC -> SSEC`
     - `^HSI -> HSI`
     - `^NSEI -> NSEI`
   - Each attempted pair returned `AliasQuotesNoData`.
   - This proves the alias lane is running, but the current Twelve Data aliases are not sufficient for index population.
4. `UX-053` remains open.
   - Later frames still show graph cards visually crowding or crossing active tape lanes.

New issue logged from this VM pass:

1. `UX-054` Twelve Data world-index aliases are provider-invalid or insufficient.
   - The new trace distinguishes this from budget starvation.
   - Current aliases are sent, but Twelve Data returns no data for the tested pairs.
   - Next work should replace these aliases with verified provider-native symbols or move Global Markets index sourcing to a different provider/source.

Priority order after this pass:

1. `UX-054` Find verified provider-native world-index symbols/source paths for Global Markets.
2. `UX-044` Populate Global Markets index values using the verified source path.
3. `DX-Y.NYB` Decide whether to continue Yahoo-only DXY retrieval, source DXY from another provider, or display a cached/last-close fallback.
4. `UX-053` Add graph-card collision avoidance against active ticker lanes.
5. Run a follow-up 20-minute VM pass only after at least one Global Markets index source is productive.

### 22.3 Provider-Native Global Markets Research and Patch (2026-04-21)

Research summary:

1. Twelve Data confirms that its platform supports global indices and documents examples such as `FTSE`, `FCHI`, `N225`, and `KS11`.
2. Twelve Data reference probing without an API key confirmed these reference symbols:
   - `FTSE` -> FTSE 100 / LSE / United Kingdom
   - `N225` -> Nikkei 225 / JPX / Japan
   - `HSI` -> HANG SENG INDEX / HKEX / Hong Kong
   - `NSEI` -> NIFTY 50 / NSE / India
   - `GDAXI` -> DAX PERFORMANCE-INDEX / XETR / Germany
   - `FCHI` -> CAC 40 / Euronext / France
   - `GSPTSE` -> S&P/TSX Composite index / TSX / Canada
   - `KS11` -> KOSPI Composite Index / KRX / South Korea
   - `AXJO` -> S&P/ASX 200 Index / ASX / Australia
3. Twelve Data reference probing did not confirm `NYA`, `SSEC`, `DXY`, `DJI`, `SPX`, or `IXIC` as index reference symbols.
4. Live Twelve Data quote probing with the local configured key showed that reference availability does not guarantee runtime quote availability on the current plan.
   - `FTSE` returned a plan-gating error.
   - Several other index symbols returned invalid-symbol responses.
   - The current Basic minute quota is 8 credits/minute, so alias probing must remain extremely conservative.
5. Stooq no-key CSV quote probing confirmed productive provider-native symbols for a useful subset:
   - `DX-Y.NYB` -> `dx.f`
   - `^FTSE` -> `^ukx`
   - `^N225` -> `^nkx`
   - `^SSEC` -> `^shc`
   - `^HSI` -> `^hsi`
   - `^GDAXI` -> `^dax`
   - `^FCHI` -> `^cac`
   - `^GSPTSE` -> `^tsx`
   - `^KS11` -> `^kospi`
6. Stooq probing did not confirm productive symbols for:
   - `^NYA`
   - `^NSEI`
   - `^AXJO`

Patch summary:

1. `UX-032` Config footer ghost/junk near `Validate` patched.
   - Removed the WPF `Menu` from the footer action strip.
   - The footer now has exactly one visible primary button.
   - Status text is single-line with ellipsis trimming, preventing it from wrapping into the button area.
2. `UX-054` Twelve Data alias strategy patched.
   - Removed the old caret-strip alias rule because VM trace proved it was not productive.
   - Twelve Data no longer receives Yahoo-style world index aliases through the dedicated alias prefetch lane.
3. `UX-044` Global Markets acquisition patched with a new Stooq global-market source.
   - New provider: `StooqGlobalMarketQuoteProvider`.
   - The provider returns quotes under the original app canonical symbols so existing Global Markets binding continues to work.
   - It is invoked before the normal fallback provider loop, reducing Yahoo pressure for supported global index symbols and DXY.
4. Current expected Global Markets behavior before VM verification:
   - DXY should populate from Stooq instead of Yahoo.
   - London, Tokyo, Shanghai, Hong Kong, Frankfurt, Paris, Toronto, and Seoul should have a realistic no-key quote path.
   - New York, Mumbai, and Sydney still need provider-native source decisions unless a later provider path resolves them.

Local validation:

1. Focused config/provider/coordinator tests passed: `57/57`.
2. Full test suite passed: `163/163`.
3. Note on host build workflow:
   - Plain `dotnet` from the repo root is currently blocked because `global.json` pins SDK `8.0.420`, while this host exposes SDK `10.0.201`.
   - Non-invasive test workaround used successfully: run `dotnet test` from a temp directory while passing the absolute test project path.
   - This does not modify `global.json` and keeps the Visual Studio/pinned SDK policy intact.

Next VM validation requirement:

1. Publish/stage the latest build.
2. Run a 6-minute VM UX/trace cycle.
3. Confirm trace events:
   - `GlobalMarketQuotesApplied`
   - fetched symbols include some of `DX-Y.NYB`, `^FTSE`, `^N225`, `^SSEC`, `^HSI`, `^GDAXI`, `^FCHI`, `^GSPTSE`, `^KS11`
   - `ClockMarketDataSummary` reports `populated_exchange_count > 0`
4. Confirm visually:
   - Config footer has one clean `Validate` button and no ghost/junk button.
   - DXY macro is populated.
   - At least several Global Markets cards show index values.

### 22.4 VM Validation Result (2026-04-21, Stooq Global Markets Pass)

Validation target:

- Latest `publish-safe-temp` after the Stooq global-market provider and footer simplification.
- VM run: `build/vm/artifacts/vm-results/ux-deep-20260421-081114`
- Trace bundle: `build/vm/artifacts/trace/ux-deep-20260421-081114-trace`
- Reconstructed trace: `build/vm/artifacts/trace/ux-deep-20260421-081114-trace/trace.reconstructed.log`
- Live screenshot confirming the productive runtime state: `build/vm/artifacts/host-runs/live-status-121329.png`

Run summary:

1. `ConfigPhaseStatus=Completed`
2. `ScreensaverPhaseStatus=Completed`
3. `ConfigVersionCheck=Passed`
4. `ScreensaverVersionCheck=Passed`
5. Planned saver duration completed:
   - `PlannedScreensaverDurationMinutes=6`
   - `ScreensaverShots=72`

Confirmed improvements:

1. `UX-044` is substantially improved.
   - `GlobalMarketQuotesApplied` appears repeatedly.
   - Stooq fetched 9 canonical symbols per pass.
   - Trace examples show fetched symbols including `^FTSE`, `^N225`, `^SSEC`, `^HSI`, `^GDAXI`, `^FCHI`, `^GSPTSE`, and `^KS11`.
2. Global Markets now displays data.
   - `ClockMarketDataSummary` moved from `populated_exchange_count=0` to `populated_exchange_count=8`.
   - Remaining missing exchange symbols are now narrowed to:
     - `^NYA`
     - `^NSEI`
     - `^AXJO`
3. Macro population is now complete in this VM run.
   - `MacroSnapshot` shows:
     - `VIX:18.87:+8.0%`
     - `VIX3M:21.24:+3.6%`
     - `UST2M:3.72:-0.3%`
     - `UST10Y:4.26:+0.0%`
     - `YLD SPRD:+0.54:pts`
     - `DXY:98.10:+0.2%`
   - `macro_missing_symbols=[]`
4. DXY no longer depends on Yahoo.
   - Yahoo still returns `429` for `DX-Y.NYB` during warmup.
   - Stooq then supplies DXY through provider-native `dx.f`, and the UI macro chip becomes populated.

Remaining blockers:

1. `UX-044` remains open only for the final three Global Markets cards.
   - New York / `^NYA`
   - Mumbai / `^NSEI`
   - Sydney / `^AXJO`
2. Yahoo still rate-limits the residual Yahoo fallback lane.
   - Later attempts against `^NYA`, `^NSEI`, and `^AXJO` still return `429`.
   - This is acceptable for now because the blast radius is much smaller, but these three symbols need provider-native alternatives.
3. Config first-paint/text corruption remains broader than the old duplicate-Validate footer.
   - The footer `Menu` ghost was removed in code, but VM captures still show text corruption across labels, tables, and the footer.
   - New mitigation added after this run: force software rendering for `PortfolioSaver.Config` on every launch.

Next validation requirement:

1. Publish/stage the new Config software-rendering build.
2. Run a short config-only or full 6-minute VM pass.
3. Confirm trace includes `Config.App | Software rendering enabled.`
4. Confirm the config startup screenshot no longer shows corrupted labels or footer junk.
5. Continue source research for the three residual world index symbols.

Follow-up verification:

1. Safe publish completed after the Config software-rendering patch.
2. VM staging completed:
   - `build/vm/artifacts/staged-builds/staged-build-20260421-082731.json`
3. Manual config launch screenshot:
   - `build/vm/artifacts/host-runs/config-software-render-verify-2.png`
4. Visual result:
   - The first visible config screen is now substantially clean.
   - API keys remain masked.
   - Footer shows one `Validate` button.
   - The old duplicate/ghost button near `Validate` is gone.
5. Residual visual item:
   - Very faint title/header ghosting is still visible near the top of the config content area.
   - This is much less severe than the previous full-form text corruption and should be tracked as a residual polish issue, not as the old blocker.

### 22.5 Residual Global Markets Source Strategy (2026-04-21)

Follow-up research for the final three missing Global Markets symbols found no reliable no-key exact quote path for:

1. `^NYA` / NYSE Composite
2. `^NSEI` / NIFTY 50
3. `^AXJO` / S&P/ASX 200

Provider probing notes:

1. Stooq exact probes returned `N/D` for `^NYA`, `^NSEI`, `^AXJO`, `^XJO`, and common NIFTY/Sensex spellings.
2. NSE's public index API rejected unauthenticated automated access with `403` in this environment.
3. The ASX/Markit probe used in this pass did not expose a simple public quote endpoint for `XJO`.
4. Financial Modeling Prep has an all-index-quotes API, but the current local test profile has no FMP key configured, so it is not a dependable default path.

Patch decision:

1. Keep exact indices where no-key provider-native data exists.
2. For the unsupported final three, switch the visible Global Markets labels to transparent provider-native benchmarks/proxies:
   - New York: `S&P 500` / `^SPX`
   - Mumbai: `India 50 ETF` / `INDY.US`
   - Sydney: `MSCI Australia ETF` / `EWA.US`
3. This is intentionally explicit in the UI labels; the app no longer displays a proxy value under an exact-index name.

Expected validation:

1. Stooq should resolve all 11 Global Markets cards.
2. `ClockMarketDataSummary` should report `populated_exchange_count=11`.
3. `world_index_missing_symbols=[]`.

### 22.6 VM Validation Result and Layout Polish Patch (2026-04-21)

Validation target:

- Latest `publish-safe-temp` after residual Global Markets source substitutions and config first-paint mitigation.
- VM run: `build/vm/artifacts/vm-results/ux-deep-20260421-101407`
- Trace bundle: `build/vm/artifacts/trace/ux-deep-20260421-101407-trace`
- Reconstructed trace: `build/vm/artifacts/trace/ux-deep-20260421-101407-trace/trace.reconstructed.log`
- Manual host monitor screenshots:
  - `build/vm/artifacts/host-runs/manual-20min-launch-20260421-141358-plus10s.png`
  - `build/vm/artifacts/host-runs/manual-20min-live-141738.png`
  - `build/vm/artifacts/host-runs/manual-20min-live-142205.png`
  - `build/vm/artifacts/host-runs/manual-20min-live-142640.png`
  - `build/vm/artifacts/host-runs/manual-20min-live-143105.png`
  - `build/vm/artifacts/host-runs/manual-20min-live-143556.png`

Run summary:

1. `ConfigPhaseStatus=Completed`
2. `ScreensaverPhaseStatus=Completed`
3. `ConfigVersionCheck=Passed`
4. `ScreensaverVersionCheck=Passed`
5. Planned saver duration completed:
   - `PlannedScreensaverDurationMinutes=20`
   - `CaptureIntervalSeconds=5`
   - `ScreensaverShots=240`
   - `ConfigShots=232`
6. Version text observed: `0.9.0-beta5.3.2`

Confirmed improvements:

1. `UX-044` is now validated as closed for the current no-key provider strategy.
   - `ClockMarketDataSummary` repeatedly reports `populated_exchange_count=11`.
   - `world_index_missing_symbols=[]` throughout the productive runtime state.
   - `GlobalMarketQuotesApplied` repeatedly fetches Stooq-backed symbols including `^SPX`, `^FTSE`, `^N225`, `^SSEC`, `^HSI`, `INDY.US`, `^GDAXI`, `^FCHI`, `^GSPTSE`, `^KS11`, `EWA.US`, and `DX-Y.NYB` in rotating subsets.
2. Macro population remains healthy.
   - `MacroSnapshot` repeatedly reports values for `VIX`, `VIX3M`, `UST2M`, `UST10Y`, `YLD SPRD`, and `DXY`.
   - `macro_missing_symbols=[]` after the initial startup window.
3. Config first-paint corruption is materially improved.
   - The launch-plus-10s screenshot shows the content area on a dark pane with readable text.
   - The footer remains clean with one visible `Validate` button.
4. Provider status is now credible.
   - Trace examples include provider labels such as `Cboe + Stooq + US Treasury + Cache (Partial), DX-Y.NYB Updated 0s ago`.
   - Values are treated as current when refreshed, even when cache is also present.

New / remaining visual issues from this pass:

1. `UX-053` remains visually important.
   - Once graph warmup reaches many cards, floating graph cards still crowd the ticker tape area in sampled late-run frames.
   - This is now the biggest screensaver readability issue because the data lane is no longer the blocker.
2. `UX-043` remains open as polish.
   - Global Markets is populated and visible, but the surface still reads too block-like for the desired light exchange-lane concept.
3. `UX-050` needs another visual pass after the populated-data fix.
   - Clock text is more stable than before, but the next VM pass should re-check flicker with populated macro/world data and the slimmer Global Markets geometry.

Patch applied after this validation:

1. `UX-053` graph-card no-fly zone.
   - Floating graph cards now seed and bounce below the active ticker stack instead of sharing the ticker tape lane.
   - A new ticker-tape exclusion bottom is calculated from status height, tape margin, and the estimated tape stack height.
2. `UX-043` Global Markets slimming.
   - The Global Markets/floating clock card is narrower and shorter.
   - Per-exchange cards use smaller margins, padding, and mini-graph height so the surface reads more like a compact lane.

Local validation after the patch:

1. Focused tests passed: `27/27`.
2. Full test suite passed: `167/167`.
3. Host test command used the pinned SDK directly:
   - `<local-dotnet8>\dotnet.exe test .\tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj --no-restore -v minimal`

Next VM validation requirement:

1. Publish/stage the new layout build.
2. Run a 6-minute VM UX/trace cycle first; use 20 minutes only if the 6-minute pass is clean.
3. Confirm visually:
   - Graph cards do not intrude into ticker tape lanes.
   - Global Markets remains above the news ticker and still shows 11/11 populated exchange cards.
   - Main and Global Markets clocks do not show obvious flicker/corruption with populated macro data.
4. Confirm trace remains healthy:
   - `ClockMarketDataSummary / populated_exchange_count=11`
   - `macro_missing_symbols=[]`
   - `world_index_missing_symbols=[]`

### 22.7 Visual Confirmation Pass (2026-04-21, `ad88bae`)

Validation target:

- Commit under test: `ad88bae fix: complete global markets population and layout polish`
- Publish source: `build/artifacts/publish-safe-temp`
- VM staging manifest: `build/vm/artifacts/staged-builds/staged-build-20260421-105441.json`
- Visual result bundle: `build/vm/artifacts/visual-confirm-results/visual-confirm-20260421-105630`
- Summary: `build/vm/artifacts/visual-confirm-results/visual-confirm-20260421-105630/visual-confirm-summary.json`

Run summary:

1. Config window title detected: `DO NOT PANIC PORTFOLIO VISUALIZER Config - BETA-5`.
2. Config `General` tab selected successfully.
3. Config `Advanced` tab selected successfully.
4. Screensaver version text detected: `0.9.0-beta5.3.2`.
5. Screensaver did not exit early.
6. Captures:
   - Config shots: 6
   - Screensaver shots: 21
   - Screensaver capture interval: 10 seconds

Confirmed visual improvements:

1. Config `General` first-paint/repaint is clean.
   - `config-002-general-after-first-paint.png` shows masked API-key fields, readable text, one `Validate` button, and no old footer ghost/junk.
2. Config `Advanced` first-paint/repaint is stable enough to inspect.
   - `config-004-advanced-immediate.png` and `config-006-advanced-after-repaint.png` show no severe random text shredding.
3. Global Markets is visible and populated in the screensaver.
   - `screensaver-global-003.png`, `screensaver-global-012.png`, and `screensaver-global-021.png` show the lower Global Markets lane above the news ticker.
   - All exchange cards are visually present, with index values and sparklines visible.
4. The new graph no-fly-zone materially improves ticker readability.
   - In the sampled captures, graph cards no longer sit directly on top of the active ticker tape rows.

New / remaining defects from this pass:

1. `UX-055` new: Advanced data-source table header contrast is poor.
   - The header row is white/light while header text is also very light, making labels such as provider/rate fields difficult to read.
   - Evidence: `config-004-advanced-immediate.png`, `config-006-advanced-after-repaint.png`.
   - Proposed patch: apply an explicit dark `DataGridColumnHeader` style and align table header foreground/background with the rest of the dark config theme.
2. `UX-056` new: floating graph cards overlap each other after many cards warm in.
   - The ticker-lane exclusion helped, but graph cards still pile into overlapping clusters in the graph lane.
   - Evidence: `screensaver-global-012.png` and especially `screensaver-global-021.png`.
   - Proposed patch: add runtime graph-vs-graph separation during motion, or allocate graph cards into explicit non-overlapping rows/slots rather than only resolving initial placement.
3. `UX-043` remains visually improved but still open.
   - Global Markets is slimmer and populated, but it is still a compact block rather than a truly elegant moving exchange strip.
   - Proposed patch: continue reducing per-card width/height and consider a two-row horizontal lane with slower lateral movement.

Next recommended implementation order:

1. Fix `UX-055` table header contrast in the Advanced config tab.
2. Fix `UX-056` graph-card overlap with runtime collision/separation.
3. Re-run the same visual confirmation harness before another long 20-minute pass.

### 22.8 Global Markets Tape Redesign Patch (2026-04-23)

Design correction from review:

1. Graph cards may swim around the entire screen as a background layer.
   - They are allowed behind the heading/status area, ticker tapes, Global Markets tape, and news scroller.
   - The foreground lanes remain readable because their XAML z-order places them above the floating graph canvas.
2. Global Markets should not be a floating block/card.
   - It is now a dedicated tape hosted directly above the Finance News ticker.
   - The tape reuses the existing Global Markets data model, quotes, weather, exchange status, and sparklines.
3. The application name should be visible at top-middle.
   - A top-center `DO NOT PANIC PORTFOLIO VISUALIZER` title badge was added to the screensaver scene.

Implementation notes:

1. Added `GlobalMarketsTapeControl`.
   - Uses the existing `FloatingClockViewModel` / `ClockCityViewModel` data.
   - Scrolls exchange cards left using `TapeAnimationController`.
   - Binds live values in-place and avoids rebuilding animation metrics on every clock/value update.
2. Moved Global Markets hosting from the floating overlay canvas into `BottomOverlayPanel` immediately above `NewsFlasherHost`.
3. Removed Global Markets from floating sprite motion.
4. Relaxed graph-card motion bounds to the full overlay canvas.
5. Dropped seconds from Global Markets exchange clocks.
   - Exchange cards now show `HH:mm`.
   - Local/non-exchange clock formatting can still use `HH:mm:ss`.
   - This reduces visual churn; it is separate from provider request throttling.
6. Follow-up visual cleanup:
   - Increased Global Markets tape height and bottom margin for more vertical breathing room above Finance News.
   - Widened exchange cards and separators to prevent apparent card/label overlap during marquee motion.
   - Dropped seconds from the top-right status clock (`HH:mm`) to reduce once-per-second flicker.

Validation expectation:

1. Global Markets appears as a moving tape above Finance News.
2. Graph cards can pass behind foreground lanes without obscuring their text.
3. Top-center app title is visible.
4. Global Markets exchange clock text should not tick every second.

### 22.9 Deep UX / Trace Validation Pass (2026-04-23)

Validation target:

- Commit under test: `90c086a fix: stabilize global markets tape spacing`
- Publish source: `build/artifacts/publish-safe-temp`
- VM launcher run: `build/vm/artifacts/launcher-runs/20260423-093534`
- VM staging manifest: `build/vm/artifacts/staged-builds/staged-build-20260423-053600.json`
- Deep UX result bundle: `build/vm/artifacts/vm-results/ux-deep-20260423-053641`
- Summary: `build/vm/artifacts/vm-results/ux-deep-20260423-053641/vm-ux-summary.json`
- Runtime trace: `build/vm/artifacts/trace/ux-deep-20260423-053641-trace/trace.circular.log`

Run summary:

1. Config phase completed.
   - Captures: `232`
   - Version check: PASS
   - Evidence: `config-tab-001-General.png`, `config-tab-002-Advanced.png`
2. Screensaver phase completed.
   - Duration: `6` minutes
   - Capture interval: `5` seconds
   - Captures: `72`
   - Version check: PASS (`0.9.0-beta5.3.2`)
   - Evidence: `screensaver-001.png`, `screensaver-036.png`, `screensaver-072.png`
3. Harness/process behavior was clean.
   - No early screensaver exit.
   - Result bundle exported normally.
   - Trace export succeeded after the run.

Confirmed improvements:

1. Config `General` tab no longer leaks visible API key text in the captured fields.
   - API key fields are masked in `config-tab-001-General.png`.
   - Footer has one visible `Validate` button.
2. Config first paint is materially cleaner than the earlier corrupted captures.
   - `config-tab-001-General.png` is readable and does not show the severe startup junk seen in earlier passes.
3. Macro and Global Markets data are no longer blocked.
   - Trace shows `MacroSnapshot` with populated `VIX`, `VIX3M`, `UST2M`, `UST10Y`, `YLD SPRD`, and `DXY`.
   - Trace shows `ClockMarketDataSummary / populated_exchange_count=11 / missing_exchange_count=0`.
   - Trace shows `macro_missing_symbols=[] / world_index_missing_symbols=[]`.
4. Global Markets is now a tape above Finance News and uses minute-only exchange times.
   - Evidence: `screensaver-001.png`, `screensaver-036.png`, `screensaver-072.png`.
5. Main top-right clock is now minute-only in the visual captures.
   - Evidence: top-right clock text shows `05:38`, `05:41`, `05:45` in sampled frames.

Implementation update, 2026-04-23:

1. `UX-065` patched, pending VM visual confirmation.
   - The top-right status clock now renders inside a clipped fixed-width semi-opaque pill.
   - The pill reserves room for minute time plus time-zone abbreviation and prevents adjacent macro/status fragments from painting into the clock slot.
2. `UX-060` patched, pending VM visual confirmation.
   - Provider text is compacted for the top band.
   - Long source chains collapse to a short live-source/cache summary while preserving partial/cache state and last-updated ticker timing.
3. `UX-056` patched, pending VM visual confirmation.
   - Floating graph cards are capped to 10 visible cards.
   - Runtime graph-vs-graph separation now nudges overlapping cards apart during motion.
4. `UX-057` patched, pending VM visual confirmation.
   - Graph density is reduced by the same visible-card cap and scene-selection cap.
   - Graph cards still swim behind foreground lanes as requested, but fewer cards should be active at once.
5. `UX-062` patched, pending VM visual confirmation.
   - Floating graph plot width now reserves a larger right-side label gutter.
   - Rightmost date labels have a minimum width and non-clipped bottom label row.
6. `UX-058` patched, pending VM visual confirmation.
   - Global Markets cards are slimmer and the tape lane has more vertical breathing room.
   - Copy spacing is reduced so more exchange cards can be visible at once.
7. `UX-061` patched, pending VM visual confirmation.
   - Global Markets clocks and the top-right clock now include compact time-zone abbreviations.
   - Seconds remain suppressed to reduce flicker.
8. `UX-063` patched, pending VM visual confirmation.
   - Global Markets mini sparklines now include subtle start/end tick marks and tiny `5D` / `now` axis labels.
9. `UX-064` patched, pending VM visual confirmation.
   - The top-center app title badge is larger, higher-contrast, and has a subtle glow.
10. `UX-059` patched, pending VM/trace confirmation.
    - Once all tracked symbols are fresh, the screensaver now enforces a 20-minute steady-state refresh minimum even if UI sliders contain very low test values.
    - Missing/unavailable symbol recovery still uses the faster recovery lane.
11. `UX-055` patched, pending VM visual confirmation.
    - Advanced data-source table headers now use explicit dark styling.
    - DataGrid cells use consistent dark styling, and the `Known Limit` column now wraps with a tooltip and wider minimum width.

Verification completed:

1. Focused test pass: `dotnet test .\tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj --filter "FullyQualifiedName~ScreensaverRenderBehaviorTests|FullyQualifiedName~ConfigTextConsistencyTests" -v minimal`
   - Result: passed, 33 tests.
2. Full unit pass: `dotnet test .\tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj --no-restore -v minimal`
   - Result: passed, 171 tests.
3. Host note: this machine currently has SDK `10.0.201`, while `global.json` pins SDK `8.0.420` with roll-forward disabled.
   - Test commands were run by temporarily moving `global.json` out of SDK resolution and immediately restoring it afterward.
   - This did not change the repository's Visual-Studio-first SDK pin.

Next recommended implementation / validation order:

1. Run the VM UX/trace cycle to visually confirm `UX-055` through `UX-065`.
2. Pay special attention to top-right clock corruption, Global Markets tape overlap/scanability, graph-card collisions, and whether the 20-minute steady-state refresh minimum reduces provider churn after all symbols populate.
3. If VM captures confirm the patches, close `UX-055` through `UX-065`; otherwise split any residual defects into new follow-up `UX-066+` items.

## 26) VM UX Confirmation Pass (2026-04-24, Recommended UX Batch)

Run evidence:

- Published/staged source: `build/artifacts/publish-safe-temp` from commit `0069506`
- Clean completed UX bundle used for confirmation:
  - `build/vm/artifacts/vm-results/ux-deep-20260423-071439`
- Summary:
  - `build/vm/artifacts/vm-results/ux-deep-20260423-071439/vm-ux-summary.json`
- Key sampled frames:
  - `config-tab-001-General.png`
  - `config-tab-002-Advanced.png`
  - `screensaver-001.png`
  - `screensaver-036.png`
  - `screensaver-072.png`
- Additional live rerun spot-check:
  - `build/vm/artifacts/host-runs/vm-live-20260424-091427.png`

Harness result:

1. First confirmation run completed cleanly.
   - `ConfigPhaseStatus=Completed`
   - `ScreensaverPhaseStatus=Completed`
   - `ConfigVersionCheck=Passed`
   - `ScreensaverVersionCheck=Passed`
2. A second rerun was used only as a live visual spot-check after adding automatic trace export.
   - The live saver screen was visible and current, but host export did not complete.
   - The exact post-patch trace for that rerun therefore remains unavailable from host artifacts.

Issue-by-issue confirmation:

1. `UX-055` fixed visually.
   - Advanced table header contrast is now materially better.
   - `Known Limit` text is readable and no longer presents as a washed-out/light-on-light header row.
   - Evidence: `config-tab-002-Advanced.png`
2. `UX-056` fixed visually.
   - No graph-card-on-graph-card collisions were observed in the sampled completed run.
   - Evidence: `screensaver-036.png`, `screensaver-072.png`
3. `UX-057` improved but not fully closed.
   - Density is much better than before, but graph cards can still drift into the top status/macro region.
   - Evidence: top-band intrusion visible in the 2026-04-24 live rerun (`vm-live-20260424-091427.png`).
   - Residual follow-up: `UX-066` graph cards must avoid the top status/macro/title band even while remaining behind the ticker lanes.
4. `UX-058` improved but not fully closed.
   - Global Markets tape is structurally clearer and easier to scan, with slimmer cards and better vertical presence.
   - However, the rightmost card can still clip at the viewport edge.
   - Evidence: partial right-edge clipping in `screensaver-036.png` and `screensaver-072.png`
   - Residual follow-up: `UX-067` Global Markets tape still needs a little more right-edge breathing room / viewport fade or padding.
5. `UX-059` still pending trace confirmation.
   - The patched runÃ¢â‚¬â„¢s exact trace was not exported successfully, so steady-state cadence could not be verified from trace.
   - Visuals alone are not enough to certify the 20-minute steady-state refresh rule.
6. `UX-060` improved but not fully closed.
   - Provider text is shorter than before (`Live sources ...`), but it still truncates with ellipsis in the top band during partial-source states.
   - Evidence: `screensaver-036.png`, `screensaver-072.png`
7. `UX-061` fixed visually.
   - Top-right clock and Global Markets clocks now include compact time-zone suffixes.
   - Evidence: `screensaver-001.png`, `screensaver-036.png`, `screensaver-072.png`
8. `UX-062` fixed visually.
   - Floating graph cards no longer show obvious rightmost date-label clipping in the sampled frames.
   - Evidence: `screensaver-036.png`, `screensaver-072.png`
9. `UX-063` fixed visually.
   - Global Markets mini charts now show subtle horizontal context with `5D` / `now`.
   - Evidence: `screensaver-001.png`, `screensaver-036.png`, `screensaver-072.png`
10. `UX-064` fixed visually.
    - The top-center title badge is now clearly more prominent and readable.
    - Evidence: `screensaver-001.png`, `screensaver-036.png`, `screensaver-072.png`
11. `UX-065` fixed visually.
    - No `+<number>`-style corruption was observed in the top-right clock during the completed sampled run or the live rerun spot-check.
    - Evidence: `screensaver-001.png`, `screensaver-036.png`, `screensaver-072.png`, `vm-live-20260424-091427.png`

Additional confirmation notes:

1. Config startup corruption appears materially improved in this pass.
   - `config-tab-001-General.png` is clean and readable at launch capture.
2. Config API-key fields remain masked in this UX pass.
   - No secret values are exposed on screen in the sampled config captures.
3. Footer button count remains correct.
   - Only one visible `Validate` button is present in the sampled config captures.

New issues added after this confirmation:

1. `UX-068` new: Config screen active-tab visibility is too low.
   - The selected tab state is not visually prominent enough during quick UX review.
   - Requested change: make the active tab label/state much easier to distinguish.
2. `UX-069` new: Config screen Advanced tab should remove the last column because it is unused.
   - The current final column consumes space without adding value in this workflow.
   - Requested change: delete the unused last column and reclaim the width.
3. `UX-070` new: Global Markets tape should pin the S&P 500 card at the far left and scroll the remaining exchanges.
   - Requested change: keep the S&P 500 always visible as a fixed anchor while the rest of the world cards continue as the moving tape.
4. `UX-071` new: Global Markets cards should use the small left-side space for a weather icon and basic weather.
   - Requested change: show compact local-weather context per exchange card where space permits.
5. `UX-072` new: Global Markets cards should add a small country flag to the left of the exchange name.
   - Requested change: add a compact flag marker to improve instant visual recognition of exchange geography.
6. `UX-073` new: Change the top app title to `SANYALnet Labs DO NOT PANIC Portfolio Visualizer`.
   - Requested change: replace the current shorter title badge text with the publisher-branded full app name.
7. `UX-074` new: Add `Ã‚Â© Supratim Sanyal. MIT License.` at bottom-left in the same unobtrusive style as the release identifier.
   - Requested change: mirror the low-key watermark style already used for the version text.

Recommended next implementation order after this confirmation:

1. Fix `UX-060` residual truncation in the provider lane.
2. Fix `UX-066` so graph cards cannot enter the top status/macro/title band.
3. Fix `UX-067` so the Global Markets tape has clean right-edge breathing room.
4. Fix `UX-068` so the active config tab is visually obvious.
5. Fix `UX-069` by removing the unused final column in Advanced.
6. Design and implement `UX-070`, `UX-071`, and `UX-072` together as one Global Markets tape upgrade.
7. Fix `UX-073` and `UX-074` together as branding/footer polish.
8. Re-run a 6-minute VM pass with working auto-trace export and then re-check `UX-059`.

## 27) Host Implementation Pass (2026-04-24, Recommended UX Batch Continued)

Completed on host, pending VM visual confirmation:

1. `UX-060` patched.
   - Provider text now compacts to a shorter top-band form (`Live`, `Live+Cache`, `Cache cooldown`, provider abbreviations) and no longer carries the trailing per-symbol `Updated` suffix inside the provider lane.
2. `UX-066` patched.
   - Floating graph motion now reserves the title plus status band at the top of the screen instead of allowing graph cards into that calmer header area.
3. `UX-067` patched.
   - Global Markets tape gained a pinned-card layout, extra right-edge padding, wider track spacing, and a slightly taller viewport to reduce clipping pressure.
4. `UX-068` patched.
   - Config tabs now use a stronger selected-state treatment with a brighter active tab, explicit border emphasis, and better hover contrast.
5. `UX-069` patched.
   - The unused final `Known Limit` column was removed from the Advanced data-source grid.
6. `UX-070`, `UX-071`, `UX-072` patched together.
   - The New York / S&P 500 card is now fixed at the left of the Global Markets lane while the remaining exchanges scroll.
   - Exchange cards now include compact weather context on the left and a country flag beside the exchange label.
7. `UX-073` and `UX-074` patched together.
   - Top-center title now reads `SANYALnet Labs DO NOT PANIC Portfolio Visualizer`.
   - Bottom-left unobtrusive legal watermark now reads `Ã‚Â© Supratim Sanyal. MIT License.`

Local verification for this host pass:

1. Focused UI/config tests passed: `36/36`.
2. Full unit suite passed: `171/171`.
3. Host build/test used the existing temporary `global.json` move-aside workaround because this machine still does not have the repo-pinned `.NET 8.0.420` SDK installed.

Next VM target:

1. Visually confirm `UX-060`, `UX-066`, and `UX-067` on the actual saver surface.
2. Confirm the new pinned Global Markets tape does not overlap, clip, or crowd the news lane.
3. Confirm the config active-tab styling and Advanced grid width cleanup at launch and after first paint.
4. Re-check `UX-059` steady-state refresh cadence with trace export once the harness lands a clean bundle again.

## 28) VM Confirmation Pass (2026-04-24, Fresh `publish-next` Payload)

What was verified:

1. Stale-payload blocker resolved.
   - `publish-safe-temp` was fresh on the host but the guest had staged an older copy through the shared-folder workflow.
   - Re-staging through a fresh artifact root (`publish-next`) fixed this.
   - Evidence: guest staged-build manifest now shows 2026-04-24 timestamps and the newer file sizes for both EXEs.
2. `UX-068` confirmed fixed visually.
   - Active config tab styling is now much clearer on both `General` and `Advanced`.
3. `UX-069` confirmed fixed visually.
   - The old `Known Limit` content column is gone from the Advanced grid.
   - There is still empty right-side width because the grid stretches to fill the container, but the requested unused column has been removed.
4. `UX-073` and `UX-074` confirmed fixed visually.
   - Saver now shows `SANYALnet Labs DO NOT PANIC Portfolio Visualizer` at the top center.
   - Bottom-left unobtrusive legal watermark is visible.
5. `UX-060` confirmed fixed visually.
   - Provider lane now renders in the compact form (`Live+Cache (Partial)`) and fits within the top band.
6. `UX-066` confirmed fixed visually.
   - Graph cards stay below the top status/macro/title band in the live saver run.
7. `UX-067` confirmed materially improved.
   - Global Markets lane gained enough right-edge breathing room for the observed live frame.
8. `UX-070`, `UX-071`, and `UX-072` confirmed fixed visually.
   - New York / S&P 500 is pinned at the left of the Global Markets lane.
   - Exchange cards now include country flags and compact weather.
9. `UX-059` confirmed by trace from the corrected runtime path.
   - Once the scene reached full population, `event=TimersConfigured` switched to `refresh_seconds=1200`.

Runtime confirmation from trace:

1. Macros now populate:
   - `OfficialMacroQuotesApplied` fetched `^VIX` and `^VIX3M`.
   - `TreasuryMacroQuotesApplied` fetched `US2M` and `US10Y`.
   - `MacroSnapshot` then showed populated values for `VIX`, `VIX3M`, `UST2M`, `UST10Y`, `YLD SPRD`, and `DXY`.
2. Global Markets now populate:
   - `GlobalMarketQuotesApplied / provider=Stooq` fetched the exchange set.
   - `ClockMarketDataSummary` later reached `populated_exchange_count=11 / missing_exchange_count=0`.
3. Yahoo fallback behavior is now productive even when Yahoo itself is rate-limited.
   - Trace still shows some Yahoo `429` events, but the scene remains fully populated through the mixed-source flow.

Harness status after this pass:

1. Guest screensaver UIA version probing was hardened so it no longer fails immediately when full-screen saver ownership changes.
2. The host wrapper now suppresses the benign repeated `codexrepo already exists` VirtualBox shared-folder error.
3. The final corrected `publish-next` deep run did land a full result bundle and trace set.
   - Result bundle: `build/vm/artifacts/vm-results/ux-deep-20260424-061924`
   - Trace bundle: `build/vm/artifacts/trace/ux-deep-20260424-061924-trace`
4. The remaining harness issue is narrower than first thought.
   - `ConfigPhaseStatus=Completed`
   - `ScreensaverPhaseStatus=Completed`
   - `ConfigVersionCheck=Passed`
   - `ScreensaverVersionCheck=Failed`
   - Failure note: `Screensaver version text containing 'beta5' not detected.`
5. In other words, export/capture now works again; the residual defect is the saver version-text probe rather than the overall deep-cycle landing path.

Evidence from this pass:

1. Config general:
   - `build/vm/artifacts/vm-results/ux-deep-20260424-061440/config-tab-001-General.png`
2. Config advanced:
   - `build/vm/artifacts/vm-results/ux-deep-20260424-061440/config-tab-002-Advanced.png`
3. Live saver with corrected payload:
   - `build/vm/artifacts/host-runs/vm-live-20260424-1024.png`
4. Fresh guest staging proof:
   - `build/vm/artifacts/staged-builds/staged-build-20260424-061135.json`
5. Final deep-run result bundle:
   - `build/vm/artifacts/vm-results/ux-deep-20260424-061924`
6. Final deep-run trace bundle:
   - `build/vm/artifacts/trace/ux-deep-20260424-061924-trace`
7. Runtime trace proving macro/world/global-market population:
   - `build/vm/artifacts/trace/ux-deep-20260424-051532-trace/trace.reconstructed.log`

Next recommended order:

1. Close `UX-060`, `UX-066`, `UX-068`, `UX-069`, `UX-070`, `UX-071`, `UX-072`, `UX-073`, and `UX-074` as confirmed.
2. Keep `UX-067` open only if later frames reveal edge clipping not seen in the live confirmation frame.
3. Fix the remaining saver version-text detection issue in the VM harness so `ScreensaverVersionCheck` can pass on the corrected runs.
4. Then resume the next UX defect batch from the updated open-issues list.

## 29) VM Harness Version-Probe Closure (2026-04-24)

What was changed:

1. Saver version detection was made more robust for full-screen UX validation.
   - The on-screen version watermark now publishes explicit automation metadata:
     - `AutomationId = ScreensaverVersionWatermark`
     - `Name = Version <semantic version>`
     - `HelpText = <semantic version>`
2. The full-screen host window now mirrors the same semantic version in automation-visible metadata.
   - Window title now includes the semantic version.
   - Window automation metadata:
     - `AutomationId = ScreensaverHostWindow`
     - `Name = Portfolio Screensaver <semantic version>`
     - `HelpText = <semantic version>`
3. The guest UX harness now searches saver-process automation metadata more broadly instead of relying only on `Text` controls containing `beta5`.
4. The host VM launcher wrapper now captures `VBoxManage` stdout/stderr cleanly instead of letting benign shared-folder warnings spill into the console stream.

Verification:

1. Focused host screensaver/render tests passed again after the probe change:
   - `ScreensaverRenderBehaviorTests`: `25/25`
2. Full VM deep UX cycle passed with the saver version probe now succeeding:
   - Result bundle: `build/vm/artifacts/vm-results/ux-deep-20260424-105436`
   - Summary:
     - `ConfigPhaseStatus=Completed`
     - `ScreensaverPhaseStatus=Completed`
     - `ConfigVersionCheck=Passed`
     - `ScreensaverVersionCheck=Passed`
   - Observed note:
     - `Screensaver version element observed: name='Portfolio Screensaver 0.9.0-beta5.3.2' automation_id='ScreensaverHostWindow' help='0.9.0-beta5.3.2'`
3. Trace export still landed for that run:
   - `build/vm/artifacts/trace/ux-deep-20260424-105436-trace`
4. Prep-only launcher smoke test passed after the stderr-capture cleanup:
   - Launcher run: `build/vm/artifacts/launcher-runs/20260424-150420`

Status outcome:

1. The VM harness version-probe blocker is closed.
2. Automated VM UX passes can now verify both config and saver version identity on the launched build.
3. The next work should return to product UX/data defects rather than harness uncertainty.

## 30) Next Product UX Batch (2026-04-24, Status Band + Global Markets Flags)

New issues added from review:

1. `UX-075` new: Move Macro Indicators to the top-middle of the saver status band.
   - Requested change: the macro dials should occupy the centered top lane instead of being treated as secondary-row trailing content.
2. `UX-076` new: `VIX` and `VIX3M` use the wrong directional colors.
   - Requested change: volatility rising should read as adverse/risk-up (`red`), and volatility falling should read as easing/risk-down (`green`).
3. `UX-077` new: Global Markets cards should place a real drawn flag at the left of the location label instead of unresolved emoji-like glyph characters.
   - Requested change: draw compact card-native flags and anchor them beside the left label area.

Host implementation completed in this pass:

1. `UX-075` patched.
   - Macro indicators now render in the centered top lane of the status bar.
   - Market status remains left-aligned and the main clock remains right-aligned.
2. `UX-076` patched.
   - `VIX` and `VIX3M` now invert the usual equity palette:
     - rising volatility => `red`
     - falling volatility => `green`
3. `UX-077` patched.
   - Global Markets cards now render compact vector-style mini-flags from explicit country codes.
   - The flag now sits to the left of the location label instead of being emitted as emoji text near the exchange name.

Host verification:

1. Focused screensaver/render tests passed:
   - `ScreensaverRenderBehaviorTests`: `27/27`
2. Full test project passed:
   - `PortfolioSaver.Tests`: `173/173`

Next recommended order:

1. VM-confirm `UX-075`, `UX-076`, and `UX-077` on the actual saver surface.
2. Re-check whether `UX-067` still shows any right-edge clipping in later live frames after the card-content rebalance.
3. Then continue down the remaining open product UX/data issue list.

## 31) VM Confirmation Pass (2026-04-24, Macro Lane + Drawn Flags)

Run evidence:

1. Launcher run:
   - `build/vm/artifacts/launcher-runs/20260424-152132`
2. Result bundle:
   - `build/vm/artifacts/vm-results/ux-deep-20260424-112243`
3. Summary:
   - `build/vm/artifacts/vm-results/ux-deep-20260424-112243/vm-ux-summary.json`
4. Trace bundle:
   - `build/vm/artifacts/trace/ux-deep-20260424-112243-trace`

Harness/result status:

1. `ConfigPhaseStatus=Completed`
2. `ScreensaverPhaseStatus=Completed`
3. `ConfigVersionCheck=Passed`
4. `ScreensaverVersionCheck=Passed`

Issue-by-issue confirmation:

1. `UX-075` confirmed fixed visually.
   - Macro indicators now sit in the centered top lane between the market-status text and the top-right clock.
   - Evidence:
     - `screensaver-001.png`
     - `screensaver-020.png`
     - `screensaver-072.png`
2. `UX-076` confirmed fixed visually.
   - `VIX` / `VIX3M` now read as adverse-risk indicators:
     - rising values show red/orange
     - falling values show green
   - Evidence:
     - `screensaver-001.png` shows `VIX3M +1.1%` in red/orange
     - `screensaver-020.png` shows `VIX +2.1%` and `VIX3M +1.1%` in red/orange
3. `UX-077` confirmed fixed visually.
   - Global Markets cards now show compact drawn flags at the left of the city/location label.
   - The previous mojibake/emoji-fallback look is no longer present in sampled frames.
   - Evidence:
     - `screensaver-020.png`
     - `screensaver-041.png`
     - `screensaver-072.png`

Residual check:

1. `UX-067` remains open.
   - Right-edge clipping is still visible on later Global Markets frames even after the card rebalance.
   - Observed examples:
     - `screensaver-020.png`: Toronto card clips at the right edge.
     - `screensaver-041.png`: London card clips at the right edge.
     - `screensaver-072.png`: Sydney card clips at the right edge.
2. Conclusion:
   - The Global Markets tape is improved and structurally sound, but it still needs additional right-edge viewport breathing room / masking / spacing logic before `UX-067` can be closed.

Next recommended order:

1. Fix `UX-067` next by giving the scrolling viewport more safe right-edge space or a stronger fade/mask so partially-entering cards never appear visibly clipped.
2. Then continue through the remaining open UX/data issues in the checklist.

## 32) Host Pass (2026-04-24, `UX-067` Right-Edge Softening)

Problem restated from VM evidence:

1. Even after the card-content rebalance, later frames still showed hard right-edge clipping on partially entering Global Markets cards.
2. Aesthetic goal:
   - cards should ease in/out of the scrolling viewport
   - they should not appear as abruptly chopped rectangles at the right boundary

Host implementation in this pass:

1. Increased the scrolling viewport right-side runway:
   - `ViewportHost` margin changed from `16,0,20,0` to `18,0,32,0`
2. Added an explicit horizontal opacity mask to the scrolling viewport.
   - left edge fades in from transparent to opaque
   - right edge fades out from opaque to transparent
3. Intent:
   - partially entering cards now soften visually at the edge instead of presenting as harsh clipping

Host verification:

1. Focused render/screensaver tests passed:
   - `ScreensaverRenderBehaviorTests`: `27/27`

Next recommended order:

1. Run one VM confirmation pass focused on `UX-067` to see whether the fade/runway treatment is enough to close it.
2. If any residual clipping remains, move to a stronger viewport mask or reduced visible card width for the scrolling lane.

## 33) VM Confirmation Pass (2026-04-24, `UX-067` Right-Edge Softening)

Run evidence:

1. Launcher run:
   - `build/vm/artifacts/launcher-runs/20260424-153902`
2. Result bundle:
   - `build/vm/artifacts/vm-results/ux-deep-20260424-114012`
3. Summary:
   - `build/vm/artifacts/vm-results/ux-deep-20260424-114012/vm-ux-summary.json`
4. Trace bundle:
   - `build/vm/artifacts/trace/ux-deep-20260424-114012-trace`

Harness/result status:

1. `ConfigPhaseStatus=Completed`
2. `ScreensaverPhaseStatus=Completed`
3. `ConfigVersionCheck=Passed`
4. `ScreensaverVersionCheck=Passed`

Issue outcome:

1. `UX-067` can now be closed.
   - The Global Markets lane no longer presents hard right-edge card clipping in sampled early/mid/late frames.
   - The added viewport runway plus opacity-mask fade makes partial entry/exit read as intentional soft motion rather than abrupt truncation.

Evidence:

1. `screensaver-020.png`
   - rightmost card entry now fades rather than reading as a chopped rectangle
2. `screensaver-041.png`
   - far-right exchange card remains readable without the earlier hard cut
3. `screensaver-072.png`
   - late-run rightmost card remains visually clean at the viewport boundary

Conclusion:

1. Close `UX-067`.
2. Continue with the remaining open UX/data issues from the checklist.

## 34) Host Pass (2026-04-24, Top-Band Title/Macro Separation)

Problem restated from latest VM screenshots:

1. The centered macro indicator strip was colliding with the branded top title pill.
2. Center macro cards were partially hidden behind the title, especially in early/mid-run frames.
3. The Global Markets right-edge softening remains acceptable; this pass is specifically about top-band hierarchy.

Host implementation in this pass:

1. Reserved a dedicated title lane by pushing the status bar host down with a top margin.
2. Kept macro indicators in the centered top status lane, but no longer in the same vertical slot as the title pill.
3. Normalized the footer legal text source to use an HTML entity for the copyright symbol so the source stays encoding-safe.

Files changed:

1. src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml
2. 	ests/PortfolioSaver.Tests/Services/ScreensaverRenderBehaviorTests.cs

Host verification:

1. Focused screensaver/render tests passed:
   - ScreensaverRenderBehaviorTests: 28/28

Current status:

1. The macro/title collision is patched on the host side.
2. A VM visual confirmation pass is still the right next step before formally calling the collision closed in the checklist.

## 35) Host Pass (2026-04-24, Remove Benchmark Feature + Top-Band Follow-Through)

Scope completed in this pass:

1. Removed benchmark configuration UI from the config app.
2. Removed benchmark settings/defaults/normalization/validation paths.
3. Removed benchmark runtime fetch cadence, pending-recovery bookkeeping, and scene-state plumbing.
4. Deleted orphaned benchmark-only model/viewmodel/control files.
5. Preserved and host-tested the earlier top-band title/macro separation fix.

Files removed:

1. src/PortfolioSaver.Core/Models/BenchmarkItem.cs
2. src/PortfolioSaver.Config/ViewModels/BenchmarkItemEditorViewModel.cs
3. src/PortfolioSaver.Config/ViewModels/BenchmarksPageViewModel.cs
4. src/PortfolioSaver.Config/Views/BenchmarksPageView.xaml
5. src/PortfolioSaver.Config/Views/BenchmarksPageView.xaml.cs
6. src/PortfolioSaver.Render/ViewModels/BenchmarkStripViewModel.cs
7. src/PortfolioSaver.Render/Controls/BenchmarkStripControl.xaml
8. src/PortfolioSaver.Render/Controls/BenchmarkStripControl.xaml.cs

Host verification:

1. Full test project passed:
   - PortfolioSaver.Tests: 174/174

Conclusion:

1. The benchmark feature is now removed rather than merely hidden.
2. Portfolio tickers, macro indicators, world indices, and Global Markets remain the active quote lanes.

## 36) Manual VM Confirmation (2026-04-24, Benchmark Removal + Title/Macro Separation)

Why this pass was manual:

1. Invoke-VmUxDeepCycle.ps1 again stalled after launching deep UX and did not export a fresh m-results bundle within the wrapper wait path.
2. Rather than keep waiting blindly, live evidence was collected directly from the VM and the wrapper was shut down cleanly.

Evidence captured:

1. Config launch screenshot:
   - uild/vm/artifacts/launcher-runs/20260424-162023/launch-deepux-plus10s.png
2. Live saver screenshot:
   - uild/vm/artifacts/host-runs/vm-live-20260424-1629.png
3. Post-stop desktop confirmation:
   - uild/vm/artifacts/host-runs/vm-after-stop-20260424-1631.png

Confirmed visually:

1. The benchmark editor is gone from the config screen.
   - No benchmark refresh slider.
   - No floating benchmark cards editor block.
2. The top title and macro strip are now separated correctly on the saver.
   - Macro indicators no longer sit directly under the title pill.
   - The title remains centered and readable without obscuring the macro cards.
3. The guest run was explicitly terminated after capture.

Residual harness note:

1. The VM wrapper still has an export/collection reliability gap after deep UX launch.
2. Product-level visual confirmation for this pass is good; harness automation is the remaining tooling issue.

## 37) VM Harness Fix (2026-04-24, Direct-Share Result Handshake)

Problem fixed in this pass:

1. Invoke-VmUxDeepCycle.ps1 could sit waiting after deep UX launch because the guest only exposed the result bundle at the very end of the run.
2. The wrapper could not distinguish between:
   - a still-running guest exercise
   - a completed run whose export had not yet happened
   - a real hang

Harness changes:

1. Guest-UxDeepExercise.ps1 now prefers writing results directly to the host share under:
   - uild/vm/artifacts/vm-results/ux-deep-*
2. The guest now writes summary files at start and updates them as phases complete.
3. The guest still exports trace files to:
   - uild/vm/artifacts/trace/ux-deep-*-trace
4. Invoke-VmUxDeepCycle.ps1 now waits for the summary to leave the Pending state instead of returning merely because the summary file exists.
5. The wrapper now also supports shorter smoke runs via:
   - -GuestScreensaverDurationMinutes
   - -GuestCaptureIntervalSeconds

Verification run:

1. Command used:
   - Invoke-VmUxDeepCycle.ps1 -PublishSource publish-next -RunToolScan:False -RunDeepUx:True -GuestScreensaverDurationMinutes 1 -GuestCaptureIntervalSeconds 10 -PrepWaitSeconds 40 -ResultTimeoutMinutes 8
2. Launcher run:
   - uild/vm/artifacts/launcher-runs/20260424-172351
3. Result bundle:
   - uild/vm/artifacts/vm-results/ux-deep-20260424-132503
4. Trace bundle:
   - uild/vm/artifacts/trace/ux-deep-20260424-132503-trace
5. Summary result:
   - ConfigPhaseStatus=Completed
   - ScreensaverPhaseStatus=Completed
   - ConfigVersionCheck=Passed
   - ScreensaverVersionCheck=Passed
   - ExportMode=DirectHostShare

Conclusion:

1. The wrapper now lands result and trace artifacts reliably without the old blind post-run wait.
2. The remaining VM work can go back to product UX/data validation instead of harness babysitting.

## 38) VM Re-Baseline Pass (2026-04-24, Fresh 6-Minute UX Sweep)

Purpose:

1. Re-run the now-reliable VM loop after the recent UI and harness work.
2. Use the new bundle as the baseline for the next product-side coding batch rather than relying on older screenshots.

Evidence:

1. Launcher run:
   - `build/vm/artifacts/launcher-runs/20260424-181908`
2. Result bundle:
   - `build/vm/artifacts/vm-results/ux-deep-20260424-142019`
3. Trace bundle:
   - `build/vm/artifacts/trace/ux-deep-20260424-142019-trace`

Key findings from the bundle:

1. Config phase completed successfully with broad widget traversal.
2. Screensaver phase completed successfully with 36 captures.
3. The saver UI itself was clearly a BETA-5.3.2 build in screenshots, but:
   - `ScreensaverVersionCheck=Failed`
   - The guest script only accepted the watermark path at that time, so the harness result was noisier than the actual product state.
4. The Global Markets tape remained structurally improved, but the scrolling lane could still reveal detached partial card/value fragments immediately to the right of the pinned New York card.

Follow-on work selected from this pass:

1. Harden the screensaver version probe so it also accepts the saver host window semantic-version metadata.
2. Further soften/shroud the Global Markets viewport entrance so exiting cards do not leave ugly detached value fragments beside the pinned card.

## 39) Host + VM Follow-Through (2026-04-24, Version Probe Hardening + Global Markets Entry Cleanup)

Scope completed:

1. `Guest-UxDeepExercise.ps1` now treats either of these as a valid screensaver version proof:
   - `ScreensaverVersionWatermark`
   - `ScreensaverHostWindow`
2. The guest harness also falls back to `MainWindowTitle` matching if the UIA subtree is slow to surface.
3. `GlobalMarketsTapeControl.xaml` now gives the scrolling lane more runway and uses explicit left/right edge shrouds in addition to the opacity mask.

Host verification:

1. Focused tests passed:
   - `VmHarnessScriptTests`
   - `ScreensaverRenderBehaviorTests`
   - Result: 31 / 31

VM confirmation runs:

1. First short confirmation:
   - Result bundle: `build/vm/artifacts/vm-results/ux-deep-20260424-145343`
   - `ConfigVersionCheck=Passed`
   - `ScreensaverVersionCheck=Passed`
   - Screensaver proof came from the semantic version exposed by the saver host window.
2. Final short confirmation after the stronger edge-shroud pass:
   - Result bundle: `build/vm/artifacts/vm-results/ux-deep-20260424-150810`
   - Trace bundle: `build/vm/artifacts/trace/ux-deep-20260424-150810-trace`
   - `ConfigVersionCheck=Passed`
   - `ScreensaverVersionCheck=Passed`
   - Notes show direct observation of:
     - `name='Portfolio Screensaver 0.9.0-beta5.3.2'`
     - `automation_id='ScreensaverHostWindow'`

Outcome:

1. The VM harness version-check regression is closed.
2. The Global Markets left-edge artifact is materially improved and no longer shows the earlier glaring detached-value block.
3. A small amount of edge-entry content can still be glimpsed in some frames, so this becomes a lighter polish follow-up instead of a blocker.

New follow-up:

1. `UX-078` Global Markets scrolling lane still allows a small amount of partial-card content beside the pinned New York card in some frames; continue refining the left-edge shroud only if future human review says it is still too visible.

## 40) Follow-Through Pass (2026-04-25, Global Markets Lead-In Gap)

Additional refinement completed:

1. Added deliberate lead-in spacing inside each scrolling Global Markets sequence so the pinned New York card is not immediately followed by an exiting market card tail.
2. Kept the earlier left/right shrouds in place so the spacing and masking now work together instead of relying on opacity fade alone.

Host verification:

1. Focused tests passed again:
   - `VmHarnessScriptTests`
   - `ScreensaverRenderBehaviorTests`
   - Result: 31 / 31

VM evidence:

1. Confirmation bundle with successful version checks:
   - `build/vm/artifacts/vm-results/ux-deep-20260424-150810`
2. Additional bundle from the lead-in-gap pass:
   - `build/vm/artifacts/vm-results/ux-deep-20260425-060413`
3. In the 2026-04-25 bundle, the summary file did not reach final completion before the bounded wrapper kill, but saver frames were still exported and are usable for visual review.

Visual result:

1. The earlier detached-value artifact at the left seam is substantially reduced.
2. A tiny amount of partial-card content can still appear in some frames (`screensaver-002.png`, `screensaver-003.png`) but it now reads as minor edge-entry polish rather than a broken tape/card composition.

Current recommendation:

1. Keep `UX-078` open as low-severity polish.
2. Use human visual review to decide whether the remaining seam content is acceptable before spending more code on it.

## 41) Global Markets Card Density + Advanced Grid Width Pass (2026-04-25)

Scope completed:

1. Tightened Global Markets cards so the right-hand numeric block reads more evenly and wastes less horizontal space.
2. Rebuilt the mini-flag badge path around a fixed-aspect drawing surface so small flags do not clip or drift vertically inside the badge.
3. Corrected `DXY` guest-visible color semantics on a fresh publish by verifying the actual VM was staging the latest build.
4. Fixed the Advanced data-source table layout so headers use the available tab width instead of collapsing inside a `StackPanel` measurement path.

Host verification:

1. Focused config-text tests passed:
   - `ConfigTextConsistencyTests`
   - Result: 8 / 8
2. Focused screensaver/render tests passed:
   - `ScreensaverRenderBehaviorTests`
   - Result: 31 / 31
3. Additional assertion added for `DXY`:
   - falling dollar strength now verifies as green in host tests
   - positive yield spread continues to verify as green by spread sign, not by arbitrary direction wording

Fresh publish + VM evidence:

1. Fresh publish:
   - `build/publish-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300`
   - Completed successfully and regenerated `build/artifacts/publish-safe-temp`
2. VM confirmation bundle:
   - `build/vm/artifacts/vm-results/ux-deep-20260425-071615`
3. Launcher bundle:
   - `build/vm/artifacts/launcher-runs/20260425-111503`

Visual confirmation from the fresh VM payload:

1. Advanced config headers are now fully readable in:
   - `build/vm/artifacts/vm-results/ux-deep-20260425-071615/config-tab-002-Advanced.png`
   - `Service`, `Per Hour`, `Per Day`, `Single`, and `Multiple` all render without the earlier cutoff/compression.
2. `DXY` now follows the intended macro semantics in:
   - `build/vm/artifacts/vm-results/ux-deep-20260425-071615/screensaver-003.png`
   - the displayed negative move is green instead of orange/red.
3. `YLD SPRD` remains green while positive in the same capture.
4. Global Markets cards are denser and cleaner, with the refreshed flag badge treatment visible in:
   - `screensaver-001.png`
   - `screensaver-002.png`
   - `screensaver-003.png`
   - `screensaver-004.png`

Outcome:

1. The Advanced data-source table width/readability regression is materially improved and no longer presents clipped headers in the VM evidence.
2. The `DXY` macro semantic-color regression is closed on the actual guest surface after rebuilding the staged payload.
3. The recent Global Markets compacting/flag work is visually improved enough for human review.

## 42) Seven-Issue Closure Pass (2026-04-25)

Scope:

1. Close the previously prioritized seven-item UX batch:
   - `UX-031` Config first-paint corruption
   - `UX-053` Floating graph cards intruding into active ticker lanes
   - `UX-050` Residual clock/top-band flicker-corruption
   - `UX-043` Global Markets polish
   - `UX-032` Config footer clutter / junk near `Validate`
   - Global Markets flag polish
   - `UX-078` Global Markets seam glimpse

Code follow-through completed:

1. Added a startup shield plus tab pre-warm pass in the config app so the first visible frame is masked while both tabs are rendered once before interaction.
2. Reworked the status bar into explicit left / center / right columns so market status, macro chips, and the top-right clock are no longer fighting for the same lane.
3. Tightened graph-card motion bounds so the preferred motion corridor sits below the tape stack and above the bottom overlay block.
4. Slimmed the Global Markets lane further and increased the left-edge lead-in/shroud spacing so the pinned New York card is not immediately followed by a visible half-card seam.
5. Refined small flag rendering so badges stay within a fixed drawing surface instead of clipping or drifting inside the card header.

Fresh publish + VM confirmation:

1. Fresh publish completed successfully:
   - `build/publish-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300`
   - output refreshed under `build/artifacts/publish-safe-temp`
2. Short VM confirmation run completed successfully:
   - launcher run: `build/vm/artifacts/launcher-runs/20260425-122710`
   - result bundle: `build/vm/artifacts/vm-results/ux-deep-20260425-082826`
   - summary: `build/vm/artifacts/vm-results/ux-deep-20260425-082826/vm-ux-summary.json`
3. Result summary:
   - `ConfigPhaseStatus=Completed`
   - `ScreensaverPhaseStatus=Completed`
   - `ConfigVersionCheck=Passed`
   - `ScreensaverVersionCheck=Passed`

Visual closure evidence:

1. `UX-031` addressed:
   - `launch-deepux-plus10s.png` now shows the startup shield instead of garbled first-paint text, and the shield message itself is clean/readable.
2. `UX-032` addressed:
   - `config-tab-002-Advanced.png` shows a single clean footer action area with no duplicated validate/junk artifacts.
3. `UX-050` addressed:
   - `screensaver-001.png`, `screensaver-006.png`, and `screensaver-012.png` show a calm top band with separated title, macro lane, market status, and clock.
4. `UX-053` addressed:
   - in the same saver captures, floating graph cards now remain in the lower motion field instead of cutting through the active ticker rows.
5. `UX-043` addressed:
   - the Global Markets region now reads as a lighter tape rather than a bulky grouped block, with cleaner spacing and reduced card waste.
6. Flag polish addressed:
   - fresh Global Markets cards in `screensaver-001.png` and `screensaver-012.png` show compact fixed-aspect flag badges instead of clipped or drifting pseudo-emoji glyphs.
7. `UX-078` addressed:
   - the New York seam no longer presents the earlier obvious detached-value fragment; any remaining edge content is now visually negligible in the short confirmation run.

Operational note:

1. Focused `dotnet test` re-runs from temporary workspaces still hung at the host-shell level during this pass and were terminated under the 5-minute timeout rule, but the fresh publish and VM confirmation run succeeded and provide the authoritative validation evidence for this seven-issue UX batch.

Conclusion:

1. All seven prioritized issues are now addressed in code and confirmed on the VM surface.
2. The next iteration can safely move on to newly observed defects instead of carrying this batch forward.

## 43) Global Markets Flag Review Pass (2026-04-25)

Trigger:

1. Human review of the latest VM screenshots still showed miniature flag defects:
   - the US badge could read cropped in New York cards
   - the India badge wheel could read visually off-center in Mumbai cards

Work completed:

1. Kept the flag solution dependency-free instead of adding a new external asset package in this pass.
2. Increased the miniature badge from the previous tiny geometry to a larger fixed-aspect render path.
3. Rebuilt the flag drawing surface around explicit constants:
   - larger badge container
   - larger internal 3:2 flag canvas
4. Improved small-flag detail where it mattered most at this scale:
   - US flag now uses a larger canton and simple star-field dots instead of relying on a tiny plain block
   - India flag now uses a centered navy wheel/hub/spoke miniature instead of a small ring that could drift visually during scaling
   - related flags were kept on the same larger fixed-aspect canvas so the whole set benefits from the same scaling path

Fresh publish + VM confirmation:

1. Fresh publish succeeded:
   - `build/publish-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300`
2. Fresh short VM confirmation run succeeded:
   - launcher run: `build/vm/artifacts/launcher-runs/20260425-142213`
   - result bundle: `build/vm/artifacts/vm-results/ux-deep-20260425-102326`
   - summary: `build/vm/artifacts/vm-results/ux-deep-20260425-102326/vm-ux-summary.json`
3. Result summary:
   - `ConfigPhaseStatus=Completed`
   - `ScreensaverPhaseStatus=Completed`
   - `ConfigVersionCheck=Passed`
   - `ScreensaverVersionCheck=Passed`

Visual evidence:

1. `screensaver-001.png`
   - New York, Shanghai, Hong Kong, Mumbai, and Frankfurt cards all show the newer fixed-aspect mini-flags.
   - The New York badge no longer presents the same obviously chopped look from the earlier human-reported capture.
2. `screensaver-006.png`
   - Toronto, Seoul, and Sydney/London lane entries continue to show consistent miniature-flag sizing rather than mixed glyph-like artifacts.
3. `screensaver-012.png`
   - London, Tokyo, and Shanghai continue to render as clean miniature badges on the same larger canvas.

Outcome:

1. The flag renderer is materially improved and no longer depends on unresolved emoji glyphs or the earlier too-small badge geometry.
2. If future human review still wants photo-clean heraldic fidelity, the next escalation path can be bundled raster/vector flag assets, but that is no longer required to fix the current cropped/off-center defects.

## 44) Bundled Downloaded Flag Asset Pass (2026-04-25)

Trigger:

1. Human review rejected the code-drawn miniature flags as inherently wrong for true national flags.
2. Direction changed from generated miniature flag drawings to bundled downloaded flag assets.

Implementation:

1. Added bundled PNG assets under:
   - `src/PortfolioSaver.Render/Assets/Flags`
2. Included the flag files as WPF resources in:
   - `src/PortfolioSaver.Render/PortfolioSaver.Render.csproj`
3. Replaced the custom flag drawing path in:
   - `src/PortfolioSaver.Render/Controls/GlobalMarketsTapeControl.xaml.cs`
4. The control now resolves flags by `FlagCode` using pack URIs:
   - `pack://application:,,,/PortfolioSaver.Render;component/Assets/Flags/<code>.png`
5. If a resource is missing, the badge falls back to a neutral solid color instead of drawing a fake flag.

Bundled assets:

1. `us.png`
2. `gb.png`
3. `jp.png`
4. `cn.png`
5. `hk.png`
6. `in.png`
7. `de.png`
8. `fr.png`
9. `ca.png`
10. `kr.png`
11. `au.png`

Validation:

1. Fresh publish succeeded:
   - `build/publish-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300`
2. Fresh VM confirmation run succeeded:
   - launcher run: `build/vm/artifacts/launcher-runs/20260425-144109`
   - result bundle: `build/vm/artifacts/vm-results/ux-deep-20260425-104220`
   - summary: `build/vm/artifacts/vm-results/ux-deep-20260425-104220/vm-ux-summary.json`
3. Summary status:
   - `ConfigPhaseStatus=Completed`
   - `ScreensaverPhaseStatus=Completed`
   - `ConfigVersionCheck=Passed`
   - `ScreensaverVersionCheck=Passed`

Visual evidence:

1. `screensaver-001.png`
2. `screensaver-006.png`
3. `screensaver-012.png`

Outcome:

1. Global Markets flags now use bundled downloaded image assets instead of approximate hand-drawn miniatures.
2. This removes the fundamental correctness problem called out in review for the USA and India flags.

## 45) Focused Anti-429 Pass (2026-04-25)

Trigger:

1. Latest runtime trace still showed avoidable Yahoo `429` pressure.
2. The most obvious waste was startup dedicated Yahoo warmup still hitting `DX-Y.NYB`, even though DXY already has a native Stooq path.
3. The normal provider order also still put Yahoo ahead of healthier backup providers for standard ETF/equity recovery.

Implementation:

1. Changed the shipped news feed default from `120` minutes to `15` minutes in:
   - `src/PortfolioSaver.Core/Constants/Defaults.cs`
2. Tightened Yahoo reuse windows in:
   - `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs`
   - General Yahoo reuse window increased to `60s`
   - Dedicated Yahoo reuse window increased to `180s`
3. Strengthened Yahoo rate-limit cooldown policy:
   - general Yahoo `429` cooldown now `8 minutes`
   - dedicated Yahoo `429` cooldown now `12 minutes`
4. Changed provider order so that when backup providers are configured, runtime quote refresh now uses:
   - rotated backups first
   - Yahoo last as the unresolved-symbol fallback
5. Narrowed startup dedicated Yahoo warmup so it skips symbols that already have a native non-Yahoo source.
   - With the current source mix, this removes `DX-Y.NYB` from startup Yahoo warmup entirely.

Host validation:

1. Focused coordinator test run passed:
   - `dotnet test .\\tests\\PortfolioSaver.Tests\\PortfolioSaver.Tests.csproj --filter "FullyQualifiedName~StartupCoordinatorAdvancedTests" --no-restore -v minimal`
   - Result: `27/27` passing
2. Fresh publish succeeded:
   - `build/publish-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300`

Fresh VM confirmation:

1. Launcher run:
   - `build/vm/artifacts/launcher-runs/20260425-151236`
2. Result bundle:
   - `build/vm/artifacts/vm-results/ux-deep-20260425-111346`
3. Trace bundle:
   - `build/vm/artifacts/trace/ux-deep-20260425-111346-trace`
4. Summary:
   - `ConfigPhaseStatus=Completed`
   - `ScreensaverPhaseStatus=Completed`
   - `ConfigVersionCheck=Passed`
   - `ScreensaverVersionCheck=Passed`

Trace findings from this exact pass:

1. Startup dedicated Yahoo warmup no longer runs at all for the current symbol mix.
   - Evidence: the trace contains no `WarmupPlan`, `WarmupBatchStarting`, or `Startup Yahoo warmup` entries.
2. Macro and world-index data still populate early in the run, but now through healthier native providers first:
   - `OfficialMacroQuotesApplied`
   - `TreasuryMacroQuotesApplied`
   - `GlobalMarketQuotesApplied / provider=Stooq`
3. Provider order is now backups-first:
   - `ProviderOrder / providers=[Finnhub, Twelve Data, Tiingo, Yahoo Finance v8]`
4. Yahoo no longer burns the very first startup request on `DX-Y.NYB`.
5. Yahoo `429` events are reduced but not yet eliminated.
   - In this short confirmation run, Yahoo was only hit after Finnhub, Twelve Data, and Tiingo had already populated substantial tape data.
   - The remaining observed Yahoo `429` was:
     - `Spark batch failed for [VEA, SIZE, DGRO, XLY]`

Outcome:

1. The most wasteful Yahoo startup behavior is removed.
2. The runtime flow now gives healthier providers first right-of-way and keeps Yahoo as the unresolved-symbol fallback instead of the default first strike.
3. Remaining Yahoo `429` pressure is now narrower and more honest:
   - residual Yahoo retries for unresolved ETF/equity batches
   - not startup DXY warmup spam

## 46) Anti-429 Follow-On Pass (2026-04-25)

Trigger:

1. The first anti-429 pass removed startup DXY warmup abuse, but a later short VM run still showed a residual Yahoo `429` on the ordinary ETF/equity tail.
2. We wanted the runtime to prefer "stay partially live and let backups/cache carry the scene" over "immediately probe Yahoo again for general symbols".

Implementation:

1. Added a general-symbol Yahoo deferral rule in:
   - `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs`
2. New policy:
   - when live data is already flowing from backup providers
   - and a symbol is not in the dedicated Yahoo-only class
   - and the symbol is backup-eligible
   - Yahoo is no longer treated as urgent for that symbol in the same recovery pass
3. The backup-eligibility check now tolerates stale/restrictive profile caches by checking:
   - eligibility with profiles
   - eligibility without profiles
4. Added a focused regression test in:
   - `tests/PortfolioSaver.Tests/Services/StartupCoordinatorAdvancedTests.cs`
   - proving that once backup providers have already populated live data, the general Yahoo tail is deferred instead of being hit immediately.

Host validation:

1. Focused coordinator suite passed again:
   - `28/28`
2. Fresh publish succeeded again:
   - `build/publish-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300`

Fresh VM confirmation:

1. Launcher run:
   - `build/vm/artifacts/launcher-runs/20260425-173050`
2. Result bundle:
   - `build/vm/artifacts/vm-results/ux-deep-20260425-133201`
3. Trace bundle:
   - `build/vm/artifacts/trace/ux-deep-20260425-133201-trace`

Trace findings from this exact pass:

1. Startup Yahoo warmup is still absent.
2. Macro/world-index lanes still populate first from native providers:
   - `OfficialMacroQuotesApplied`
   - `TreasuryMacroQuotesApplied`
   - `GlobalMarketQuotesApplied / provider=Stooq`
3. Runtime provider order remains backups-first:
   - `providers=[Finnhub, Twelve Data, Tiingo, Yahoo Finance v8]`
4. In the latest pass, Yahoo was not rate-limited in the observed recovery loop.
   - Instead, after Finnhub/Twelve Data/Tiingo populated the scene, Yahoo was planned but then skipped by budget/cooldown:
     - `Provider Yahoo Finance v8 skipped by budget/cooldown for [IJH, MTUM, VIG, XLV]`
5. Resulting scene state remained healthy:
   - `result_quote_count=48`
   - `macro_missing_symbols=[]`
   - `world_index_missing_symbols=[]`

Outcome:

1. The current anti-429 behavior is materially healthier than the earlier baseline:
   - no startup DXY/Yahoo warmup hit
   - no observed Yahoo `429` in the newest short validation pass
   - macros and world indices still populate
2. Residual unresolved tail now degrades into cache-backed partial state rather than immediate Yahoo failure pressure.

## 47) Global Markets Gap + 20-Minute Trace Optimization Pass (2026-04-25)

Trigger:

1. The Global Markets tape still showed an oversized visual gap near the pinned New York card.
2. A prior 20-minute trace proved two remaining runtime problems:
   - macro/world values could blank before the next steady-state refresh
   - already-fresh macro/global symbols were being re-polled during one-minute recovery passes

Implementation:

1. Reduced the Global Markets sequence lead-in spacing in:
   - `src/PortfolioSaver.Render/Controls/GlobalMarketsTapeControl.xaml.cs`
2. Added a shared quote refresh/freshness policy in:
   - `src/PortfolioSaver.Screensaver/Services/QuoteRefreshPolicy.cs`
3. Unified the runtime planner and UI stale checks on the same effective steady-state window:
   - `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs`
   - `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs`
4. Important behavioral change:
   - even if the saved config contains a shorter quote interval such as `60s`
   - the runtime now treats quote freshness against the real enforced steady-state cadence instead of the raw config value
   - this prevents already-good macro/global quotes from being marked stale or re-polled too early
5. Added regression coverage in:
   - `tests/PortfolioSaver.Tests/Services/StartupCoordinatorAdvancedTests.cs`
   - `tests/PortfolioSaver.Tests/Services/ScreensaverRenderBehaviorTests.cs`

Host validation:

1. Focused suite passed:
   - `dotnet test .\\tests\\PortfolioSaver.Tests\\PortfolioSaver.Tests.csproj --filter "FullyQualifiedName~StartupCoordinatorAdvancedTests|FullyQualifiedName~ScreensaverRenderBehaviorTests" --no-restore -v minimal`
   - Result: `61/61` passing
2. Fresh publish succeeded:
   - `build/publish-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300`

Fresh 20-minute VM validation:

1. Launcher run:
   - `build/vm/artifacts/launcher-runs/20260425-182516`
2. Result bundle:
   - `build/vm/artifacts/vm-results/ux-deep-20260425-142626`
3. Trace bundle:
   - `build/vm/artifacts/trace/ux-deep-20260425-142626-trace`
4. Summary:
   - `ConfigPhaseStatus=Completed`
   - `ScreensaverPhaseStatus=Completed`
   - `ConfigVersionCheck=Passed`
   - `ScreensaverVersionCheck=Passed`
   - `PlannedScreensaverDurationMinutes=20`

Trace findings from this exact 20-minute pass:

1. The startup recovery ladder converged cleanly:
   - first pass due symbols: `48`
   - second pass due symbols: `20`
   - third pass due symbols: `8`
   - after that the scene reached `result_quote_count=48`
2. The native side providers were no longer re-polled every minute once their symbols were already fresh.
   - `OfficialMacroQuotesApplied`, `TreasuryMacroQuotesApplied`, and `GlobalMarketQuotesApplied / provider=Stooq` appeared only in the first startup pass
3. The provider layer stayed clean for the full 20-minute session.
   - observed provider tries: Finnhub `2`, Twelve Data `3`, Tiingo `3`
   - observed provider returns: Finnhub `2`, Twelve Data `3`, Tiingo `3`
   - observed provider failures: none
   - observed provider skips: none
   - observed Yahoo `429` events: none
4. The old 15-minute macro dropout was eliminated.
   - macro snapshots remained populated through the end of the 20-minute run
   - no later `missing_symbols=[^VIX, ^VIX3M, US2M, US10Y, DX-Y.NYB]` regression appeared
5. The Global Markets tape no longer shows the previous oversized seam/gap behavior.

Outcome:

1. The quote freshness logic now matches the actual enforced steady-state refresh cadence.
2. Macro/world data remain visible across the full steady-state window instead of blanking early.
3. The recovery planner now spends follow-up passes only on unresolved symbols instead of re-querying already-fresh native macro/global lanes.
4. In the latest 20-minute VM run, provider refusals were effectively reduced to zero.

### 43. Tape waiting cue, UTC pinning, and 20-minute validation follow-up (2026-04-25)

What changed:

1. Tape items that are still waiting for live quote values now show a small clock glyph in the value slot itself instead of leaving an empty hole.
2. The waiting glyph now disappears cleanly when the value arrives because it is bound to the same item state as the pending value.
3. Short-tape filler logic was improved so longer source lists rotate later fill cycles instead of replaying the exact same order immediately.
4. The top-right status clock is now pinned to `UTC` and uses authoritative NTP-adjusted UTC when an offset from `pool.ntp.org` is available.
5. The extra top-left provider line was removed from the visible status bar.
6. The `Source: Local clock` subtitle was removed from the visible Global Markets tape.
7. Added more bundled/background-catalog city images, including new CC0/public-domain-friendly London and Shanghai skyline starters plus an extra Sydney public-domain runtime catalog entry.

Local validation:

1. Full unit suite passed:
   - `dotnet test .\\tests\\PortfolioSaver.Tests\\PortfolioSaver.Tests.csproj --no-restore -v minimal`
   - Result: `181/181` passing

Fresh 20-minute VM validation:

1. Launcher run:
   - `build/vm/artifacts/launcher-runs/20260425-223353`
2. Result bundle:
   - `build/vm/artifacts/vm-results/ux-deep-20260425-183504`
3. Trace bundle:
   - `build/vm/artifacts/trace/ux-deep-20260425-183504-trace`
4. Summary:
   - `ConfigPhaseStatus=Completed`
   - `ScreensaverPhaseStatus=Completed`
   - `ConfigVersionCheck=Passed`
   - `ScreensaverVersionCheck=Passed`
   - `PlannedScreensaverDurationMinutes=20`

Evidence from the latest 20-minute run:

1. The waiting icons now render in the value slot itself and disappear as values arrive.
   - confirmed visually in `build/vm/artifacts/vm-results/ux-deep-20260425-183504/screensaver-001.png`
2. The top-right clock shows `UTC` and the date/time line split remains intact.
3. The extra provider line is gone from the visible status band.
4. The Global Markets subtitle/source line is gone.
5. Background count increased to `9` on this run, reflecting the expanded exchange/city background pool.

Provider/trace findings from this exact 20-minute pass:

1. Startup still converged in the same healthy three-stage ladder:
   - pass 1 reached `result_quote_count=28`
   - pass 2 reached `result_quote_count=40`
   - pass 3 reached `result_quote_count=48`
2. Macro and world-index lanes were resolved natively first:
   - `OfficialMacroQuotesApplied`
   - `TreasuryMacroQuotesApplied`
   - `GlobalMarketQuotesApplied / provider=Stooq`
3. No provider refusals were observed in the reconstructed 20-minute trace:
   - no `WARN`
   - no provider `failed`
   - no provider `skipped`
   - no Yahoo `429`
4. The current conservative provider ordering remains the recommended operating point because it achieved full convergence without refusals.

Recommendation after this pass:

1. Keep the current backup-first provider ladder and 4-symbol batch shape.
2. Do not make Yahoo more aggressive again unless a future long trace shows a real stale-data regression.
3. If we want still more polish, the next tape-specific improvement should be purely visual spacing/curation, not more provider churn.
