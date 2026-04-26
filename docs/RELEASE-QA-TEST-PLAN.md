# PortfolioSaver Release QA Test Plan

## Goal

Certify the installer, configuration applet, runtime scheduler, and screensaver visuals for a general-public beta release across common desktop and laptop use cases.

## Release Gates

- Installer install, repair, and uninstall succeed without manual file cleanup.
- Config UI saves valid settings, blocks invalid symbols when online, and warns gracefully when offline.
- Runtime scheduler honors provider budgets and only routes symbols to providers that are known or profiled as eligible.
- Screensaver remains visually readable across standard and ultrawide layouts without overlapping core elements.
- Cached quotes, headlines, symbol profiles, exchange-photo cache, and history cache behave correctly across restart, offline operation, and uninstall.

## Test Environments

### Primary

- Host Windows 10/11 with Windows Sandbox.
- Standard desktop scale factors: 100%, 125%, 150%.
- Config window resized to a compact laptop-like width and to standard/full desktop widths.

### Secondary

- Real hardware or VM pass on:
  - 1366x768 or 1920x1080 14" laptop-class display
  - 1920x1080 22" desktop display
  - 2560x1080 or wider ultrawide display

### Note

Windows Sandbox is good for install/config/runtime isolation, but not for exact hardware-display-profile certification. The current harness can resize app windows and reuse a persistent sandbox, but final cross-resolution signoff still benefits from at least one real or VM-based multi-resolution pass.

## Symbol Matrix

Exercise symbols from several classes in the same configuration:

- Common equities and ADRs:
  - `AAPL`
  - `BRK.B`
  - `TSM`
- ETFs:
  - `SPY`
  - `QQQ`
  - `VNQ`
  - `XLRE`
- Mutual fund / money market:
  - `VTSAX`
  - `SWVXX`
- Indexes:
  - `^GSPC`
  - `VIX`
  - `TNX`
  - `DXY`
- Futures:
  - `ES=F`
  - `NQ=F`
  - `ZN=F`
  - `CL=F`
  - `GC=F`
- FX / crypto:
  - `EURUSD=X`
  - `JPY=X`
  - `BTC-USD`
  - `ETH-USD`

## Config UI Coverage

### General UI

- Launch config app from installed location.
- Verify General and Advanced tabs render without clipped controls.
- Resize config window down to compact laptop width and confirm:
  - no button row overlap
  - no text inputs clipped beyond usability
  - scroll behavior still reaches all sections
- Resize back to full width and confirm layout reflows cleanly.

### Multi-Pass UX Methodology (Current)

- Pass 1: visual capture sweep (`compact`, `medium`, `large`) for config and timed screensaver captures (`12s`, `36s`, `66s`).
- Pass 2: control-bound validation with WinAppDriver/UIA (tab selection, footer button bounds, clipping/overlap checks).
- Pass 3: resolution-truth validation (requested profile vs actual screenshot dimensions).
- Pass 4: technical trace against XAML/layout code for each observed defect.

### Validation Flow

- Edit one valid symbol to another valid exotic symbol through the UI and Apply.
- Edit one symbol to an invalid token and verify Apply is blocked with a warning dialog.
- Correct the invalid symbol and Apply successfully.
- Repeat once with network disabled and confirm warning rather than hard failure.

### Data Source Policy

- Change per-hour and per-day values at low, medium, and near-maximum settings.
- Toggle single and batch query checkboxes where supported.
- Confirm invalid limits are blocked by validator.
- Confirm policy values persist after closing and reopening config.

### Feed and Speed Controls

- Change news feed URL and refresh slider.
- Change each tape speed slider.
- Confirm values persist after reopen.

### Tape Configuration

- Rename tapes with short and max-length names.
- Disable one or more tapes and confirm empty tape rows are not rendered.
- Add and remove tape symbols and benchmarks.

## Scheduler and Data Routing Coverage

- Validate a mixed symbol set while online and confirm `symbol-profiles.json` is written.
- Confirm profiled provider support is reused by runtime scheduler.
- Verify futures, index, money-market, crypto, and FX symbols are not sent to providers that the app cannot currently support for those symbols.
- Confirm provider cooldown behavior after simulated or real `429` responses.
- Confirm cached profiles allow offline warning-only Apply behavior.

## Screensaver Runtime Coverage

### Visual Warmup

- Start from empty quote cache and observe progressive card hydration.
- Restart with warm cache and confirm tapes/graphs populate immediately from cache.
- Confirm each graph card appears only once fully placed, not half-hidden at spawn.

### Tapes and News

- Confirm all visible tapes are active when configured.
- Confirm tape content repeats back-to-back with no blank segments.
- Confirm left/right tape motion remains continuous after live updates.
- Confirm finance news headlines repeat continuously with no blank gaps.

### Cards and Overlays

- Confirm graph cards and clock card bounce slowly and stay fully visible.
- Confirm the offline overlay appears when network is unavailable.
- Confirm world-clock/weather card remains legible and distinct from graph cards.

### Backgrounds

- Confirm bundled exchange photos load immediately on first run.
- Confirm managed cache downloads additional exchange photos online.
- Confirm background transition effects remain smooth and non-jarring.

## Cache and Uninstall Coverage

- Confirm quote cache survives restart.
- Confirm symbol-profile cache survives restart.
- Confirm background cache survives restart and is removed on uninstall.
- Confirm historical cache survives restart and is removed on uninstall.
- Confirm uninstall removes `.scr`, manifest, uninstall key, and managed caches.

## Evidence to Collect

- Config screenshots at compact and standard widths.
- Config screenshots for both General and Advanced tabs at each width profile.
- Managed-mode screensaver screenshots at early, mid, and warm states.
- Offline overlay screenshot.
- Runtime log showing provider routing decisions.
- Install/uninstall state validation output.

## Known UX Findings (2026-04-11 Audit)

- Footer command row can wrap/crowd at compact widths, reducing readability and operability.
- Current VM automation does not reliably capture Advanced-tab images because of stale window/tab targeting in the runner script.
- Top-band screensaver overlays (status/warmup/macro chips/clock) can crowd each other during warmup.
- Resolution-labeled artifact folders may not reflect true guest framebuffer resolution without explicit validation.
- Warmup UX currently appears delayed/non-incremental from user POV (cached placeholder continuity and per-batch visual updates need explicit verification).
- Tape motion smoothness and per-tape speed differentiation appear regressed in current runtime behavior.
- Update flash cues (ticker and graph-card) appear inconsistent/regressed and need focused visual regression checks.
- Clock card interior content (indices/sparklines) can truncate under top-lane crowding.
- Background transitions can visually jitter foreground overlays, suggesting transition coupling issues.
- Slow background zoom/parallax behavior appears regressed and needs explicit runtime verification.
- Macro indicator visuals should be speedometer-style gauges (runtime currently not matching expected style).
- All clock displays should use fixed-size 7-segment LED typography consistently.
- Yellow stale-state dwell appears too long during warmup and needs stale-policy tuning verification.

## Online Reference Set

These sources were consulted while shaping the symbol matrix and QA scope:

- Windows Sandbox configuration: <https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file>
- Twelve Data docs and asset coverage references: <https://twelvedata.com/docs>
- Tiingo product/docs entry points: <https://www.tiingo.com/> and <https://api.tiingo.com/>
- CME futures reference examples: <https://www.cmegroup.com/>
- Cboe VIX reference: <https://www.cboe.com/tradable_products/vix/>
- Schwab mutual fund / money-market reference examples: <https://www.schwabassetmanagement.com/>
