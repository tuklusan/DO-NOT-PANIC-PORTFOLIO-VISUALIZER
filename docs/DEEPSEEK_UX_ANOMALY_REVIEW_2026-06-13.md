# DeepSeek UX Anomaly Review

Date: 2026-06-13
Scope: CR-064 through CR-085 degraded/anomalous-situation validation tickets
Outcome: Additional UX-specific validation tickets CR-086 through CR-091 were created.

## Codex Scan Result

CR-064 through CR-085 already included broad user-visible behavior requirements such as expected user-visible behavior, UI responsiveness, screenshots/VM validation, stale/fresh truthfulness, no blank scenes, no clipping/layout runaway, and config closeability. However, the suite did not fully separate user-experience semantics from functional resilience. DeepSeek was asked to identify missing UX checks.

## Added Tickets

- CR-086: Define user-facing status indicators and error messaging for degraded modes
- CR-087: Validate accessibility of anomaly states for screen readers, high contrast, and keyboard users
- CR-088: Validate clear data freshness indicators and live/stale/recovery transitions
- CR-089: Validate degraded symbol placeholder UX and consistency across components
- CR-090: Validate user-friendly config validation error messages and progress feedback
- CR-091: Validate interactive element responsiveness during network degradation and latency

## DeepSeek Response

## CR-086 User-facing status indicators and error messaging during degraded modes
**Priority:** 1  
**Area:** ux_degradation_feedback  
**Severity:** High  

**Rationale:**  
The existing tickets focus on functional behavior (no crashes, correct data flow, trace logging) but do not specify what the *human user sees, hears, or can infer* during each anomaly. A user staring at a live dashboard has no access to trace logs; they rely on visual status indicators, placeholders, color codes, and explicit messages. Missing or misleading UI feedback can cause misinterpretation (e.g., believing stale data is fresh, or that the app has crashed when it’s actually waiting for network). This ticket explicitly defines the user-facing “face” of each degraded mode: what the top‑left market status shows, what ticker tapes and graph cards display for unavailable symbols, what the news scroller shows when DeepSeek fails, and how the world markets ribbon indicates partial failures. Without this, engineers may fix the data pipeline but leave the user confused.

**Notes:**  
- Cover every degraded scenario listed in CR‑064 through CR‑085 that has visible components.  
- For each component (market status, ticker tape, graph card, macro card, world markets card, news scroller, background, config window), define at least three states: **healthy**, **degraded/fallback**, and **offline/unavailable**.  
- Specifically:  
  - Top‑left status: “Live”, “Stale (cached)”, “Offline – data may be delayed”, “Timing unavailable” (blank).  
  - Ticker tape: Show symbol name with “N/A” in price/change columns when no fresh data, or show last cached value with a subtle visual cue (e.g., grey background, “stale” tag).  
  - Graph cards: Use a dashed outline or “no data” overlay when symbol fails; avoid blank white rectangle.  
  - News scroller: When DeepSeek fails, show “RSS feed” prefix or a brief fallback indicator (e.g., “(RSS)” before each headline).  
  - World markets ribbon: Each card should show a small icon or color band for unknown timing/quote status (e.g., orange dot for “unknown”, grey for “closed without data”).  
- Check that all messages are user‑actionable (e.g., “Network unavailable – check connection” vs “HTTP 429”).  
- Do **not** expose implementation details (JSON errors, server port, checksum failures).  

**Acceptance Criteria:**  
- A deterministic harness path exists to reproduce each degraded scenario and examine the UI visually (screenshots or screen reader output).  
- For each component, the expected healthy/degraded/offline display is documented in a reference table before implementation changes.  
- When a degradation is injected, the UI updates within 2 seconds to show the appropriate indicator.  
- No misleading visual – stale data is never shown with the same styling as fresh data.  
- All status text fits within the allocated UI space without clipping or overflow.  
- Trace output confirms the user‑face state matches the injected condition.  
- The app remains responsive; no dispatcher stall during indicator update.  

---

## CR-087 Accessibility of anomaly states for screen readers and assistive technology
**Priority:** 2  
**Area:** ux_accessibility_degradation  
**Severity:** Medium  

**Rationale:**  
None of the existing tickets address how anomalies are communicated to users relying on screen readers, high‑contrast themes, or keyboard navigation. Financial dashboards are used by a diverse workforce; a visually impaired user who depends on live quotes and status announcements will be severely impacted if degraded states are not voiced correctly. For example, a screen reader might announce a ticker tape symbol as “AAPL 150.00 up 2%” when the price is actually stale, or remain silent when a graph card fails. This ticket ensures that all visual degradation feedback (from CR‑086) is mirrored through accessibility APIs (e.g., `AutomationProperties`, LiveRegion for live ticker) and that fallback behaviors do not cause infinite loops or unclear announcements.

**Notes:**  
- Cover screen reader behavior for:  
  - Ticker tape updates: use `AutomationProperties.LiveSetting=”Polite”`; when symbol data degrades, announce “AAPL – data unavailable” instead of stale value.  
  - Graph cards: set `IsEnabled=false` on chart area and announce “No chart data for symbol”.  
  - News scroller: when scroller stops updating due to fallback, announce “News feed using RSS fallback”.  
  - Config window validation: ensure error messages are read; “Validate” button state changes are announced.  
  - Background selection: when an image fails to load, announce “Background image not available, using default”.  
- Test with Narrator (Windows) and NVDA.  
- Verify high‑contrast themes do not hide offline indicators (e.g., grey text on grey background is avoided).  
- Keyboard navigation must remain possible even when some cards show “no data” (focus should not jump unpredictably).  

**Acceptance Criteria:**  
- A deterministic harness path exists that simulates each degraded scenario and allows screen reader output capture.  
- For each component, expected screen reader output is documented in a table (e.g., “ticker AAPL offline: reads ‘AAPL unavailable’ with tone different from quote update”).  
- Injected network failure causes screen reader to announce “offline mode” within 5 seconds instead of repeating stale values.  
- No screen reader crash or hang when anomaly persists.  
- High‑contrast testing passes (visual indicators remain distinct).  
- Keyboard navigation order is preserved; Tab key moves only through interactive elements in a logical sequence.  

---

## CR-088 Clear data freshness indicators and transition announcements
**Priority:** 1  
**Area:** ux_freshness_communication  
**Severity:** High  

**Rationale:**  
Users cannot distinguish between stale cached data and fresh live data unless the UI explicitly tells them. CR‑074 (cache staleness) says “stale cache never labeled fresh” but does not define how the user learns the cache is stale. Without a visual or audible indicator, a user may make trading decisions based on out‑of‑date quotes. This ticket covers the entire lifecycle:  
- When the app launches offline and shows cached data, the top‑left status must say “Offline – using cached data from [time]”.  
- When the app was online but loses network during runtime, the transition from “Live” to “Stale” must be visually flagged (e.g., fade tape background to grey, or show a small clock icon).  
- When network recovers, the transition back to fresh data must be announced to the user (e.g., a brief flash of the status bar or a “Data now live” toast).  
- The graph card “flash” behavior must only occur on truly new price changes, not on cache refresh with the same value.  

**Notes:**  
- Integrate with CR‑086 (status indicators) to ensure consistency.  
- Specific scenarios:  
  - Cold start offline with cache → show “Cached data from HH:MM”.  
  - Online → offline after cache expiry → show “Offline – no recent data”.  
  - Recovery after cache expiry → when first fresh tick arrives, update all stale indicators and briefly highlight the new values.  
  - YFinance server latency >1s → while waiting, show a small spinner or “updating” icon on the affected symbol, not stale value.  
- The transition from stale to fresh must be smooth (no batch redraw) but noticeable. Consider a subtle green border animation on the graph card.  
- Ensure that the “last updated” timestamp is visible somewhere (tooltip or small label) for at least the ticker tape and top‑left status.  

**Acceptance Criteria:**  
- A deterministic harness exists that injects network loss and recovery at precise timestamps.  
- Screenshots or video capture shows the exact user‑visible state at each phase (live → stale → recovery → fresh).  
- The word “stale” or “cached” or equivalent is visible in the UI when data is not live.  
- The user‑facing timestamp is accurate and updated only when fresh data arrives.  
- Graph cards do not flash when old cached data is re‑rendered.  
- No toast or visual transition lasts longer than 3 seconds.  
- Trace output confirms that the freshness state change flag matches the UI.  

---

## CR-089 Degraded symbol placeholder UX and consistency across components
**Priority:** 2  
**Area:** ux_placeholder_consistency  
**Severity:** Medium  

**Rationale:**  
CR‑071 proves that per‑symbol failures are isolated, but it does not define what the user sees in place of a failed symbol. Currently different components likely handle missing data differently (e.g., ticker tape might skip the symbol, graph card might show a blank rectangle, macro card might show “N/A”). Inconsistency confuses users: a missing ticker in the tape but a blank chart for the same symbol feels like a bug, not a deliberate degradation. This ticket mandates a unified placeholder strategy: each component must show a visually consistent, recognizable “no data” state that is clearly not a loading state or a crash. It also must be distinguishable from “stale data” (CR‑088).

**Notes:**  
- For each component, define a placeholder style:  
  - Ticker tape: show the symbol name with grey background and “unavailable” text in the price area.  
  - Graph card: overlay a centered icon (e.g., “—” or a warning triangle) and keep the card frame visible (so layout doesn’t collapse).  
  - Macro card: show “--” for numeric fields, keep label readable.  
  - World markets card: show “Closed/No data” and a grey circle.  
- Placeholders must be static (no animation) to differentiate from loading/updating.  
- The same symbol failing in multiple places must display the same placeholder style (e.g., ticker tape shows grey, graph card shows grey overlay).  
- Avoid showing implementation details like “null” or “404”.  
- Ensure that tooltips or “hover to see details” still work on the placeholder card (e.g., “Last successful quote: HH:MM”).  

**Acceptance Criteria:**  
- A harness injects per‑symbol failure for two symbols in a user portfolio and another in world markets.  
- Screenshots confirm that the failing symbols appear identically styled across all components they appear in.  
- Hovering over a placeholder reveals a tooltip with the last successful fetch time (if any).  
- The placeholder does not change size or shift layout when healthy symbols update.  
- No trace of error messages (like “HTTP 403”) is exposed in the UI.  

---

## CR-090 User‑friendly config validation error messages
**Priority:** 2  
**Area:** ux_config_error_clarity  
**Severity:** High  

**Rationale:**  
CR‑066 and CR‑067 cover config dialog behavior during network degradation, but they do not require that the error messages shown to the user are understandable by a non‑developer. Currently, the config window may expose implementation notes like “YFinance server startup failed on port 9999” or “HTTP 429 too many requests”. This ticket requires that all validation errors be translated into plain language and actionable advice. For example:  
- “Unable to verify symbol AAPL – network is unreachable. Please check your internet connection.”  
- “Validation cannot complete because the market‑data server did not start. Please try again later.”  
- “Too many requests – waiting before retry. The button will become active again in X seconds.”  
Additionally, the progress of validation (which symbol is being checked, overall percentage) should be visible in the dialog, especially during slow network (CR‑067).  

**Notes:**  
- Cover both General and Advanced tabs.  
- Error messages must be localized in the same language as the app.  
- Avoid technical terms like “crumb”, “checksum”, “port conflict”, “cache expiry”.  
- Provide a clear path forward: e.g., “Try again” button that re‑enables after a cooldown, or “Skip invalid symbols” option.  
- For stalled validation (CR‑067), the dialog should show a message like “Validation is taking longer than expected. You may close the window and try again later.”  

**Acceptance Criteria:**  
- A harness injects each type of validation failure (offline, DNS failure, server not starting, symbol not found, rate limit).  
- Screenshots of the config dialog show user‑readable messages that match the advice in the notes.  
- No messages exceed 2 lines in the dialog’s current font size.  
- The “Validate” button is always re‑enabled after failure, with a clear “Retry” label.  
- A progress indicator (e.g., “Checking symbol 3 of 10”) appears during validation.  
- Trace output confirms that the displayed message corresponds to a degraded condition entry.  

---

## CR-091 Interactive element responsiveness during network degradation
**Priority:** 2  
**Area:** ux_interactivity_degradation  
**Severity:** Medium  

**Rationale:**  
The existing tickets ensure the UI does not freeze or crash, but they do not verify that interactive elements (buttons, sliders, checkboxes) remain responsive and provide feedback during degraded modes. A user might click a button expecting immediate action, but if a network‑dependent callback is queued and delayed, the user may think the click didn’t register. This ticket focuses on:  
- Button click feedback: Immediate visual state change (e.g., button depressed, or a “pending” animation) even if the underlying action will complete later.  
- Slider/menu changes: UI updates local state instantly, then propagates to network‑backed features asynchronously.  
- Disabling of controls that are useless offline (e.g., “Validate” button should be greyed out with tooltip “No internet connection”).  
- Ensure that clicks are not lost during network‑induced dispatcher stalls (test with throttled network).  

**Notes:**  
- Cover all controls in the main scene (config button, background cycle, news mode toggle if any) and the config dialog.  
- For the config dialog, Validate button is already covered, but also check “Cancel”, “Apply”, “OK” during heavy network load.  
- Keyboard shortcuts (e.g., Escape to close dialog) must work even when background workers are retrying.  
- Visual feedback: a disabled button must look disabled; an active button must show a brief press animation.  

**Acceptance Criteria:**  
- A harness injects high latency (e.g., 5s delay) on every network call.  
- User clicks Validate -> button immediately shows a “checking” state (e.g., text changes to “Validating…” and becomes disabled until failure/complete).  
- User clicks Cancel during latency -> dialog closes immediately, no pending network cleanup blocks the close.  
- No control remains unresponsive for more than 500ms after user input.  
- Keyboard Escape works to close dialog even when a network request is outstanding.  
- Trace confirms UI events are processed within the same frame as input.
