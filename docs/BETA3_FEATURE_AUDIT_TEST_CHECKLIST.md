# BETA-3 Feature Audit + Automated Test Checklist

Date: 2026-04-10  
Scope: verification against rough scratch notes provided in chat

Automated suite run:

```powershell
dotnet test .\PortfolioScreensaver.sln -c Release --nologo
```

Result: **Passed 45 / Failed 0 / Skipped 0**

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
| F-041 | “No network” detection by pinging baidu 5x | Implemented | `src/PortfolioSaver.Shared/Services/InternetProbeService.cs:18-77`, `src/PortfolioSaver.Config/Services/ConfigConnectivityService.cs:5-11`, `src/PortfolioSaver.Screensaver/Services/NetworkAvailabilityService.cs:5-11` | T-041 |
| F-042 | If no update for 15 minutes -> yellow + blank values + card removed | Implemented | stale threshold + blank value handling in `src/PortfolioSaver.Screensaver/Services/StartupCoordinator.cs:663-699`; graph hide/blank behavior in `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs:1735-1742` | T-042 |
| F-043 | Round macro meters (VIX/VIX3M/yield spread/rate flows) | Implemented | `src/PortfolioSaver.Render/ViewModels/MacroMeterViewModel.cs`, `src/PortfolioSaver.Render/Controls/StatusBarControl.xaml:47`, `src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs:736-793` | T-043 |
| F-044 | Visual polish across very small to ultrawide screens | Partial | responsive logic exists (`src/PortfolioSaver.Screensaver/Controls/ScreensaverSceneControl.xaml.cs:515-550`) but no automated multi-resolution regression suite yet | T-044 |
| F-045 | Baseline/version text reflects BETA-3 | Superseded | runtime baselines moved beyond BETA-3; see `src/PortfolioSaver.Shared/PortfolioVersion.cs:7`, `src/PortfolioSaver.Config/Windows/MainWindow.xaml:9`, `src/PortfolioSaver.Config/Content/about.txt:3` | T-045 |

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
| T-009 | Unit (Config VM) | F-008 | Invalid symbol after validation should auto-uncheck `Enabled` (currently expected to fail; documents gap) | TODO |
| T-010 | Unit (Config VM) | F-003 | Validate->OK gate and fingerprint invalidation when edits occur | TODO |
| T-011 | Unit (Config VM + group editor) | F-009, F-010 | Enforce 4 tapes and 8 tickers/tape in UI view-model layer | TODO |
| T-012 | UI automation (WinAppDriver) | F-012 | Verify all main sections display help badges and tooltips | TODO |
| T-013 | UI automation | F-013 | Help/About buttons open document dialog with non-empty content | TODO |
| T-014 | Unit (NewsFeedValidationService) | F-014 | Invalid/non-RSS URL resets to default Yahoo feed | TODO |
| T-015 | Unit (DataSourcePolicy editor + validator) | F-015 | Clamp and reject out-of-bound per-hour/day values and unsupported batch flags | TODO |
| T-016 | UI automation (resize) | F-016, F-017 | Resize config window across breakpoints and assert advanced grid columns remain visible/usable | TODO |
| T-017 | UI automation (resize) | F-017 | General tab controls remain usable without clipped primary actions | TODO |
| T-018 | UI automation | F-018 | Confirm no "Show indexes" toggle exists and help text explains behavior | TODO |
| T-019 | Integration (mock Yahoo provider) | F-019 | Startup warmup emits incremental batches with ~5s gaps | TODO |
| T-020 | Integration (mock providers) | F-020 | Yahoo first, rotated backup order by seed, fallback when provider fails | TODO |
| T-021 | Unit (ProviderBudgetLedgerService) | F-021 | 15-second minimum reuse is enforced per provider entry | TODO |
| T-022 | Unit (ProviderBudgetLedgerService) | F-022 | Hour/day budget ceiling enforcement with cooldown behavior | TODO |
| T-023 | Unit | F-023 | Provider eligibility rejects unsupported symbol shapes per source | TODO |
| T-024 | Integration (mock HTTP handler) | F-024 | Yahoo session obtains cookie+crumb and retries on invalid crumb/cookie responses | TODO |
| T-025 | Integration (mock HTTP handler) | F-025 | Yahoo quote path uses spark batch and chart fallback when spark unavailable | TODO |
| T-026 | Integration (mock HTTP handler) | F-026 | Historical path uses spark batch first and chart fallback per symbol | TODO |
| T-027 | Render integration | F-027, F-028 | Alternating directions and wrap-copy counts ensure no blank tape space | TODO |
| T-028 | Render integration | F-028 | Track rebuild not triggered by value-only updates (anti-jitter regression) | TODO |
| T-029 | Render integration | F-029 | News sequence wraps and fills width without blank gaps | TODO |
| T-030 | Unit (StartupCoordinator) | F-030 | Stale/missing quote yields yellow symbol color | TODO |
| T-031 | Render integration | F-031 | Value flash triggers once per update and appears on all repeated instances | TODO |
| T-032 | Render integration | F-032 | Graph card flash performs two pulses on quote change | TODO |
| T-033 | UI automation | F-033 | Network waiting overlay appears and bounces when network unavailable | TODO |
| T-034 | Unit/UI | F-034 | Clock builder emits 12 cells; local summary + 11 exchange cards with weather/time/index fields | TODO |
| T-035 | Unit | F-035 | NY status string includes "(New York)" and open/close countdown format | TODO |
| T-036 | Integration (calendar service) | F-036, F-037 | FMP/EODHD merge + cache + offline fallback; international status formatting | TODO |
| T-037 | Unit (calendar parser) | F-036 | Guard against false "closed holiday" positives (`IsClosedHoliday`) | TODO |
| T-038 | Integration (photo cache service) | F-038 | Bundled seed copy + one-by-one Wikimedia fetch + attribution file write | TODO |
| T-039 | Installer integration (sandbox/VM) | F-039, F-040 | Install/uninstall validates files, registry, and LocalAppData cache cleanup | TODO |
| T-040 | Integration | F-041 | Validate baidu 5x probe + probe cache invalidation behavior | TODO |
| T-041 | Integration/UI | F-042 | Validate stale>15m yellow symbol, blank values, and hidden graph cards | TODO |
| T-042 | UI/Render | F-043 | Validate macro round meter data binding and visuals | TODO |
| T-043 | UI automation (multi-resolution) | F-044 | 1366x768, 1920x1080, 3440x1440 layout consistency assertions | TODO |
| T-044 | Unit/UI text checks | F-045 | Current baseline/version/help/about/window-title labels remain internally consistent | TODO |

## 3) Immediate Risks Found During Audit

1. `help.txt` still contains stale wording (`16 tickers`, `auto-uncheck`) versus current runtime behavior.
2. `IsClosedHoliday` currently returns `true` by default in `ExchangeMarketCalendarService` (`line 503`), which may over-mark holidays when payload fields are ambiguous.
3. Multi-resolution and resize regressions still rely on manual verification; automated coverage is pending (`T-016`, `T-017`, `T-043`).
4. Macro meters and stale-card behavior are present but still need targeted automated regression tests (`T-041`, `T-042`).


