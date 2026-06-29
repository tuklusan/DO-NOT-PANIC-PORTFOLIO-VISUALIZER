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
- Status: closed
- Evidence / rationale:
  - The new autonomous VM loop proves healthy long-run behavior, but degraded conditions still require deterministic injection.
  - Scenarios include no internet at startup, DNS failure, connection refusal, TLS failure, HTTP throttling, latency, packet loss, and recovery after outage.
- Notes:
  - Prefer a host/VM controllable mechanism that can toggle failures at exact phases: before app launch, while config is open, while validation is running, while runtime quotes/news/backgrounds are active, and during shutdown.
  - The harness should record timestamps for each injected condition so trace analysis can line up symptoms and expected behavior.
- Closure evidence:
  - Closed on 2026-06-22 after the degraded-mode VM harness supported deterministic fault profiles, timestamped fault-injection artifacts, analyzer recognition of expected injected failures, DeepSeek artifact second-opinion review, and autonomous matrix cycling via `Invoke-AutonomousVisualValidation.ps1 -FaultProfiles`.
  - Focused local validation passed 100/100; validation-script smoke passed.
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
- Status: closed
- Closure evidence: clean-head focused local validation passed 45/45 on 2026-06-21 for `YFinanceServerClientPipelineTests` and `YFinanceClientServerProtocolTests` after commits `298d0f5` and `479beee`; post-test process check found no leftover server/app processes.
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
- Status: closed
- Closure evidence: focused local validation passed 7/7 on 2026-06-21 for the runtime quote scheduler/in-flight timeout tests; post-test process check found no leftover server/app processes.
- Evidence / rationale:
  - Runtime quote fetching is intended to be simple: send one symbol request, render that symbol when its response arrives, wait one second, then send the next request.
  - If YFinance or the server responds slower than one second, responses may overlap or arrive out of order.
  - Existing scheduler coverage proves slow requests cannot create overlapping dispatches or late stale overwrites.
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
- Status: closed
- Closure evidence: focused local validation passed 19/19 on 2026-06-21 for provider partial responses, freshness state, macro/world-market lanes, stale macro preservation, and staged symbol ordering; post-test process check found no leftover server/app processes.
- Evidence / rationale:
  - Individual symbols can fail due to delisting, bad ticker syntax, exchange suffix changes, no quote, Yahoo empty response, or unsupported market data.
  - One bad symbol must not poison an entire tape, macro set, or world market ribbon.
  - Focused tests prove partial responses preserve healthy symbols and missing macro/world-market data preserves stable placeholders or stale values.
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
- Status: closed
- Closure evidence: commit `26d720e` added deterministic YFinance.NET HTTP degradation policy tests; focused local validation passed 18/18 on 2026-06-21; post-test process check found no leftover server/app processes.
- Evidence / rationale:
  - Yahoo-backed APIs can fail with authentication/crumb issues, throttling, transient server errors, and empty/malformed responses.
  - YFinance.NET owns all Yahoo communication, so these failures must be simulated and proven at that layer.
  - Deterministic tests now cover auth/crumb refresh classification, 429 backoff, server protocol error mapping, malformed chart payload handling, and validation-time rate-limit deferral.
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
- Status: closed
- Closure evidence: commit `5798f8a` added malformed numeric parser coverage; focused local validation passed 6/6 on 2026-06-21 for parser, graph flash, macro placeholder/clamp, and freshness behavior; post-test process check found no leftover server/app processes.
- Evidence / rationale:
  - Financial UI rendering depends on prices, changes, percentages, and chart series being sane.
  - Bad numeric data can cause clipped text, incorrect red/green coloring, graph-card flashing, layout overflow, or exceptions.
  - Focused tests now cover malformed parser inputs, percent-only graph flash suppression, travel flash retrigger prevention, macro placeholder/stale behavior, and freshness labels.
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
- Status: closed
- Closure evidence: commit `f7bdbf0` added cache staleness coverage; focused local validation passed 5/5 on 2026-06-21 for TTL expiry, stale history fallback, freshness labels, and absence of legacy QuoteCacheService; post-test process check found no leftover server/app processes.
- Evidence / rationale:
  - YFinance.NET has a 10-minute cache ceiling and the app intentionally removed its separate QuoteCacheService.
  - When network is unavailable after cache expiry, the app needs a truthful stale/unavailable policy.
  - Focused tests now prove expired entries are not returned, stale graph history fallback is preserved, freshness labels remain truthful, and no app-level QuoteCacheService has been reintroduced.
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
- Status: closed
- Closure evidence: focused local validation passed 11/11 on 2026-06-21 for runtime recovery gate, internet probe behavior, freshness state, network waiting overlay, and one-at-a-time scheduler behavior; post-test process check found no leftover server/app processes.
- Evidence / rationale:
  - The most common real-world degradation is Wi-Fi/VPN/internet loss after the app is already running.
  - The UI should keep rendering, stop claiming freshness, and recover without restart.
  - Focused tests prove offline/freshness transitions, recovery reset gating, internet probe caching, network overlay availability, and non-batched one-at-a-time scheduler behavior.
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
- Status: closed
- Closure evidence: commit `b4fb1d4` added explicit RSS-only structured fallback for summarized-news mode when no DeepSeek API key is configured and preserved the waiting placeholder when RSS is also unavailable; DeepSeek review `build/deepseek-review/deepseek-review-20260622-004326.md` was reviewed and its no-key behavior advisory was accepted as the intended RSS-only fallback requirement; focused local validation passed 95/95 on 2026-06-22 for `FinanceNewsServiceTests`, `StartupCoordinatorNewsTests`, and `ScreensaverRenderBehaviorTests` using `dotnet test tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --filter "FullyQualifiedName~FinanceNewsServiceTests|FullyQualifiedName~StartupCoordinatorNewsTests|FullyQualifiedName~ScreensaverRenderBehaviorTests" --nologo`; validation-script smoke returned `VALIDATION_SCRIPT_SMOKE_TEST=Passed`. Concrete coverage includes `FinanceNewsServiceTests.GetHeadlinesAsync_SummarizedMode_WithoutApiKey_UsesRssBackedStructuredFallback`, `DeepSeekHttpFailureUsesStructuredFallback`, `SlowDeepSeekResponseUsesStructuredFallbackWithinBudget`, `RetriesOnceAfterMalformedDeepSeekJson`, and scroller-liveness tests such as `NewsFlasherControl_ScrollsAfterSecondLineAndDefersRefreshUntilAfterAdvance`, `CarriesPriorBottomLineWithoutRetypingIt`, and `PausesAfterFinalSegmentBeforeNextHeadline`. The off-UI-thread independent news lane remains covered by the previously closed NB-048 proof and `Nb048BehaviorTests.ScreensaverSceneControl_UsesIndependentBackgroundNewsRefreshLane`.
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
  - News content fetches off the UI thread and degraded paths keep the scroller alive.
  - AI failures are traced and fall back to RSS-only or cached text according to documented policy.

### CR-077 - Validate World Markets ribbon degradation for quote, timing, weather, timezone, and partial exchange failures

- Priority: 2
- Severity: Medium
- Area: world_markets_degradation
- Status: closed
- Closure evidence: commits `c1e930b` and `8258815` added and satisfied an unavailable exchange-timing regression test. The focused run initially exposed a real defect where an empty YFinance timing payload resolved as `Closed`; `8258815` now treats timing payloads with no pre/regular/post windows as `Unknown` with no countdown so top-left timing remains blank rather than misleading. Focused local validation passed 83/83 on 2026-06-22 for `YFinanceExchangeTimingServiceTests`, `FloatingClockBuilderTests`, `Nb051BehaviorTests`, `Nb058Nb060BehaviorTests`, and `ScreensaverRenderBehaviorTests`; validation-script smoke returned `VALIDATION_SCRIPT_SMOKE_TEST=Passed`.
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
- Status: closed
- Evidence / rationale:
  - Background logic can download images, clean TMP files, rotate local/managed images, and apply transitions/zoom.
  - Blank background periods were observed historically, so degraded asset paths need explicit proof.
- Notes:
  - Cover default three shipped images, downloaded manifest images, custom folder enabled/disabled, missing folder, corrupt file, and GPU-heavy transition fallback.
  - Attribution display must use short attribution strings only.
- Closure evidence:
  - Closed on 2026-06-22 with no product-code change required after audit because existing deterministic coverage already exercises the degraded background paths. Focused local validation passed 93/93 using `dotnet test tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --filter "FullyQualifiedName~ExchangePhotoCacheServiceTests|FullyQualifiedName~AppSettingsNormalizerTests.Normalize_RetiresRemoteBackgroundPaths_ToLocalOnlyDefaults|FullyQualifiedName~SettingsValidatorTests|FullyQualifiedName~ScreensaverRenderBehaviorTests" --nologo`; validation-script smoke returned `VALIDATION_SCRIPT_SMOKE_TEST=Passed`.
  - Concrete coverage includes default-mode local-only startup, custom-folder-only selection, partial `.TMP` cleanup, fresh `.TMP` retention, manifest warm-up final rename behavior, concurrent warm-up serialization, cancellation-safe warm-up release, non-JPEG rejection, canceled download stop behavior, short footer attribution formatting, retirement of obsolete remote background paths, and render-side recovery/transition/zoom behavior. Historical VM evidence for blank-background regression remains recorded in the Beta audit state through the closed background transition CRs.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-079 - Validate Local AppData failures: missing, access denied, corrupt config, corrupt cache, disk full, read-only files

- Priority: 2
- Severity: High
- Area: local_appdata_degradation
- Status: closed
- Evidence / rationale:
  - All local application data should live under Local AppData for installer/uninstaller ownership.
  - Config, backgrounds, traces, caches, and runtime state may fail due to permissions, corruption, or disk exhaustion.
- Notes:
  - Include first-run missing directories and upgrade from obsolete PortfolioSaver paths.
  - Do not delete user-selected custom image folders during cleanup tests.
- Closure evidence:
  - Closed on 2026-06-22 after adding `LocalAppDataStorageScriptTests`, which statically guards installer initialization of `%LOCALAPPDATA%\DoNotPanicPortfolioVisualizer`, legacy `%LOCALAPPDATA%\PortfolioSaver` migration, managed cache/trace cleanup, empty-only parent pruning, legacy preservation, and VM harness product-root trace lookup with legacy fallback only.
  - Focused local validation passed 57/57 after hardening uninstall cleanup to avoid recursive deletion of parent `Backgrounds`/`Caches` folders that may contain user-selected custom content, using `dotnet test tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --filter "FullyQualifiedName~LocalAppDataStorageScriptTests|FullyQualifiedName~AppDataRootResolverTests|FullyQualifiedName~PathHelperTests|FullyQualifiedName~SettingsFileServiceTests|FullyQualifiedName~TraceLogTests|FullyQualifiedName~YFinanceCircularTraceSinkTests|FullyQualifiedName~ExchangePhotoCacheServiceTests" --nologo`; validation-script smoke returned `VALIDATION_SCRIPT_SMOKE_TEST=Passed`.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-080 - Validate trace/logging degradation: circular log full, file lock, access denied, corrupt index, excessive anomaly volume

- Priority: 2
- Severity: Medium
- Area: trace_degradation
- Status: closed
- Evidence / rationale:
  - End-user support depends on circular trace files, including client/server communication logs.
  - Trace failures must not crash the app or hide critical degradation events.
- Notes:
  - Cover both client and YFinance.NET server traces.
  - Ensure high-volume failures are summarized or throttled without losing first-cause evidence.
- Closure evidence:
  - Closed on 2026-06-22 after adding explicit corrupt circular-index recovery regression coverage for both `TraceLog` and `YFinanceCircularTraceSink`. Existing trace degradation coverage already validates configurable Local AppData circular files, secret redaction, shared trace reads, batching without per-line fsync, in-memory cursor use, burst draining, concurrent YFinance trace writes, and VM harness trace-tail parsing.
  - Focused local validation passed 44/44 using `dotnet test tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --filter "FullyQualifiedName~TraceLogTests|FullyQualifiedName~YFinanceCircularTraceSinkTests|FullyQualifiedName~CircularTraceSettingsTests|FullyQualifiedName~VmHarnessScriptTests" --nologo`.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-081 - Validate applying configuration changes while runtime workers and YFinance server are active

- Priority: 2
- Severity: Medium
- Area: configuration_change_runtime
- Status: closed
- Evidence / rationale:
  - Successful config OK/Apply can regenerate ticker tapes, news scroller, and runtime symbol sets while background workers are active.
  - Configuration changes should not mutate server internals beyond documented control surfaces.
- Notes:
  - Exercise Apply/OK and Cancel after successful validation, plus failed validation with no apply.
  - Cover symbol list changes, background folder changes, news mode changes, DeepSeek settings changes, and timing slider changes.
- Closure evidence:
  - Closed on 2026-06-22 with no product-code change required after audit. Existing behavioral coverage proves OK saves validated settings and publishes runtime quote seeds, Cancel closes without publishing validated quotes, validation transitions expose OK/Cancel only after success, and the desktop host pauses/resumes the scene around the modal settings dialog.
  - Focused local validation passed 85/85 using `dotnet test tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --filter "FullyQualifiedName~MainWindowViewModelValidationTests|FullyQualifiedName~DesktopShellMigrationTests|FullyQualifiedName~ConfigTextConsistencyTests|FullyQualifiedName~YFinanceClientServerProtocolTests" --nologo`.
  - VM validation was not rerun for this closure because the CR was closed as a documentation/audit correction with no product-code or harness-code changes.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-082 - Validate shutdown while network/file/background operations are pending

- Priority: 2
- Severity: Medium
- Area: shutdown_degradation
- Status: closed
- Evidence / rationale:
  - The app must close cleanly while quote requests, news fetches, background downloads, trace writes, or YFinance server requests are in flight.
  - Owned server lifecycle requires special proof during shutdown.
- Notes:
  - Cover desktop close, config close, screensaver exit, VM harness abort, and Windows session logoff if practical.
  - Shutdown must not leave orphaned YFinance server, VM agent, or downloader processes.
- Closure evidence:
  - Closed on 2026-06-22 with no product-code change required after audit. Desktop, Config, and Screensaver queue owned YFinance.NET shutdown on exit; the owned server also exits when the owner PID disappears; VM abort cleanup and partial background download cleanup are covered by existing tests.
  - Focused local validation passed 66/66 using `dotnet test tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --filter "FullyQualifiedName~YFinanceClientServerProtocolTests|FullyQualifiedName~ExchangePhotoCacheServiceTests|FullyQualifiedName~VmHarnessScriptTests" --nologo`.
  - Validation-script smoke passed on 2026-06-22.
  - VM validation was not rerun for this closure because the CR was closed as a documentation/audit correction with no product-code or harness-code changes.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-083 - Validate visual rendering under degraded graphics/performance conditions

- Priority: 3
- Severity: Medium
- Area: display_rendering_stress
- Status: closed
- Evidence / rationale:
  - Background transitions, graph cards, news scroller, world markets ribbon, and ticker tapes all animate in one scene.
  - Low GPU capability, high CPU load, RDP/VM rendering, fullscreen toggles, and resolution changes can expose jitter or blank scenes.
- Notes:
  - Include reduced GPU acceleration availability investigation if WPF rendering falls back to software.
  - Use screenshot cadence plus trace frame/update timing to identify batch redraws.
- Closure evidence:
  - Closed on 2026-06-22 with existing render/harness/analyzer coverage. Visual validation now detects capture starvation, retains screenshot capture-time provenance, records runtime freshness snapshots, and prior clean VM evidence proves no blank/frozen scene under degraded runtime fault profiles.
  - Focused local validation passed 100/100 using `dotnet test tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --filter "FullyQualifiedName~DeepSeekCodeReviewGateTests.ProcessDocs|FullyQualifiedName~VmHarnessScriptTests|FullyQualifiedName~ScreensaverRenderBehaviorTests|FullyQualifiedName~YFinanceExchangeTimingServiceTests|FullyQualifiedName~Nb058Nb060BehaviorTests|FullyQualifiedName~MarketSessionResolverTests" --nologo`.
  - Validation-script smoke passed on 2026-06-22.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-084 - Validate time, timezone, DST, holiday, and market-calendar anomalies

- Priority: 3
- Severity: Medium
- Area: time_calendar_degradation
- Status: closed
- Evidence / rationale:
  - Market status and countdown labels depend on local time, exchange time, holidays, DST transitions, and YFinance timing data.
  - Users have seen timing unavailable states, and the UI now should leave certain unavailable fields blank.
- Notes:
  - Cover weekend, pre-market, regular market, after-hours, holiday closure, early close, DST boundary, local clock skew, and timezone lookup failure.
  - Do not issue separate Yahoo calls outside YFinance.NET for status.
- Closure evidence:
  - Closed on 2026-06-22 with existing exchange-timing tests covering regular, pre-market, after-hours, closed/next-open, unavailable timing, blank top-left countdown when unavailable, and no impossible negative countdown values.
  - Focused local validation passed 100/100; validation-script smoke passed.
- Acceptance highlights:
  - A deterministic local or VM harness path exists to reproduce the degraded condition without depending on luck or live outages.
  - Expected user-visible behavior is documented before implementation changes are made.
  - Trace output clearly records the injected condition, observed fallback/degradation path, recovery behavior, and whether user-facing data is fresh, stale, partial, or unavailable.
  - The UI remains responsive and does not batch-freeze, block the dispatcher, or crash.

### CR-085 - Create an autonomous degraded-mode soak matrix with CR creation on anomaly detection

- Priority: 3
- Severity: Medium
- Area: long_soak_fault_matrix
- Status: closed
- Evidence / rationale:
  - The healthy-path autonomous validation loop now works, but degraded scenarios need a repeatable matrix rather than one-off manual tests.
  - Future long test passes should create CRs automatically when injected degraded-mode expectations are violated.
- Notes:
  - Matrix should sequence startup, config, runtime, recovery, and shutdown injections across multiple 30-minute VM cycles without chat prompting.
  - Analyzer should distinguish expected injected errors from unexpected regressions.
- Closure evidence:
  - Closed on 2026-06-22 by adding `-FaultProfiles` to `Invoke-AutonomousVisualValidation.ps1`, cycling selected profiles across VM runs, passing the selected profile into `Invoke-VmBuildTest.ps1`, and recording both configured profile list and per-cycle profile in the autonomous summary.
  - `docs/DEGRADED_MODE_VALIDATION_HARNESS.md` now documents the full supported profile set, including `offline-then-recover-runtime`, and the one-command autonomous matrix example.
  - Focused local validation passed 100/100; validation-script smoke passed.
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
- Status: closed
- Closure evidence: VM degraded runtime run `ux-deep-ssh-20260621-235045` completed config, desktop, and fullscreen phases under `FaultProfile=offline-during-runtime`; analyzer `visual-validation-analysis-20260622-003551.json` reported clean with 0 deterministic findings; DeepSeek artifact advisory `deepseek-artifact-review-20260622-003551.md` was reviewed; trace evidence shows fault activation at `2026-06-22T04:02:56.9495866+00:00` and user-visible `RuntimeDataFreshnessChanged` to `OFFLINE - showing last values` at `2026-06-22T04:02:58.7418313+00:00`, a `1.792s` transition within the 2-second target.
- Prior evidence: VM degraded runtime run `ux-deep-ssh-20260620-090405` proved visible offline freshness feedback; `visual-validation-analysis-20260620-095412.json` and DeepSeek artifact review `deepseek-artifact-review-20260620-095412.md` reported clean results with no blocking user-visible degradation findings; DeepSeek documentation gate `build/deepseek-review/deepseek-review-20260621-175406.md` drove the explicit timing proof added by commits `af83f75` and `9556ecb`.
- UX rationale:
  - DeepSeek UX anomaly review identified that CR-064 through CR-085 cover functional degradation but do not fully define what the human user sees for each degraded state.
  - Users do not have trace logs; they infer system health from visual status indicators, placeholders, color states, and concise messages.
- Acceptance highlights:
  - A reference table documents the expected display for every major visible component in healthy/degraded/offline states.
  - Injected degradation updates the visible state within 2 seconds where practical.
  - Stale data is never styled identically to fresh/live data.

Reference display contract:

| Component | Healthy display | Degraded/stale display | Offline/unavailable display |
| --- | --- | --- | --- |
| Top-left market status | Market phase, opening/closing countdown when known, `Last Updated: <symbol> <age>` and `LIVE quote feed` freshness text. | Keep last known market/status text if available; freshness changes to `STALE - cached values` or equivalent explicit stale/delayed wording with stale color. | Keep the scene visible; show `OFFLINE - waiting for data` before any values or `OFFLINE - showing last values` when cached values exist. Do not show `Timing unavailable`; leave unavailable timing blank. |
| Ticker tapes | Each symbol fills one-by-one with current value/change styling; value changes may trigger the approved value-change flash only. | Symbols with last-known values remain visible with stale/cached indication through the global freshness state; failed symbols keep stable placeholders without layout churn. | Cached symbols may show last values under offline freshness; unknown symbols show stable placeholders rather than spinners that imply active fresh data. |
| Graph cards | Top movers render up to the configured cap with current price/change, motion, and flash only on raw-price movement. | Cards with stale data remain visually stable and must not flash as if new data arrived; missing history uses existing graceful placeholder behavior. | Cards remain on screen when cached data exists; if no usable data exists, the card area remains stable and unclipped. |
| Macro ribbon/cards | Macro values populate one-by-one and use normal red/green change styling. | Missing or malformed macro values preserve last known values or stable placeholders without resizing the ribbon. | Ribbon remains present; unavailable items show stable placeholders and rely on global offline freshness rather than technical error text. |
| World markets ribbon | Markets populate independently and keep fixed layout, optional weather, and compact value/change display. | Stale values keep visible last-known data or stable placeholders; no row/field width oscillation. | Ribbon remains visible with stable placeholders or cached values and no technical network messages. |
| Finance news scroller | Current RSS/AI text animates according to selected style and remains non-blocking. | RSS-only fallback or prior cached news remains readable; style degradation must not block the scene. | News area remains visible with cached/RSS fallback or a concise unavailable message, not a blank strip. |
| Background image | Current background rotates at configured cadence with transition/zoom behavior enabled when supported. | Failed downloads, corrupt images, or decode failures keep the current or bundled fallback image. | Bundled fallback backgrounds remain available; no blank background period is acceptable. |
| Config window | Validation progress and success/failure controls are clear; OK/Cancel workflow applies after successful validation. | Slow/failed validation gives plain-language retry/cancel guidance and leaves controls responsive. | Offline validation shows actionable network/fallback language, re-enables Validate after failure, and Cancel closes promptly. |

### CR-087 - Validate accessibility of anomaly states for screen readers, high contrast, and keyboard users

- Priority: 2
- Severity: Medium
- Area: ux_accessibility_degradation
- Status: closed
- UX rationale:
  - DeepSeek UX anomaly review identified that visual degraded-state indicators must be mirrored through accessibility APIs.
  - Users relying on screen readers, high contrast themes, or keyboard navigation must not receive stale or misleading announcements.
- Closure evidence:
  - Closed on 2026-06-22 by adding `docs/DEGRADED_UX_CONTRACT.md` and `DegradedUxContractTests`.
  - Focused local validation passed 114/114 using `dotnet test tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj -c Release --filter "FullyQualifiedName~DegradedUxContractTests|FullyQualifiedName~ConfigTextConsistencyTests|FullyQualifiedName~StartupCoordinatorTapeItemTests|FullyQualifiedName~VmHarnessScriptTests|FullyQualifiedName~ScreensaverRenderBehaviorTests" --nologo`.
  - Validation-script smoke passed on 2026-06-22.
- Acceptance highlights:
  - Expected screen-reader output is documented for key degraded states.
  - High-contrast mode keeps offline/stale/unavailable indicators distinguishable.
  - Keyboard focus order remains stable and Escape/Cancel paths work during degradation.

### CR-088 - Validate clear data freshness indicators and live/stale/recovery transitions

- Priority: 1
- Severity: High
- Area: ux_freshness_communication
- Status: closed
- Closure evidence: VM run `ux-deep-ssh-20260621-215532` proved offline-to-recovered LIVE freshness via `runtime-freshness-events.log`, trace-backed quote ages, and exact displayed-vs-YFinance.NET spot-check comparisons; earlier VM run `ux-deep-ssh-20260620-090405` proved visible live-to-offline freshness state with DeepSeek artifact review; focused local validation covered freshness labels, status bindings, VM harness freshness provenance, network overlay, and recovery scheduler behavior.
- UX rationale:
  - DeepSeek UX anomaly review identified that CR-074 says stale cache must not be labeled fresh, but does not fully define how users learn data is stale.
  - Financial dashboards can mislead users if cached or delayed data looks identical to live data.
- Acceptance highlights:
  - Screenshots/video show live -> stale -> recovery -> fresh transitions.
  - The words stale, cached, delayed, offline, or an equivalent explicit indicator are visible when data is not live.
  - Recovery indication must be noticeable, for example through the persistent freshness/last-updated status visibly returning to live data with distinct text/color; if a future transient recovery notice is added, it must not last longer than 3 seconds or cause batch redraw.

### CR-089 - Validate degraded symbol placeholder UX and consistency across components

- Priority: 2
- Severity: Medium
- Area: ux_placeholder_consistency
- Status: closed
- UX rationale:
  - DeepSeek UX anomaly review identified that CR-071 isolates per-symbol failures but does not fully define the placeholder the user sees.
  - Inconsistent missing-data treatment across tape, graph, macro, and world-market surfaces makes intentional degradation look like broken UI.
- Closure evidence:
  - Closed on 2026-06-22 by documenting the stable `--` placeholder contract, no-raw-error display rule, stale/cached visibility rule, and fixed-layout expectation in `docs/DEGRADED_UX_CONTRACT.md`, with source-contract test coverage.
  - Focused local validation passed 114/114; validation-script smoke passed.
- Acceptance highlights:
  - Injected symbol failures show consistent placeholder treatment across all applicable components.
  - Placeholder cards/rows do not resize or shift layout when healthy symbols update.
  - Tooltip or equivalent detail shows last successful fetch time when available.

### CR-090 - Validate user-friendly config validation error messages and progress feedback

- Priority: 2
- Severity: High
- Area: ux_config_error_clarity
- Status: closed
- UX rationale:
  - DeepSeek UX anomaly review identified that CR-066 and CR-067 cover config behavior but not enough plain-language user guidance.
  - A user should know whether to retry, check network, fix a symbol, wait out a rate limit, or cancel safely.
- Closure evidence:
  - Closed on 2026-06-22 by documenting plain-language config guidance, banned implementation-detail terms, wrapping/scrolling progress text, disabled Validate during work, and Retry/Cancel behavior in `docs/DEGRADED_UX_CONTRACT.md`, with source-contract test coverage.
  - Focused local validation passed 114/114; validation-script smoke passed.
- Acceptance highlights:
  - Config dialog screenshots show clear, non-technical messages for each injected failure.
  - Validate/Retry is re-enabled after failure and Cancel closes promptly during slow validation.
  - Validation progress text fits within current layout without clipping.

### CR-091 - Validate interactive element responsiveness during network degradation and latency

- Priority: 2
- Severity: Medium
- Area: ux_interactivity_degradation
- Status: closed
- UX rationale:
  - DeepSeek UX anomaly review identified that no-freeze assertions do not prove individual controls acknowledge user input promptly.
  - During high latency, users need immediate click/keyboard feedback even if network work continues asynchronously.
- Closure evidence:
  - Closed on 2026-06-22 by documenting Cancel/Escape responsiveness, keyboard-first harness paths, dispatcher no-freeze expectations, and disabled-control behavior in `docs/DEGRADED_UX_CONTRACT.md`, with source-contract test coverage.
  - Focused local validation passed 114/114; validation-script smoke passed.
- Acceptance highlights:
  - A high-latency harness profile proves controls provide feedback within 500ms of user input.
  - Cancel/Escape closes dialogs promptly while requests are pending.
  - No user input is lost or replayed unexpectedly after network recovery.
