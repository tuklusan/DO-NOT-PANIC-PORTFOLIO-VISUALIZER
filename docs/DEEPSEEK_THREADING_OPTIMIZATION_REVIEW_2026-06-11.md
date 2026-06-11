# DeepSeek Threading and Optimization Review - 2026-06-11

- Review type: full tracked text/codebase concurrency, parallel processing, async, UI-fluidity, and optimization review
- Reviewer: DeepSeek v4-pro via chunked whole-repository packet process
- Source artifact directory: `build/deepseek-review/threading-optimization-20260611-090438`
- Tracked-file manifest: `build/deepseek-review/threading-optimization-20260611-090438/tracked-text-manifest.json`
- Packet manifest: `build/deepseek-review/threading-optimization-20260611-090438/packet-manifest.json`
- Final synthesis artifact: `build/deepseek-review/threading-optimization-20260611-090438/FINAL_SYNTHESIS.md`

Policy note: this is a versioned project review record. CR candidates from this review are tracked in `docs/BETA6_AUDIT_STATE.json` as CR-015 through CR-060.

# Consolidated CR Candidates (Deduplicated)

### Priority 1 – Critical

**CR‑1: UI thread blocked by synchronous InternetProbeService probe every second**  
- Priority: 1  
- Area: UI fluidity / Async  
- Severity: Critical  
- Evidence: `Settings/MainWindowViewModel.OnStateTimerTickAsync` → `UpdateConnectivityState` → `InternetProbeService.IsInternetAvailable()`; `Shared/Services/InternetProbeService.cs` – `ProbeInternet` uses sync `HttpClient.Send` + `Thread.Sleep`.  
- Notes: Dispatcher timer ticks every 1 s; worst‑case blocks WPF dispatcher up to 6 s, making the config window unresponsive.  
- Acceptance criteria: UI remains responsive during periodic probe ticks; no mouse freeze or input lag.  
- Suggested verification: Mock HTTP handler with 2 s delay; confirm UI buttons are clickable while probe runs. Use `DispatcherTimer` + `async Task` probe.

**CR‑2: Process output stream deadlock in `Invoke-ProcessWithTimeout`**  
- Priority: 1  
- Area: Build infrastructure / Pipe I/O  
- Severity: Critical  
- Evidence: `build/build-safe-temp.ps1`, function `Invoke-ProcessWithTimeout` (reads stdout/stderr *after* `WaitForExit`, causing full‑pipe block).  
- Notes: Classic deadlock; child process hangs, build spuriously fails.  
- Acceptance criteria: Process with >4 KB output completes within timeout.  
- Suggested verification: Use `cmd /c "for /L %i in (1,1,1000) do @echo %i"` through the function; must exit quickly.

**CR‑3: Race condition in quote pipeline dictionary access**  
- Priority: 1  
- Area: Concurrency / Thread safety  
- Severity: Critical  
- Evidence: `Presentation/Services/StartupCoordinator.cs` – `_pendingQuotePipeline` (`Dictionary<string, PendingQuoteRequest>`) modified without locking by `LoadQuotesAsync`, `DrainCompletedQuotePipelineAsync`, `QueueQuotePipelineRequests`.  
- Notes: Overlapping scene builds cause `InvalidOperationException` during enumeration, data loss, or double‑processing.  
- Acceptance criteria: No dictionary exceptions under concurrent `BuildSceneAsync` calls; each entry processed exactly once.  
- Suggested verification: Stress test with rapid concurrent build triggers, assert no `InvalidOperationException`.

**CR‑4: `YFinanceRuntimeClientFactory` reset tears down in‑flight operations**  
- Priority: 1  
- Area: Concurrency / Resource management  
- Severity: Critical  
- Evidence: `Data/Services/YFinanceRuntimeClientFactory.cs` – `ResetConnectionState` called on any exception, disposes shared client while other callers still use it.  
- Notes: Causes `ObjectDisposedException` / `NullReferenceException` for orthogonal requests.  
- Acceptance criteria: Transient failure in one request does not break concurrent requests; factory reconnects cleanly.  
- Suggested verification: Stress test 20 parallel requests, force one to throw; remaining 19 complete successfully.

**CR‑5: Screensaver `App.OnExit` sync‑over‑async deadlock**  
- Priority: 1  
- Area: Async / Shutdown  
- Severity: Critical  
- Evidence: `Screensaver/App.xaml.cs` – `OnExit` calls `StopOwnedServerAsync().GetAwaiter().GetResult()` on UI thread; inner await may not consistently use `ConfigureAwait(false)`.  
- Notes: Deadlock freezes process shutdown if continuation posted to blocked UI dispatcher.  
- Acceptance criteria: Process terminates within 1 s of OnExit, no hang.  
- Suggested verification: Run under debugger, set breakpoint in `StopOwnedServerAsync`; verify `SynchronizationContext.Current` is null or flow is fully `ConfigureAwait(false)`.

### Priority 2 – High

**CR‑6: `TraceLog` static constructor blocks first caller on DNS resolution**  
- Priority: 2  
- Area: Startup / Async  
- Severity: High  
- Evidence: `Shared/Diagnostics/TraceLog.cs` – static fields `HostName`, `LocalIp` invoke `Dns.GetHostName()` / `Dns.GetHostAddresses()` synchronously in static initializer.  
- Notes: First touch (often UI thread at startup) causes synchronous network call; may freeze UI for seconds.  
- Acceptance criteria: UI visible and responsive before DNS resolution completes; placeholder values used until resolved.  
- Suggested verification: Shim `Dns.GetHostName` to delay 5 s; measure UI startup time and responsiveness.

**CR‑7: Incremental graph additions cause repeated full visual‑tree rebuild, stalling UI**  
- Priority: 2  
- Area: UI performance  
- Severity: High  
- Evidence: `ScreensaverSceneControl.ApplyOrUpdateGraph` → `SyncGraphVisuals` clears all children and rebuilds every `FloatingGraphControl` on each graph arrival during warm‑up.  
- Notes: With 16+ graphs, visible flicker and UI‑thread blocking on startup.  
- Acceptance criteria: `SyncGraphVisuals` called once per batch; no visual flicker; incremental updates replace only affected control.  
- Suggested verification: Run 16‑graph warm‑up, verify single `SyncGraphVisuals` call and no frame drops.

**CR‑8: Synchronous file write on UI thread after symbol validation**  
- Priority: 2  
- Area: UI fluidity  
- Severity: High  
- Evidence: `Settings/ViewModels/MainWindowViewModel.SaveTrustedSymbolProfiles` → synchronous `_symbolProfileStore.Save(...)`.  
- Notes: UI thread performs disk I/O right after validation, causing freeze.  
- Acceptance criteria: Save offloaded to background; UI remains responsive.  
- Suggested verification: Profile with slow I/O; confirm UI thread free, progress window updates.

**CR‑9: Synchronous file copy during legacy data migration on startup**  
- Priority: 2  
- Area: Startup / UI fluidity  
- Severity: High  
- Evidence: `Shared/Helpers/AppDataRootResolver.TryCopyLegacyRootOnce` performs a full synchronous directory copy. Called from `ResolveInstalledLocalDataRoot` on first access (often UI thread).  
- Notes: Large legacy folders can block UI for seconds.  
- Acceptance criteria: Window appears immediately; copy proceeds in background.  
- Suggested verification: Create 100 MB legacy directory; app startup under stopwatch shows immediate UI.

### Priority 3 – Medium

**CR‑10: Config/Desktop `App.OnExit` blocks UI thread with synchronous wait on async shutdown**  
- Area: Shutdown / UI fluidity  
- Evidence: `Config/App.xaml.cs` and `Desktop/App.xaml.cs` `OnExit` → `StopOwnedServerAsync().GetAwaiter().GetResult()`.  
- Notes: UI thread blocked for duration of network close; may appear hung (seconds).  
- Acceptance criteria: Window closes within 500 ms regardless of server response.  
- Suggested verification: High‑latency remote server; measure window close time.

**CR‑11: Missing cancellation propagation in `ExchangePhotoCacheService` background download**  
- Area: Cancellation / Background tasks  
- Evidence: `Media/Services/ExchangePhotoCacheService.StartDefaultManifestWarmup` → `Task.Run(…, CancellationToken.None)`.  
- Notes: Process may stay alive until HTTP timeout (up to 45 s); download gate never released if hung.  
- Acceptance criteria: App shuts down within seconds even if download in progress.  
- Suggested verification: Start app, close immediately; verify process exit. Simulate 30 s server delay.

**CR‑12: Missing `CancellationToken` in `SymbolProfileStore.Load`**  
- Area: Cancellation / Async  
- Evidence: `Presentation/Services/StartupCoordinator.cs` line ~295, blocking file read inside async enumerable without cancellation.  
- Notes: Holds resources and delays startup if cancelled late.  
- Acceptance criteria: Operation stops promptly on cancellation; file handles released.  
- Suggested verification: Cancel token before/during profile load; assert no lingering file handles.

**CR‑13: `HistoricalCacheService.PurgeExpiredAsync` runs synchronous I/O on calling thread**  
- Area: Async / UI fluidity  
- Evidence: `Data/Services/HistoricalCacheService.PurgeExpiredAsync` – synchronous `foreach` with `FileInfo` and `TryDelete`. Called on every history fetch.  
- Notes: May cause UI stalls if called from UI‑bound context; especially problematic on slow network drives.  
- Acceptance criteria: Purge offloaded to thread pool; UI time <10 ms.  
- Suggested verification: Create 10k dummy cache files; call from UI thread, ensure no freeze.

**CR‑14: Synchronous SHA‑256 hashing during release‑manifest validation at startup**  
- Area: Startup / Performance  
- Evidence: `Shared/Integrity/ReleaseManifestValidator.ValidateDirectory` called on startup thread.  
- Notes: Adds perceptible pause before app operates.  
- Acceptance criteria: Validation runs on background; UI shell appears before completion.  
- Suggested verification: Profile startup time with/without manifest check; ensure UI appears promptly.

**CR‑15: Synchronous file read in UI recovery path (`ScreensaverSceneControl.TryRecoverActiveBackgroundSource`)**  
- Area: UI fluidity  
- Evidence: `ScreensaverSceneControl.cs` – `TryRecoverActiveBackgroundSource` calls `File.ReadAllBytes(path)` synchronously on UI thread.  
- Notes: Large background images cause UI freezes of several hundred ms.  
- Acceptance criteria: Recovery uses preloaded bytes or async read; UI remains fluid.  
- Suggested verification: Force recovery with large image; measure UI thread responsiveness.

**CR‑16: Expensive JSON serialisation on every editor property change**  
- Area: UI responsiveness / Performance  
- Evidence: `Settings/ViewModels/MainWindowViewModel.OnEditorChanged` → `BuildCandidateSettings` → `JsonSerializer.Serialize(settings)`.  
- Notes: Every keystroke triggers full serialization; causes visible lag with many symbols.  
- Acceptance criteria: Smooth typing; fingerprint computation debounced (e.g., 300 ms).  
- Suggested verification: Max configuration (4 groups × 8 tickers), hold down a key in a symbol textbox; UI must stay smooth.

**CR‑17: Dispatcher timer in `NewsFlasherControl` causing rendering jitter due to heavy text measurement**  
- Area: UI performance  
- Evidence: `Render/Controls/NewsFlasherControl.xaml.cs` – `OnPlaybackTick` → `StepScrolling` → `MeasureHeadlineHeight` (allocates `FormattedText` each tick).  
- Notes: 40 ms timer + allocation jitter worsens tape animation smoothness.  
- Acceptance criteria: Headline heights cached; `FormattedText` not created on each tick; smooth scrolling.  
- Suggested verification: Frame‑time profiler or visual inspection during news updates.

**CR‑18: High‑frequency heap allocations for mini‑graph points**  
- Area: Performance / Memory  
- Evidence: `ScreensaverSceneControl.BuildMiniGraphPoints` creates new `PointCollection` and `Point` instances every market‑data refresh (~every minute).  
- Notes: GC pressure and binding churn cause frame‑time jitter on lower‑end hardware.  
- Acceptance criteria: `ObservableCollection` reused, in‑place updates; allocation count drops significantly.  
- Suggested verification: Memory profiler during refresh cycle; confirm minimal allocations.

**CR‑19: `CompositionTarget.Rendering` handler fires when not needed**  
- Area: Performance  
- Evidence: `Render/Services/TapeAnimationController.cs` – `OnRendering` performs float arithmetic even when tape is invisible or zero‑width.  
- Notes: Wasted CPU cycles on every frame.  
- Acceptance criteria: Early exit when `IsVisible == false` or `ActualWidth <= 0`; CPU near zero when no tape motion.  
- Suggested verification: Scene with all tapes disabled; profile CPU usage of rendering thread.

**CR‑20: `ProviderHealthService` is not thread‑safe**  
- Area: Thread safety  
- Evidence: `Data/Services/ProviderHealthService.cs` – `MarkSuccess`/`MarkFailure` write to shared `_snapshot` fields without synchronization.  
- Notes: Torn reads/writes cause false healthy/failover decisions.  
- Acceptance criteria: Snapshot consistent under concurrent calls; `ConsecutiveFailures` accurate.  
- Suggested verification: 1000 concurrent success/failure calls; assert final snapshot consistency.

**CR‑21: `InternetProbeService` cache expiry race condition**  
- Area: Thread safety / Caching  
- Evidence: `Shared/Services/InternetProbeService.cs` – cache read under lock, but probe runs outside lock; multiple threads can both probe after expiry.  
- Notes: Redundant network calls and resource churn.  
- Acceptance criteria: Only one probe per expiry window under concurrent requests.  
- Suggested verification: Fire multiple simultaneous `IsInternetAvailableAsync` calls; assert a single HTTP request.

**CR‑22: `VmAgent` unbounded log growth**  
- Area: Resource management  
- Evidence: `VmAgent/Program.cs` logging method writes without size limit.  
- Notes: Long soak tests (up to 10k min) fill disk and cause I/O stalls.  
- Acceptance criteria: Log rotation at 10 MB or equivalent; file size never exceeds limit.  
- Suggested verification: Simulate long run; verify file size cap and no I/O errors.

**CR‑23: Missing `CancellationToken` support for long‑running remote/local operations in build scripts**  
- Area: Build / Cancellation  
- Evidence: `build/vm/VmSshCommon.ps1`, `Invoke-VmBuildTest.ps1`, `Guest-UxDeepExercise.ps1` – all synchronous, only timeout.  
- Notes: Ctrl‑C leaves orphaned remote processes; can corrupt result bundles.  
- Acceptance criteria: Clean abort kills remote processes, leaves valid (or marked) bundle.  
- Suggested verification: Start long soak and press Ctrl‑C; verify remote processes terminated, no corruption.

**CR‑24: Unobserved background receive loop exception in `YFinanceServerClient` dispose path**  
- Area: Async cleanup  
- Evidence: `YFinance.NET.Client/YFinanceServerClient.cs` – `DisposeSocket` cancels token, disposes stream without awaiting `_receiveLoopTask`; the background fault is unobserved.  
- Notes: Risk of unhandled exception on older runtimes; non‑deterministic teardown order.  
- Acceptance criteria: Await `_receiveLoopTask` with timeout before stream disposal; no unobserved exception.  
- Suggested verification: Unit test: disconnect while streaming, confirm clean exit, no unhandled exception event.

**CR‑25: Thread‑safe initialization of `YFinanceCircularTraceSink` not verified**  
- Area: Thread safety  
- Evidence: `tests/…/YFinanceCircularTraceSinkTests.cs` relies on singleton, but no concurrency test.  
- Notes: If `Instance` not thread‑safe, concurrent access could create multiple sinks or cause corruption.  
- Acceptance criteria: Singleton uses `Lazy<T>` with `ExecutionAndPublication`; stress test verifies exactly one instance, no garbled output.  
- Suggested verification: 100 parallel tasks writing markers; check circular log for consistency.

**CR‑26: `NewsFlasherControl` may use zero width causing layout errors**  
- Area: UI robustness  
- Evidence: `Render/Controls/NewsFlasherControl.xaml.cs` – `ActualWidth` used before layout; zero leads to `FormattedText` exceptions or infinite height.  
- Notes: Potential for unhandled exceptions that freeze UI thread.  
- Acceptance criteria: Guard with `ActualHeight <= 0` check; recover gracefully when layout completes.  
- Suggested verification: Force rendering before layout; assert no exception, control recovers.

**CR‑27: `GlobalMarketsTapeControl` swallows exceptions for missing flag images**  
- Area: Diagnostics  
- Evidence: `Render/Controls/GlobalMarketsTapeControl.xaml.cs` – `GetFlagImageSource` catches and returns null without logging.  
- Notes: Hides systemic I/O or asset problems, making render jitter debugging difficult.  
- Acceptance criteria: Warning logged with flag code and exception; UI falls back gracefully.  
- Suggested verification: Deliberately cause missing flag; verify log entry and no crash.

### Priority 4 – Low

**CR‑28: `YFinanceRuntimeClientFactory` `AsyncLocal` test suppression may leak across parallel tests**  
- Area: Test isolation  
- Evidence: `Data/Services/YFinanceRuntimeClientFactory.cs` – `SuppressServerStartupForTests` uses `AsyncLocal`; parallel tests in same process may interfere.  
- Notes: Tests expecting specific server startup timing could fail sporadically.  
- Acceptance criteria: Suppression scope isolated per test; run two parallel suppressed tests and observe correct behavior.  
- Suggested verification: Run tests with `-parallel` and assert no leaked suppression.

**CR‑29: Multiple services create raw `HttpClient` inside method bodies**  
- Area: Resource management  
- Evidence: `TreasuryYieldCurveQuoteProvider`, `YahooFinanceQuoteProvider` (legacy), `HttpClientFactory.Create`, `ExchangePhotoCacheService` inner `CreateDefaultHttpClient`.  
- Notes: Socket exhaustion under heavy polling; decreases throughput.  
- Acceptance criteria: `IHttpClientFactory` or static shared instance per endpoint; no socket leaks.  
- Suggested verification: Load test 50 symbols every 10 s, monitor `netstat`; socket count stable.

**CR‑30: `HybridHistoricalDataProvider` fetches symbols one‑by‑one**  
- Area: Performance / Concurrency  
- Evidence: `Data/Providers/HybridHistoricalDataProvider.GetHistoryAsync` – sequential `foreach` over pending symbols.  
- Notes: Total latency equals sum of individual fetches; parallelizing up to 2‑3 cuts time significantly.  
- Acceptance criteria: Parallelism via `SemaphoreSlim`; total time ≤ ~⅓ of sequential.  
- Suggested verification: 8 symbols, measure wall‑clock time; ensure improvement with limited concurrency.

**CR‑31: `TickerInfoService` resolves summaries sequentially**  
- Area: Performance  
- Evidence: `Features/Quotes/TickerInfoService.GetInfosAsync` – `foreach` over unresolved symbols with sequential `GetSummaryAsync`.  
- Notes: Cold cache multiplies load time by symbol count.  
- Acceptance criteria: Limited concurrency (e.g., 4) reduces total latency.  
- Suggested verification: Cold start top‑100 exerciser, measure info resolution time before/after.

**CR‑32: `RateLimitGuard` not thread‑safe when shared**  
- Area: Thread safety  
- Evidence: `Data/Services/RateLimitGuard.cs` – `WaitIfNeededAsync` reads/writes `_lastRunUtc` without synchronization.  
- Notes: Shared instance would violate rate limit; currently used single‑consumer but fragile.  
- Acceptance criteria: Documented as single‑consumer; or add `SemaphoreSlim` for safety.  
- Suggested verification: If shared, concurrent calls must enforce minimum interval.

**CR‑33: `SaveLedger` writes occur inside a lock, potentially blocking other threads**  
- Area: Concurrency / I/O  
- Evidence: `ProviderBudgetLedgerService.SaveLedger` – JSON serialization + disk flush inside `lock(_sync)`.  
- Notes: Hung I/O would hold lock indefinitely; low frequency but correctness risk.  
- Acceptance criteria: I/O offloaded outside the lock; UI thread never blocks on hung lock.  
- Suggested verification: Simulate slow disk; confirm UI responsive and no `TimeoutException`.

**CR‑34: Synchronous `File.Exists` in async path of `YFinanceServerProcessManager.ResolveLaunchCommand`**  
- Area: Async  
- Evidence: `Shared/Services/YFinanceServerProcessManager.cs`.  
- Notes: Adds small blocking window on UI thread during server startup.  
- Acceptance criteria: Cache resolved path after first success; subsequent calls avoid filesystem calls.  
- Suggested verification: Measure `ResolveLaunchCommand` time; under 1 ms and no disk access on repeat.

**CR‑35: `HistoricalGraphBuilder` allocates new `PointCollection` on every build**  
- Area: Performance / Memory  
- Evidence: `Render/Services/HistoricalGraphBuilder.Build`.  
- Notes: Rebuilding graphs frequently (e.g., during refresh) causes allocation churn.  
- Acceptance criteria: Reuse collections where possible; builder invoked only once per lifecycle.  
- Suggested verification: Memory profiler during rapid rebuilds; `PointCollection` allocations minimal.

**CR‑36: Static flag image cache not guarded against concurrent access**  
- Area: Thread safety  
- Evidence: `Render/Controls/GlobalMarketsTapeControl.xaml.cs` – `FlagImageCache` (static) read/written without lock.  
- Notes: Currently UI‑thread only, but future background access could corrupt.  
- Acceptance criteria: Document as UI‑only or use `ImmutableDictionary`/`Lazy` pattern.  
- Suggested verification: Multi‑threaded stress setting `FlagCode` properties; assert no `NullReferenceException`.

**CR‑37: Aggressive connection teardown on a single integrity failure**  
- Area: Robustness  
- Evidence: `YFinance.NET.Client/YFinanceServerClient.ReceiveLoopAsync` – `VerifyEnvelope` throws `IOException`, terminating whole loop.  
- Notes: A corrupt message tears down otherwise healthy connection, cancelling all queued work.  
- Acceptance criteria: Corrupt packet logged and skipped; connection remains alive for subsequent valid messages.  
- Suggested verification: Inject malformed payload; verify client logs error but still processes good responses.

**CR‑38: Server does not await active client handlers before process exit**  
- Area: Shutdown  
- Evidence: `YFinance.NET.Server/Hosting/YFinanceServerProgram.RunAsync` – after `listener.Stop()`, does not await client tasks.  
- Notes: In‑flight connections reset; resources released ungracefully.  
- Acceptance criteria: All client handlers awaited with timeout after cancellation; orderly connection finish.  
- Suggested verification: Connect several clients with long‑running requests, send shutdown; observe graceful disconnects.

**CR‑39: UI Automation Fixed‑Delay Sleeps in `Guest-UxDeepExercise` increase flakiness**  
- Area: Test harness  
- Evidence: `build/vm/Guest-UxDeepExercise.ps1` – numerous fixed `Start-Sleep` calls.  
- Notes: Wastes time on fast machines, too short on slow VMs; serial execution inflates cycle time.  
- Acceptance criteria: Replace with `Wait-UIAutomationCondition` polling loops; duration decreases when system responsive.  
- Suggested verification: Run on fast and slow VM; measure total time and zero “element not found” errors.

**CR‑40: Remote Polling Loop Blocks Host Pipeline**  
- Area: Build  
- Evidence: `build/vm/Invoke-VmBuildTest.ps1` – `do-while` loop that performs full SSH session each 15 s.  
- Notes: Host thread blocked for SSH round‑trip, unresponsive to Ctrl‑C.  
- Acceptance criteria: Polling uses non‑blocking interval with local sleep, or lighter check.  
- Suggested verification: Run long soak; confirm console accepts Ctrl‑C promptly.

**CR‑41: File‑Replacement Atomicity retry loop excessive**  
- Area: Build robustness  
- Evidence: `build/vm/Guest-UxDeepExercise.ps1` – `Write-TextFileWithRetry` retries up to 20 times with 80 ms sleep.  
- Notes: Under heavy I/O contention, blocks script for seconds; transient locks resolve quickly.  
- Acceptance criteria: Retries reduced to 3 with 50 ms pause; fail fast and let caller handle.  
- Suggested verification: Artificially lock file; observe completion within 1 s.

**CR‑42: Redundant `Get-AvailableDisplayModes` enumeration**  
- Area: Test harness / Performance  
- Evidence: `build/vm/Guest-UxDeepExercise.ps1` – multiple calls to `Get-CimSupportedDisplayModes` and `Get-AvailableDisplayModes` on each resolution change attempt.  
- Notes: Slow on VMs with many virtual modes; list used only for error reporting.  
- Acceptance criteria: Cache at startup; time spent in enumeration < 200 ms.  
- Suggested verification: Profile on VM with >100 modes; confirm single call and reduced time.

**CR‑43: `TraceLog.WriteCircular` holds lock during synchronous index‑file I/O**  
- Area: Performance  
- Evidence: `Shared/Diagnostics/TraceLog.cs` – `WriteCircular` inside `lock(FileSync)` performs `File.ReadAllText`/`File.WriteAllText`.  
- Notes: Slightly extends lock duration; marginal contention under extreme load.  
- Acceptance criteria: Index kept in‑memory and flushed asynchronously; or separate lock; no stall >few ms under 10k concurrent writes.  
- Suggested verification: Stress test with 10k concurrent writes, monitor lock wait times.

**CR‑44: Repeated synchronous `File.ReadAllText` in test fixtures slows test suite** (includes review‑09 and review‑10)  
- Area: Test infrastructure  
- Evidence: `tests/…/ScreensaverRenderBehaviorTests.cs`, `VmHarnessScriptTests.cs`, source‑reading tests.  
- Notes: Redundant disk I/O increases CI time and memory churn; blocks test runner threads.  
- Acceptance criteria: Contents cached via `Lazy<string>`; wall‑clock time reduced by ≥20% on HDD.  
- Suggested verification: Run affected tests with stopwatch; confirm reduced disk reads.

**CR‑45: Race condition in concurrent `ProviderBudgetLedger` reservation test may mask thread‑safety issues**  
- Area: Test quality  
- Evidence: `tests/…/Nb040BehaviorTests.cs` – test spawns 8 tasks without a barrier; thread pool may serialise them, hiding races.  
- Notes: Test may pass even if service not thread‑safe.  
- Acceptance criteria: Test uses `Barrier` or `ManualResetEvent` to force true concurrency; still detects correct serialisation.  
- Suggested verification: Run test repeatedly under high load, ensure consistent result.

**CR‑46: Blocking `.GetAwaiter().GetResult()` in test HTTP handler risks deadlock under `SynchronizationContext`**  
- Area: Test infrastructure  
- Evidence: `tests/…/FinanceNewsServiceTests.cs` – `FakeHttpMessageHandler.SendAsync` uses `.GetResult()` on `ReadAsStringAsync`.  
- Notes: Fragile if test runner ever uses a sync context; can cause hard‑to‑debug hangs.  
- Acceptance criteria: Handler made `async` and content read with `await`.  
- Suggested verification: Existing tests pass; test under custom `SynchronizationContext` to ensure no deadlock.