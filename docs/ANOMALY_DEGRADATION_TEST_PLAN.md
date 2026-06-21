# Anomaly and Degradation Validation Test Plan

Document status: Initial ticketization pass
Date: 2026-06-13
Applies to: CR-064 through CR-085

## Purpose

The healthy-path autonomous VM validation loop is now available. The next validation frontier is proving that the application behaves intentionally under degraded, anomalous, and exceptional conditions instead of merely passing when the internet, Yahoo/YFinance, DeepSeek, local storage, graphics, and harness infrastructure are healthy.

These tickets define a repeatable degraded-mode validation matrix. Each scenario should be reproducible through deterministic local or VM harness injection, should produce clear trace evidence, and should never rely on a lucky live outage.

## Test Tickets

### CR-064 - Add deterministic network fault-injection support for degraded-mode VM validation

- Priority: 1
- Severity: High
- Area: anomaly_test_harness
- Status: open
- Evidence / rationale:
  - The new autonomous VM loop proves healthy long-run behavior, but degraded conditions still require deterministic injection.
  - Scenarios include no internet at startup, DNS failure, connection refusal, TLS failure, HTTP throttling, latency, packet loss, and recovery after outage.
- Notes:
  - Prefer a host/VM controllable mechanism that can toggle failures at exact phases: before app launch, while config is open, while validation is running, while runtime quotes/news/backgrounds are active, and during shutdown.
  - The harness should record timestamps for each injected condition so trace analysis can line up symptoms and expected behavior.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-065 - Validate cold startup when internet is unavailable before app launch

- Priority: 1
- Severity: High
- Area: startup_network_degradation
- Status: closed
- Closure evidence: VM run `ux-deep-ssh-20260621-055258` completed config, desktop, and fullscreen phases under `FaultProfile=offline-at-start`; analyzer `visual-validation-ux-deep-ssh-20260621-055258.json` reported clean with 0 findings, runtime trace showed `OFFLINE - waiting for data`, and DeepSeek artifact advisory `deepseek-artifact-review-20260621-063454.md` found no deterministic blocker for the offline pathway.
- Evidence / rationale:
  - Startup is the most fragile time because quotes, news, background downloads, DeepSeek/RSS, upstream diagnostics, and YFinance server startup may all begin near each other.
  - The app must not hang, show misleading fresh values, or fail to render when launched fully offline.
- Notes:
  - Expected behavior should distinguish no-data-yet from stale cached data.
  - The top-left status, ticker tapes, graph cards, macro cards, world markets ribbon, and news scroller should each have intentional offline behavior.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-066 - Validate manual configuration workflow when internet is unavailable before opening settings

- Priority: 1
- Severity: High
- Area: config_network_degradation
- Status: closed
- Closure evidence: VM run `ux-deep-ssh-20260621-055258` completed config, desktop, and fullscreen phases under `FaultProfile=offline-at-start`; analyzer `visual-validation-ux-deep-ssh-20260621-055258.json` reported clean with 0 findings, and DeepSeek artifact advisory `deepseek-artifact-review-20260621-063454.md` found no deterministic blocker for the offline pathway.
- Evidence / rationale:
  - The config window may be opened while the machine is offline, before any validation starts.
  - Controls and validation affordances must remain understandable and the window must not get stuck.
- Notes:
  - Cover both General and Advanced tabs, including any controls that imply network-backed validation or generated runtime behavior.
  - The config UI should not expose implementation notes as error text.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-067 - Validate network loss or severe latency after Validate is clicked

- Priority: 1
- Severity: High
- Area: config_validation_degradation
- Status: closed
- Closure evidence: VM run `ux-deep-ssh-20260621-090801` completed config, desktop, and fullscreen phases under `FaultProfile=offline-during-config-validation`; remote build/test passed 478/478, analyzer `visual-validation-ux-deep-ssh-20260621-090801.json` reported clean with 0 findings, and DeepSeek artifact advisory `deepseek-artifact-review-20260621-094932.md` found no deterministic blocker.
- Evidence / rationale:
  - The Validate button is intentionally disabled immediately after click and later transitions to OK/Cancel only on success.
  - Network loss, DNS stalls, or YFinance latency during validation could otherwise leave the dialog stuck.
- Notes:
  - Exercise outage before first symbol, outage mid-symbol-list, slow responses, partial failures, and recovery before timeout.
  - This specifically protects the recently repaired OK/Cancel and immediate-close validation flow.
  - Harness-level screenshot coverage for the validation-error state remains a separate follow-up coverage gap; it was not a deterministic blocker for CR-067 closure.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-068 - Validate YFinance.NET server launch failures, duplicate process, port conflicts, and owned shutdown

- Priority: 1
- Severity: High
- Area: yfinance_server_lifecycle
- Status: closed
- Closure evidence: local deterministic lifecycle suite passed 29/29 on 2026-06-21 after commits b8266a3, 0797674, 6ef823e, and 9744b07.
- Evidence / rationale:
  - The UI now relies on owned YFinance.NET server/client communication for market data.
  - Port conflicts, duplicate stale server processes, failed server startup, or server crash can break every quote lane.
- Notes:
  - Include duplicate process check behavior requested during NB-031 work.
  - Cover desktop and config startup independently, plus shutdown when the UI closes.
- Expected user-visible behavior:
  - If an owned server is already reachable, desktop/config startup must continue normally without launching a duplicate server.
  - If the owned server bundle is missing or cannot launch, the desktop/config shell must remain responsive and trace `ServerLaunchFailed`/startup failure; market-data areas should remain blank, stale, partial, or unavailable rather than showing misleading fresh values.
  - If a non-server process occupies the YFinance.NET port, startup must not hang the dispatcher; the failure must be traceable as a server fatal/bind failure.
  - If the owning UI exits, the owned server must exit promptly so no stale hidden market-data server is left behind for the next run.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-069 - Validate asynchronous client/server disconnect, reconnect, malformed response, and late response handling

- Priority: 1
- Severity: High
- Area: yfinance_transport_degradation
- Status: open
- Evidence / rationale:
  - The protocol is asynchronous/pipelined; requests should continue on cadence and responses are processed when they arrive.
  - Disconnects, late responses, malformed frames, checksum failures, timestamp skew, and server exceptions need proof.
- Notes:
  - Include request/response/async-message timestamp and checksum expectations from the NB-031 ICD.
  - Ensure late responses cannot update the wrong symbol or cause batch UI redraws.
- Expected user-visible behavior:
  - A disconnected or malformed transport frame may fail the in-flight symbol request, but the UI must not freeze or batch-redraw unrelated symbols.
  - The next scheduled symbol request should reconnect through the normal client path and update only the symbol represented by its response.
  - Bad checksums, malformed frames, late responses, and async event integrity failures must be traced and must not apply data to the wrong ticker/card.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-070 - Validate quote latency greater than the one-second request cadence

- Priority: 1
- Severity: High
- Area: quote_latency
- Status: open
- Evidence / rationale:
  - Runtime quote fetching is intended to be simple: send one symbol request, render that symbol when its response arrives, wait one second, then send the next request.
  - If YFinance or the server responds slower than one second, responses may overlap or arrive out of order.
- Notes:
  - Exercise 2s, 5s, 15s, and timeout-level quote latency profiles.
  - Verify no dispatcher batching, scene-wide lock-up, graph-card over-flashing, or stale overwrite.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-071 - Validate per-symbol YFinance failures and invalid/missing symbols across tapes, macros, and world markets

- Priority: 1
- Severity: High
- Area: quote_failures
- Status: open
- Evidence / rationale:
  - Individual symbols can fail due to delisting, bad ticker syntax, exchange suffix changes, no quote, Yahoo empty response, or unsupported market data.
  - One bad symbol must not poison an entire tape, macro set, or world market ribbon.
- Notes:
  - Cover user portfolio symbols, macro symbols, global exchange symbols, and graph-card top movers.
  - Include validation-time and runtime-time failures separately.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-072 - Validate YFinance/Yahoo HTTP failures including 401, 403, 404, 408, 429, 5xx, crumb/cookie failure, and empty JSON

- Priority: 1
- Severity: High
- Area: yfinance_http_degradation
- Status: open
- Evidence / rationale:
  - Yahoo-backed APIs can fail with authentication/crumb issues, throttling, transient server errors, and empty/malformed responses.
  - YFinance.NET owns all Yahoo communication, so these failures must be simulated and proven at that layer.
- Notes:
  - Do not reintroduce direct Yahoo calls outside YFinance.NET while adding tests.
  - Check cache behavior and backoff behavior separately for each class of response.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-073 - Validate malformed numeric market data: null, NaN, Infinity, zero, negative, huge values, missing change percent

- Priority: 1
- Severity: High
- Area: market_data_integrity
- Status: open
- Evidence / rationale:
  - Financial UI rendering depends on prices, changes, percentages, and chart series being sane.
  - Bad numeric data can cause clipped text, incorrect red/green coloring, graph-card flashing, layout overflow, or exceptions.
- Notes:
  - Include raw price unchanged with percent churn because graph-cards should flash only on actual raw price changes.
  - Include chart series with too few points, duplicate timestamps, descending timestamps, and gaps.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-074 - Validate YFinance.NET cache expiry, stale fallback, and no-fresh-data behavior

- Priority: 1
- Severity: High
- Area: cache_staleness
- Status: open
- Evidence / rationale:
  - YFinance.NET has a 10-minute cache ceiling and the app intentionally removed its separate QuoteCacheService.
  - When network is unavailable after cache expiry, the app needs a truthful stale/unavailable policy.
- Notes:
  - Exercise cache hit, cache nearing expiry, cache expired with network down, cache expired with slow network, and recovery to fresh data.
  - Confirm there is still only one market-data cache owner.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-075 - Validate internet loss and recovery during normal runtime after healthy startup

- Priority: 1
- Severity: High
- Area: runtime_network_recovery
- Status: open
- Evidence / rationale:
  - The most common real-world degradation is Wi-Fi/VPN/internet loss after the app is already running.
  - The UI should keep rendering, stop claiming freshness, and recover without restart.
- Notes:
  - Inject loss while updating macro values, while updating world markets, while walking tape symbols, and while graph-card top movers are active.
  - Recovery should not cause a burst/batch redraw.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-076 - Validate Finance News degradation: no DeepSeek key, bad key, endpoint down, rate limit, slow response, malformed AI output, RSS-only fallback

- Priority: 2
- Severity: Medium
- Area: news_degradation
- Status: open
- Evidence / rationale:
  - News scroller uses DeepSeek when configured and RSS-only fallback when unavailable.
  - We previously proved RSS-only fallback once, but not as a repeatable degraded-mode matrix.
- Notes:
  - Cover Adams/Vogon mode and classical Shakespeare mode prompts.
  - Ensure failures do not block UI and do not reset the scroller into strange repeated-line states.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-077 - Validate World Markets ribbon degradation for quote, timing, weather, timezone, and partial exchange failures

- Priority: 2
- Severity: Medium
- Area: world_markets_degradation
- Status: open
- Evidence / rationale:
  - The World Markets ribbon is now independent of the rest of the scene and may have multiple data dependencies.
  - Partial exchange failures must not collapse the whole ribbon or break pinned New York status reuse.
- Notes:
  - Cover unavailable market timing, quote failure, weather failure, timezone conversion failure, and mixed open/closed/unknown states.
  - If market status change time is unavailable, top-left text should remain blank rather than show Timing unavailable.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-078 - Validate background rotation/download degradation: offline warm-up, slow downloads, partial TMP files, corrupt images, decode failures

- Priority: 2
- Severity: Medium
- Area: background_degradation
- Status: open
- Evidence / rationale:
  - Background logic can download images, clean TMP files, rotate local/managed images, and apply transitions/zoom.
  - Blank background periods were observed historically, so degraded asset paths need explicit proof.
- Notes:
  - Cover default three shipped images, downloaded manifest images, custom folder enabled/disabled, missing folder, corrupt file, and GPU-heavy transition fallback.
  - Attribution display must use short attribution strings only.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-079 - Validate Local AppData failures: missing, access denied, corrupt config, corrupt cache, disk full, read-only files

- Priority: 2
- Severity: High
- Area: local_appdata_degradation
- Status: open
- Evidence / rationale:
  - All local application data should live under Local AppData for installer/uninstaller ownership.
  - Config, backgrounds, traces, caches, and runtime state may fail due to permissions, corruption, or disk exhaustion.
- Notes:
  - Include first-run missing directories and upgrade from obsolete PortfolioSaver paths.
  - Do not delete user-selected custom image folders during cleanup tests.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-080 - Validate trace/logging degradation: circular log full, file lock, access denied, corrupt index, excessive anomaly volume

- Priority: 2
- Severity: Medium
- Area: trace_degradation
- Status: open
- Evidence / rationale:
  - End-user support depends on circular trace files, including client/server communication logs.
  - Trace failures must not crash the app or hide critical degradation events.
- Notes:
  - Cover both client and YFinance.NET server traces.
  - Ensure high-volume failures are summarized or throttled without losing first-cause evidence.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-081 - Validate applying configuration changes while runtime workers and YFinance server are active

- Priority: 2
- Severity: Medium
- Area: configuration_change_runtime
- Status: open
- Evidence / rationale:
  - Successful config OK/Apply can regenerate ticker tapes, news scroller, and runtime symbol sets while background workers are active.
  - Configuration changes should not mutate server internals beyond documented control surfaces.
- Notes:
  - Exercise Apply/OK and Cancel after successful validation, plus failed validation with no apply.
  - Cover symbol list changes, background folder changes, news mode changes, DeepSeek settings changes, and timing slider changes.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-082 - Validate shutdown while network/file/background operations are pending

- Priority: 2
- Severity: Medium
- Area: shutdown_degradation
- Status: open
- Evidence / rationale:
  - The app must close cleanly while quote requests, news fetches, background downloads, trace writes, or YFinance server requests are in flight.
  - Owned server lifecycle requires special proof during shutdown.
- Notes:
  - Cover desktop close, config close, screensaver exit, VM harness abort, and Windows session logoff if practical.
  - Shutdown must not leave orphaned YFinance server, VM agent, or downloader processes.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-083 - Validate visual rendering under degraded graphics/performance conditions

- Priority: 3
- Severity: Medium
- Area: display_rendering_stress
- Status: open
- Evidence / rationale:
  - Background transitions, graph cards, news scroller, world markets ribbon, and ticker tapes all animate in one scene.
  - Low GPU capability, high CPU load, RDP/VM rendering, fullscreen toggles, and resolution changes can expose jitter or blank scenes.
- Notes:
  - Include reduced GPU acceleration availability investigation if WPF rendering falls back to software.
  - Use screenshot cadence plus trace frame/update timing to identify batch redraws.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-084 - Validate time, timezone, DST, holiday, and market-calendar anomalies

- Priority: 3
- Severity: Medium
- Area: time_calendar_degradation
- Status: open
- Evidence / rationale:
  - Market status and countdown labels depend on local time, exchange time, holidays, DST transitions, and YFinance timing data.
  - Users have seen timing unavailable states, and the UI now should leave certain unavailable fields blank.
- Notes:
  - Cover weekend, pre-market, regular market, after-hours, holiday closure, early close, DST boundary, local clock skew, and timezone lookup failure.
  - Do not issue separate Yahoo calls outside YFinance.NET for status.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-085 - Create an autonomous degraded-mode soak matrix with CR creation on anomaly detection

- Priority: 3
- Severity: Medium
- Area: long_soak_fault_matrix
- Status: open
- Evidence / rationale:
  - The healthy-path autonomous validation loop now works, but degraded scenarios need a repeatable matrix rather than one-off manual tests.
  - Future long test passes should create CRs automatically when injected degraded-mode expectations are violated.
- Notes:
  - Matrix should sequence startup, config, runtime, recovery, and shutdown injections across multiple 30-minute VM cycles without chat prompting.
  - Analyzer should distinguish expected injected errors from unexpected regressions.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.


## DeepSeek UX Review Additions

DeepSeek reviewed CR-064 through CR-085 on 2026-06-13 with a specific prompt to identify missing user-experience checks for anomalous/degraded situations. The review found six additional UX-focused validation tickets.

### CR-086 - Define user-facing status indicators and error messaging for degraded modes

- Priority: 1
- Severity: High
- Area: ux_degradation_feedback
- UX rationale:
  - DeepSeek UX anomaly review identified that CR-064 through CR-085 cover functional degradation but do not fully define what the human user sees for each degraded state.
  - Users do not have trace logs; they infer system health from visual status indicators, placeholders, color states, and concise messages.
- Acceptance highlights:
  - A reference table documents the expected display for every major visible component in healthy/degraded/offline states.
  - Injected degradation updates the visible state within 2 seconds where practical.
  - Stale data is never styled identically to fresh/live data.

### CR-087 - Validate accessibility of anomaly states for screen readers, high contrast, and keyboard users

- Priority: 2
- Severity: Medium
- Area: ux_accessibility_degradation
- UX rationale:
  - DeepSeek UX anomaly review identified that visual degraded-state indicators must be mirrored through accessibility APIs.
  - Users relying on screen readers, high contrast themes, or keyboard navigation must not receive stale or misleading announcements.
- Acceptance highlights:
  - Expected screen-reader output is documented for key degraded states.
  - High-contrast mode keeps offline/stale/unavailable indicators distinguishable.
  - Keyboard focus order remains stable and Escape/Cancel paths work during degradation.

### CR-088 - Validate clear data freshness indicators and live/stale/recovery transitions

- Priority: 1
- Severity: High
- Area: ux_freshness_communication
- UX rationale:
  - DeepSeek UX anomaly review identified that CR-074 says stale cache must not be labeled fresh, but does not fully define how users learn data is stale.
  - Financial dashboards can mislead users if cached or delayed data looks identical to live data.
- Acceptance highlights:
  - Screenshots/video show live -> stale -> recovery -> fresh transitions.
  - The words stale, cached, delayed, offline, or an equivalent explicit indicator are visible when data is not live.
  - Recovery notification is noticeable but does not last longer than 3 seconds or cause batch redraw.

### CR-089 - Validate degraded symbol placeholder UX and consistency across components

- Priority: 2
- Severity: Medium
- Area: ux_placeholder_consistency
- UX rationale:
  - DeepSeek UX anomaly review identified that CR-071 isolates per-symbol failures but does not fully define the placeholder the user sees.
  - Inconsistent missing-data treatment across tape, graph, macro, and world-market surfaces makes intentional degradation look like broken UI.
- Acceptance highlights:
  - Injected symbol failures show consistent placeholder treatment across all applicable components.
  - Placeholder cards/rows do not resize or shift layout when healthy symbols update.
  - Tooltip or equivalent detail shows last successful fetch time when available.

### CR-090 - Validate user-friendly config validation error messages and progress feedback

- Priority: 2
- Severity: High
- Area: ux_config_error_clarity
- UX rationale:
  - DeepSeek UX anomaly review identified that CR-066 and CR-067 cover config behavior but not enough plain-language user guidance.
  - A user should know whether to retry, check network, fix a symbol, wait out a rate limit, or cancel safely.
- Acceptance highlights:
  - Config dialog screenshots show clear, non-technical messages for each injected failure.
  - Validate/Retry is re-enabled after failure and Cancel closes promptly during slow validation.
  - Validation progress text fits within current layout without clipping.

### CR-091 - Validate interactive element responsiveness during network degradation and latency

- Priority: 2
- Severity: Medium
- Area: ux_interactivity_degradation
- UX rationale:
  - DeepSeek UX anomaly review identified that no-freeze assertions do not prove individual controls acknowledge user input promptly.
  - During high latency, users need immediate click/keyboard feedback even if network work continues asynchronously.
- Acceptance highlights:
  - A high-latency harness profile proves controls provide feedback within 500ms of user input.
  - Cancel/Escape closes dialogs promptly while requests are pending.
  - No user input is lost or replayed unexpectedly after network recovery.
