<!--
============================================================================
Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
Proprietary rights reserved except as expressly licensed herein.

DO NOT PANIC PORTFOLIO VISUALIZER
This file is governed by the SANYALnet Labs Non-Commercial License in the
root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
for AI/ML model training are prohibited unless separately authorized.

Attribution is required: "Based on original work by Supratim Sanyal of
SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
patent, trademark, and governing-law provisions.
============================================================================
-->

# Manual UI QA Results - 2026-05-18

> Historical validation record. Paths and product terminology below describe
> the environment and implementation as they existed on the stated date; use
> `docs/AUDIT_STATE.json` for current release state.

## Execution summary

- Canonical desktop-first VM proof bundle at execution time:
  - `build/vm/artifacts/ssh-runs/ux-deep-ssh-20260518-104146/ux-deep-summary.json` (historical generated artifact; not retained in Git)
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

- `MainWindowViewModelValidationTests`
- `NewsFeedValidationServiceTests`
- `SettingsValidatorTests`
- `DesktopShellMigrationTests`
- `VmHarnessScriptTests`

## Results by area

### Runtime and key surfaces

- `Market data source` summary: pass
- `DeepSeek API key`: pass

Observed behavior:
- market data is now described as a fixed YFinance.NET-only runtime lane
- no retired provider-key editing remains in the General tab
- DeepSeek remains the only configurable external key surface

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

### Market data runtime card

- fixed YFinance.NET runtime messaging: pass

Observed behavior:
- advanced tab documents the YFinance.NET runtime profile instead of editable provider budgets

### Global resilience checks

- validate-only workflow: pass
- offline lock/retry path: pass
- blank DeepSeek key behavior: pass
- placeholder API key behavior: pass
- invalid RSS URL behavior: pass
- invalid ticker auto-disable: pass
- validated save/close sequence: pass

## New defects found

None beyond the already-tracked backlog during this pass.

## Closing note

This pass establishes a documented QA workbook plus a concrete execution record tied to the desktop-first canonical harness flow.
