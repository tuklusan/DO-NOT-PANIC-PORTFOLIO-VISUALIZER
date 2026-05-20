# Manual UI QA Results - 2026-05-19

## Execution summary

- Canonical desktop-first VM proof bundle:
  - [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\artifacts\ssh-runs\ux-deep-ssh-20260519-130940\ux-deep-summary.json](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\artifacts\ssh-runs\ux-deep-ssh-20260519-130940\ux-deep-summary.json)
- Key proof values:
  - `ConfigPhaseStatus = Completed`
  - `DesktopPhaseStatus = Completed`
  - `ConfigVersionCheck = Passed`
  - `DesktopVersionCheck = Passed`
  - `FullScreenToggleStatus = Completed`
  - `ConfigShots = 2`
  - `DesktopShots = 242`

## Focused validation set

Executed locally:

- `YahooSymbolValidationServiceTests`
- `MainWindowViewModelValidationTests`
- `NewsFeedValidationServiceTests`
- `SettingsValidatorTests`
- `DesktopShellMigrationTests`
- `VmHarnessScriptTests`
- `ConfigTextConsistencyTests`
- `InternetProbeServiceTests`

Result:

- `160 / 160` passing after the retired multi-provider tests were removed from the active beta lane

## Results by area

### Runtime and key surfaces

- `Market data source` summary: pass
- `DeepSeek API key`: pass

Observed behavior:

- market data is now explicitly presented as YFinance.NET-only
- no retired provider-key validation remains in the General tab
- during validation, symbol progress is logged in the transient validation-progress window

### Refresh sliders

- `Portfolio refresh`: pass
- `Off-hours refresh`: pass
- `Background change interval`: pass
- `Headline refresh`: pass

Observed behavior:

- slider values remain bounded by control limits
- visible bound-value text updates with slider movement
- persisted out-of-range values remain covered by validation tests

### Background controls

- `Managed cache folder`: pass
- `Use my image directory instead of exchange photos`: pass
- `Custom image folder`: pass

Observed behavior:

- custom folder textbox enables only when the custom-folder checkbox is checked
- managed cache folder remains read-only

### Portfolio tapes

- `Add Tape`: pass
- `Remove Tape`: pass
- tape name / enabled / direction / speed: pass
- `Add Ticker`: pass
- ticker symbol / display name / enabled / remove: pass
- validation badge behavior: pass

Observed behavior:

- validation badges remain pending until `Validate`
- no background symbol-validation churn occurs during idle edits
- symbol existence is now trusted from recent local quote/profile evidence when available, avoiding unnecessary YFinance.NET validation bursts

### News scroller controls

- summarized/RSS mode switching: pass
- DeepSeek style radios: pass
- RSS URL field enable/disable: pass
- headline refresh slider: pass

Observed behavior:

- summarized mode does not require a valid RSS URL
- RSS mode resets invalid/unreachable URLs to the default feed

### Market data runtime card

- fixed YFinance.NET runtime messaging: pass

Observed behavior:

- advanced tab now documents the fixed YFinance.NET runtime profile instead of exposing provider budgets
- 1-second pacing and 10-minute freshness ceiling are explained in the UI

### Global resilience checks

- validate-only workflow: pass
- offline lock/retry path: pass
- blank DeepSeek key behavior: pass
- retired market-data provider key gating removed: pass
- invalid RSS URL behavior: pass
- invalid ticker auto-disable: pass
- validated save/close sequence: pass
- `Validate` disabled during validation: pass
- validation-progress window lifecycle: pass
- desktop scene pause/resume around config session: pass

## Trace evidence

The canonical trace confirms the intended validation flow:

- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\artifacts\ssh-runs\ux-deep-ssh-20260519-130940\trace\trace.circular.log](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\artifacts\ssh-runs\ux-deep-ssh-20260519-130940\trace\trace.circular.log)

Key lines show:

- `Desktop.Config / ConfigDialogOpening`
- `Scene paused for config session`
- `TickerValidationTrustPlan / requested_count=32 / trusted_count=32`
- `TickerValidationNetworkPlan / network_symbol_count=0`
- `ApiValidationProgress ... Validated`
- `ValidationPassed`
- `Scene resumed after config session`
- `Desktop.Config / ConfigDialogClosed`

## New defects found

None during this pass.

## Closing note

This rerun upgrades the earlier QA workbook closure to the current stabilized config-validation flow and confirms that the project is ready to begin broader UI QA execution from the desktop-first canonical harness.



