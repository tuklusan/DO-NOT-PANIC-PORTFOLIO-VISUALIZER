# Manual UI QA Suite

## Purpose

This suite exercises every user-configurable surface in the configuration UI and verifies the desktop-first UX path, validation behavior, and resilience handling for deliberately awkward inputs.

## Canonical execution method

1. Run the desktop-first VM harness:
   - `build\vm\Push-VmWorkspace.ps1 -Bootstrap -IncludePublishArtifacts`
   - `build\vm\Invoke-VmBuildTest.ps1 -RunUxDeep -GuestScreensaverDurationMinutes 20 -TreatUxIssuesAsWarnings`
2. Review the pulled result bundle under:
   - `build\vm\artifacts\ssh-runs\<result-name>`
3. Pair the visual/control sweep with local focused validation tests that cover edge cases the harness touches only indirectly.

## Required evidence

- `ux-deep-summary.json`
- config screenshots for every tab/control sweep
- desktop screenshots for fullscreen/windowed proof
- local passing test output for the focused validation set
- a trace slice showing config open, validation progress, validation pass/fail, and natural config close

## Checklist

### General tab: provider keys

1. `Finnhub API key`
2. `Twelve Data API key`
3. `Tiingo API key`
4. `Financial Modeling Prep API key`
5. `EODHD API key`
6. `DeepSeek API key`

Checks:
- field is present and editable
- blank value behavior is understood
- installer/sample placeholder behavior is understood
- validation failure messaging is explicit

### General tab: refresh sliders

1. `Portfolio refresh`
2. `Off-hours refresh`
3. `Background change interval`

Checks:
- slider renders
- displayed bound value updates with slider
- minimum/maximum bounds are enforced by the control and validator

### General tab: background controls

1. `Managed cache folder` read-only path
2. `Use my image directory instead of exchange photos`
3. `Custom image folder`

Checks:
- custom folder textbox enables/disables with checkbox
- managed cache folder remains read-only

### General tab: portfolio tapes

Per tape:
1. tape name
2. enabled toggle
3. scroll direction
4. speed slider
5. add ticker
6. remove ticker

Per ticker row:
1. symbol
2. display name
3. enabled toggle
4. validation badge/message
5. remove action

Checks:
- add/remove controls function
- validation badge does not auto-run in background
- symbol validation occurs only through `Validate`
- invalid symbols are disabled clearly

### Advanced tab: news scroller

1. `Summarized Financial News`
2. `RSS Feed`
3. `Douglas Adams`
4. `William Shakespeare`
5. `RSS feed URL`
6. `Headline refresh`

Checks:
- radio-mode switching updates enabled state correctly
- DeepSeek style radios are enabled only in summarized mode
- RSS textbox is enabled only in RSS mode
- invalid RSS URL behavior is explicit

### Advanced tab: data source policy grid

Per provider row:
1. hourly budget
2. daily budget
3. single-query toggle
4. multi-query toggle

Checks:
- out-of-range values are normalized or rejected
- unsupported toggles are rejected

### Global resilience checks

1. validation happens only when `Validate` is clicked
2. internet unavailable state locks config editing and shows retry path
3. blank API keys produce explicit validation failure
4. installer/sample API keys produce explicit validation failure
5. invalid RSS URL resets to default in RSS mode
6. summarized mode does not require a valid RSS URL
7. invalid ticker symbols are disabled and called out
8. validated-save-close sequence behaves predictably after a successful validation
9. `Validate` is disabled while validation is running
10. a transient validation-progress window opens during validation, logs symbol/provider progress, and closes when validation ends
11. the desktop scene is paused for the config session and resumes afterward
12. ticker validation trusts recent local quote/profile evidence before falling back to Yahoo Finance

## Definition of done

The suite is complete when:

- every configurable control is listed above
- the desktop-first VM harness run completes cleanly
- the config control sweep produces screenshots across both tabs
- the focused validation test set passes
- the trace confirms config validation progress and natural close behavior
- the execution results are written down in a dated results document
