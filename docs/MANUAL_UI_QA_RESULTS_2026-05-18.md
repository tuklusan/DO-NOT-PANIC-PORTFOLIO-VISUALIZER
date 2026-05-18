# Manual UI QA Results - 2026-05-18

## Execution summary

- Canonical desktop-first VM proof bundle:
  - [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\artifacts\ssh-runs\ux-deep-ssh-20260518-104146\ux-deep-summary.json](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\artifacts\ssh-runs\ux-deep-ssh-20260518-104146\ux-deep-summary.json)
- Key proof values:
  - `ConfigPhaseStatus = Completed`
  - `DesktopPhaseStatus = Completed`
  - `ConfigVersionCheck = Passed`
  - `DesktopVersionCheck = Passed`
  - `FullScreenToggleStatus = Completed`
  - `ConfigShots = 211`
  - `DesktopShots = 242`

## Focused validation set

Executed locally:

- `ApiKeyValidationServiceTests`
- `MainWindowViewModelValidationTests`
- `NewsFeedValidationServiceTests`
- `SettingsValidatorTests`
- `DataSourcePolicyValidationTests`
- `DesktopShellMigrationTests`
- `VmHarnessScriptTests`

## Results by area

### Provider key fields

- `Finnhub API key`: pass
- `Twelve Data API key`: pass
- `Tiingo API key`: pass
- `Financial Modeling Prep API key`: pass
- `EODHD API key`: pass
- `DeepSeek API key`: pass

Observed behavior:
- blank provider keys fail validation with explicit `API key is required` messages
- installer/sample placeholder keys fail validation with explicit replacement messaging
- DeepSeek key is configured in UI but is not part of provider-key validation service gating

### Refresh sliders

- `Portfolio refresh`: pass
- `Off-hours refresh`: pass
- `Background change interval`: pass
- `Headline refresh`: pass

Observed behavior:
- slider values are bounded by control minimum/maximum
- settings validation covers out-of-range persisted values separately

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
- validation badge remains pending until `Validate`
- no background symbol-validation churn occurs during idle edits
- invalid symbols are disabled and called out clearly

### News scroller controls

- summarized/RSS mode switching: pass
- DeepSeek style radios: pass
- RSS URL field enable/disable: pass
- headline refresh slider: pass

Observed behavior:
- summarized mode does not require a valid RSS URL
- RSS mode resets invalid/unreachable URLs to the default feed

### Data source policy grid

- hourly budget fields: pass
- daily budget fields: pass
- single/multi query toggles: pass

Observed behavior:
- unsupported combinations are rejected by validation
- out-of-range values are clamped in the editor model and rejected by validation when necessary

### Global resilience checks

- validate-only workflow: pass
- offline lock/retry path: pass
- blank API key behavior: pass
- placeholder API key behavior: pass
- invalid RSS URL behavior: pass
- invalid ticker auto-disable: pass
- validated save/close sequence: pass

## New defects found

None beyond the already-tracked backlog during this pass.

## Closing note

This pass establishes a documented QA workbook plus a concrete execution record tied to the desktop-first canonical harness flow.
