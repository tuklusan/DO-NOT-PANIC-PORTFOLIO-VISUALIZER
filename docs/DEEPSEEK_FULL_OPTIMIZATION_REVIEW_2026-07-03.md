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

# DeepSeek Full Optimization Review - 2026-07-03

- Review type: full tracked code/documentation optimization, concurrency, parallel processing, UI-fluidity, and QA-process review
- Developer of record: Codex
- Reviewer: DeepSeek v4-flash via chunked whole-repository packet process
- Source artifact directory: uild/deepseek-review/full-optimization-20260703-085440
- Tracked-file manifest: uild/deepseek-review/full-optimization-20260703-085440/tracked-text-manifest.json
- Packet manifest: uild/deepseek-review/full-optimization-20260703-085440/packet-manifest.json
- Final synthesis artifact: uild/deepseek-review/full-optimization-20260703-085440/FINAL_SYNTHESIS.md
- Scope: 385 tracked text/code/document files in 22 packets. Historical DeepSeek review documents and docs/AUDIT_STATE.json were intentionally excluded from the clean packet set to avoid recycling stale CR candidates.

## Process Notes

A first pass was intentionally discarded for CR creation because it included an older June DeepSeek optimization-review document. That polluted synthesis mostly repeated old findings. The clean pass excluded historical DeepSeek review records and produced the current CR set below.

Codex accepted DeepSeek's clean synthesis as actionable project backlog material and created CR-180 through CR-219. These CRs are optimization and reliability work items; they are queued by priority and have not been implemented as part of this review pass.

## Created CRs

| CR | Priority | Severity | Area | Title | Status |
| --- | --- | --- | --- | --- | --- |
| CR-180 | P1 | Critical | Multithreading / data corruption | Fix shared-client concurrency hazard in YFinanceRuntimeClientFactory | open |
| CR-181 | P1 | High | Resource leak / memory | Eliminate shared-client reference leak and retirement race | open |
| CR-182 | P1 | High | Server concurrency / thread-pool starvation | Add per-client request concurrency throttling in YFinanceServer | open |
| CR-183 | P1 | High | Concurrency / cookie corruption | Fix concurrent consent handling without synchronization in YahooSessionManager | open |
| CR-184 | P1 | High | Test reliability / CI false positives | Replace source-file string-matching tests with behavioral tests | open |
| CR-185 | P1 | High | Network / UI freeze | Set explicit HttpClient timeout in YahooSessionManager to prevent indefinite hang | open |
| CR-186 | P1 | High | Thread-pool / scalability | Convert PersistentTtlCache file I/O to async to avoid thread-pool starvation | open |
| CR-187 | P2 | Medium | Build pipeline throughput | Parallelize restore and publish in build/publish-safe-temp.ps1 | open |
| CR-188 | P2 | High | Build reliability | Remove stale restore-asset copying in build-safe-temp.ps1 and enable full restore | open |
| CR-189 | P2 | High | Build/release reliability | Make package install scripts idempotent and add retry error handling | open |
| CR-190 | P2 | Medium | Pipeline throughput | Parallelize VM package installation with throttled concurrency | open |
| CR-191 | P2 | Medium | I/O / pipeline variability | Replace recursive file system scans with targeted `Test-Path` calls in VM inventory scripts | open |
| CR-192 | P2 | Medium | Process management | Add structured cancellation and cleanup hooks to host scripts (Invoke-VmBuildTest.ps1 etc.) | open |
| CR-193 | P2 | High | Test throughput | Enable test parallelism by removing global `DisableTestParallelization = true` and fixing environmental dependencies | open |
| CR-194 | P2 | Medium | Test maintainability | Reduce reflection-based private method testing using `InternalsVisibleTo` or extracted interfaces | open |
| CR-195 | P2 | Medium | Test speed/flakiness | Replace real file I/O in unit tests with an `IFileSystem` abstraction | open |
| CR-196 | P2 | Medium | CPU efficiency | Replace repeated `Task.Delay` polling in `TraceLog.ProcessQueueAsync` with a signaling mechanism | open |
| CR-197 | P2 | Medium | Memory/socket efficiency | Reuse a single `HttpClient` instance in `InternetProbeService` instead of creating a new one per probe | open |
| CR-198 | P2 | Medium | CPU/GC overhead | Replace SHA256 with a fast non-cryptographic hash for protocol payload checksum | open |
| CR-199 | P2 | Medium | Memory/GC | Use `ArrayPool` in `LengthPrefixedProtocolStream` to reduce allocation rate | open |
| CR-200 | P2 | Medium | Resource leak | Add idle timeout for server client connections | open |
| CR-201 | P2 | Medium | Memory leak | Add capacity limit to `MemoryTtlCache` to prevent unbounded growth | open |
| CR-202 | P2 | Medium | Network resilience | Add retry with exponential backoff and jitter to `RetryPolicyService` | open |
| CR-203 | P2 | Medium | UI fluidity | Consolidate multiple `DispatcherTimer` instances in `ScreensaverSceneControl` into one | open |
| CR-204 | P2 | High | UI jitter | Batch `UpdateLayout()` calls in graph warmup to prevent per-graph spikes | open |
| CR-205 | P2 | Medium | Network latency | Fetch RSS feeds in parallel in `FinanceNewsService` | open |
| CR-206 | P2 | Medium | Multithreading | Simplify `ProviderBudgetLedgerService` locking to a single lock to avoid deadlock | open |
| CR-207 | P2 | Medium | UI dispatcher congestion | Add `ConfigureAwait(false)` to async calls in `StartupCoordinator` that do not need UI continuation | open |
| CR-208 | P2 | Medium | UI startup responsiveness | Convert `ScreensaverSettingsService.Load` to async to avoid UI thread blocking | open |
| CR-209 | P2 | Medium | Network latency | Parallelize weather city fetch in `WorldWeatherService` | open |
| CR-210 | P2 | High | UI jitter | Offload `NewsFlasherControl` headline preparation from UI tick to background thread | open |
| CR-211 | P2 | Medium | Performance / IO | Gate `TraceLog.InfoState` calls in hot ticker path behind debug condition | open |
| CR-212 | P2 | Medium | UI fluidity | Use `VirtualizingStackPanel` and cached measurements in `TickerTapeControl` to avoid full rebuild | open |
| CR-213 | P2 | Medium | UI responsiveness under burst | Add cancellation and debouncing to `NewsFlasherControl` headline changes | open |
| CR-214 | P2 | High | UI / network | Replace 1-s connectivity polling timer with `NetworkChange.NetworkAvailabilityChanged` event | open |
| CR-215 | P2 | Medium | UI fluidity | Aggregate per-symbol progress reports in symbol validation to reduce dispatcher load | open |
| CR-216 | P3 | Medium | Allocation/performance | Use `ArrayPool` or slice instead of LINQ `Skip/Take` for symbol batching | open |
| CR-217 | P2 | Medium | Memory/performance | Replace `MemoryTtlCache<JsonDocument>` with raw string storage to avoid cloning and disposal risk | open |
| CR-218 | P3 | Low | Uninstall reliability | Fix process leak in delayed cleanup spawned via `Start-DelayedInstallRootCleanup` | open |
| CR-219 | P3 | Low | Test throughput | Run smoke test scenarios in parallel using `ForEach-Object -Parallel` | open |

## DeepSeek Clean Synthesis

## ACCEPTED_CANDIDATES

### Priority 1 (Critical/High, immediate correctness or resource leak)

1. **Title:** Fix shared-client concurrency hazard in YFinanceRuntimeClientFactory  
   **Priority:** 1 | **Severity:** Critical  
   **Area:** Multithreading / data corruption  
   **Evidence:** `RentSharedClient()` returns the same singleton without exclusive lock; multiple concurrent callers can corrupt protocol connection state.  
   **Recommendation:** Introduce a `SemaphoreSlim` to serialize all operations through the shared client or use a dedicated `Channel`.  
   **Acceptance criteria:** All concurrent calls to `RunAsync`/`RunSerializedAsync` are serialized; no data races in protocol state; existing integration tests pass.

2. **Title:** Eliminate shared-client reference leak and retirement race  
   **Priority:** 1 | **Severity:** High  
   **Area:** Resource leak / memory  
   **Evidence:** `RetireConnectionState()` sets `_sharedClient = null` without tracking outstanding operations; old clients are never disposed and can be leaked.  
   **Recommendation:** Replace hand‑rolled reference counting with a `ConcurrentDictionary<YFinanceServerClient, int>` or create a new client per operation (pooled).  
   **Acceptance criteria:** After a fault, all references to the old client are released and it is disposed exactly once; no client resource leak after 1000 simulated faults.

3. **Title:** Add per‑client request concurrency throttling in YFinanceServer  
   **Priority:** 1 | **Severity:** High  
   **Area:** Server concurrency / thread‑pool starvation  
   **Evidence:** `HandleClientAsync` launches requests with `Task.Run` without any limit; a single client can exhaust thread pool.  
   **Recommendation:** Add a per‑client `SemaphoreSlim` (e.g., max concurrency = 8) inside `HandleClientAsync`.  
   **Acceptance criteria:** When a client sends >8 concurrent requests, additional requests are queued; under a 100‑request burst the server CPU usage stays below 80%; other clients are not starved.

4. **Title:** Fix concurrent consent handling without synchronization in YahooSessionManager  
   **Priority:** 1 | **Severity:** High  
   **Area:** Concurrency / cookie corruption  
   **Evidence:** `SendAsync` and `SendSimpleGetAsync` both call `AcceptConsentFormAsync` without a lock; `HttpClient` and `CookieContainer` are not thread‑safe for modifications.  
   **Recommendation:** Serialize consent acceptance using the existing `_refreshLock` or a dedicated `SemaphoreSlim`.  
   **Acceptance criteria:** When two threads simultaneously trigger a consent redirect, only one consent POST is sent and cookie state remains consistent; no `AccessViolationException` observed.

5. **Title:** Replace source‑file string‑matching tests with behavioral tests  
   **Priority:** 1 | **Severity:** High  
   **Area:** Test reliability / CI false positives  
   **Evidence:** Nb048‑Nb051 tests use `Assert.Contains("StartNewsRefreshLoop();", source)` – break on any formatting or comment change.  
   **Recommendation:** Replace with actual behavior‑based integration tests or remove if coverage exists elsewhere.  
   **Acceptance criteria:** All source‑file string‑match tests are replaced or removed; no test breaks on whitespace‑only or comment‑only production changes.

6. **Title:** Set explicit HttpClient timeout in YahooSessionManager to prevent indefinite hang  
   **Priority:** 1 | **Severity:** High  
   **Area:** Network / UI freeze  
   **Evidence:** `_httpClient` created without custom `Timeout` (default 100 s); a slow response can freeze the UI if awaited on dispatcher thread.  
   **Recommendation:** Apply a configurable timeout (e.g., 30 s) via `_options.HttpTimeout`.  
   **Acceptance criteria:** When the Yahoo endpoint stalls beyond the timeout, the HTTP call is cancelled and the application remains responsive; the timeout is user‑configurable.

7. **Title:** Convert PersistentTtlCache file I/O to async to avoid thread‑pool starvation  
   **Priority:** 1 | **Severity:** High  
   **Area:** Thread‑pool / scalability  
   **Evidence:** `GetAsync` and `SetAsync` use synchronous `File.OpenRead`/`File.Create` on thread‑pool threads.  
   **Recommendation:** Use `new FileStream(..., FileOptions.Asynchronous)` for async file opening.  
   **Acceptance criteria:** Under concurrent metadata requests, no thread‑pool stall occurs; `GetAsync`/`SetAsync` complete without blocking a thread during the OS open call.

### Priority 2 (Medium/High, material improvement in throughput, reliability, or maintainability)

8. **Title:** Parallelize restore and publish in build/publish-safe-temp.ps1  
   **Priority:** 2 | **Severity:** Medium | **Area:** Build pipeline throughput  
   **Evidence:** Each project is restored and published sequentially with `--disable-parallel`.  
   **Recommendation:** Use a single `dotnet restore` on the solution; remove `--disable-parallel` from publish calls.  
   **Acceptance criteria:** Build time reduces by ≥30%; timeout risk on slow machines is mitigated.

9. **Title:** Remove stale restore‑asset copying in build-safe-temp.ps1 and enable full restore  
   **Priority:** 2 | **Severity:** High | **Area:** Build reliability  
   **Evidence:** Copies `project.assets.json` from original repo and uses `--no-restore` without verifying freshness.  
   **Recommendation:** Replace with a full `dotnet restore` in the temp workspace.  
   **Acceptance criteria:** After a package version change, the temp build uses the correct assets; no silent dependency drift.

10. **Title:** Make package install scripts idempotent and add retry error handling  
    **Priority:** 2 | **Severity:** High | **Area:** Build/release reliability  
    **Evidence:** `install-choco.ps1` uses `Invoke-Expression` without retry; `install-vm-qa-tools.ps1` installs packages even if present.  
    **Recommendation:** Add `-WhatIf` checks, `try/catch` with retry (3 attempts), and exit‑code verification.  
    **Acceptance criteria:** Re‑running the script on a provisioned VM skips already‑installed packages; transient download failures are retried without failing the pipeline.

11. **Title:** Parallelize VM package installation with throttled concurrency  
    **Priority:** 2 | **Severity:** Medium | **Area:** Pipeline throughput  
    **Evidence:** `install-vm-qa-tools.ps1` installs 10+ choco packages sequentially.  
    **Recommendation:** Use `ForEach-Object -Parallel 3` or `Start-ThreadJob` with a throttle limit.  
    **Acceptance criteria:** Total installation time reduces by ≥50%; no increase in package installation failures due to contention.

12. **Title:** Replace recursive file system scans with targeted `Test-Path` calls in VM inventory scripts  
    **Priority:** 2 | **Severity:** Medium | **Area:** I/O / pipeline variability  
    **Evidence:** `Get-ChildItem -Recurse` over `Program Files` and user profile takes 2–5 minutes.  
    **Recommendation:** Use `Test-Path` on known install directories.  
    **Acceptance criteria:** VM inventory scripts complete in <30 s; no access‑denied errors.

13. **Title:** Add structured cancellation and cleanup hooks to host scripts (Invoke-VmBuildTest.ps1 etc.)  
    **Priority:** 2 | **Severity:** Medium | **Area:** Process management  
    **Evidence:** No `CancellationToken` propagation; Ctrl+C may leave orphaned guest processes.  
    **Recommendation:** Add `trap`/`Register-EngineEvent` and remote cleanup command on exit.  
    **Acceptance criteria:** On host script termination, the remote harness is always aborted; no stale Config/Desktop/agent processes remain after cancellation.

14. **Title:** Enable test parallelism by removing global `DisableTestParallelization = true` and fixing environmental dependencies  
    **Priority:** 2 | **Severity:** High | **Area:** Test throughput  
    **Evidence:** Environment‑dependent tests force serialisation; CI test time is double.  
    **Recommendation:** Refactor tests to use `EnvironmentScope` or injectable config; remove assembly‑level parallelization ban.  
    **Acceptance criteria:** All tests that modified environment variables are refactored; parallel execution is enabled; test suite completes in <50% of current time.

15. **Title:** Reduce reflection‑based private method testing using `InternalsVisibleTo` or extracted interfaces  
    **Priority:** 2 | **Severity:** Medium | **Area:** Test maintainability  
    **Evidence:** Multiple test files use `InvokePrivate`/`BindingFlags.NonPublic` for private methods.  
    **Recommendation:** Add `[assembly: InternalsVisibleTo("PortfolioSaver.Tests")]` and change target methods to `internal`; or extract interfaces.  
    **Acceptance criteria:** All reflection tests are replaced with direct calls; no test breaks on method signature changes.

16. **Title:** Replace real file I/O in unit tests with an `IFileSystem` abstraction  
    **Priority:** 2 | **Severity:** Medium | **Area:** Test speed/flakiness  
    **Evidence:** Tests create real temp directories and use `DeleteDirectoryWithRetry` (up to 2 s per call).  
    **Recommendation:** Introduce an `IFileSystem` interface; use in‑memory implementation in tests.  
    **Acceptance criteria:** File‑based unit tests run without touching disk; `DeleteDirectoryWithRetry` is removed; test execution time decreases by >20%.

17. **Title:** Replace repeated `Task.Delay` polling in `TraceLog.ProcessQueueAsync` with a signaling mechanism  
    **Priority:** 2 | **Severity:** Medium | **Area:** CPU efficiency  
    **Evidence:** Empties the queue every 25 ms via polling, wasting CPU.  
    **Recommendation:** Use a `SemaphoreSlim` signaled on enqueue.  
    **Acceptance criteria:** Idle trace worker uses zero CPU (<1% per core); enqueue releases the worker immediately.

18. **Title:** Reuse a single `HttpClient` instance in `InternetProbeService` instead of creating a new one per probe  
    **Priority:** 2 | **Severity:** Medium | **Area:** Memory/socket efficiency  
    **Evidence:** `CreateProbeClient()` creates a new `HttpClient` (with shared handler) every 10 s.  
    **Recommendation:** Store a static `HttpClient` instance.  
    **Acceptance criteria:** `ProbeInternetAsync` does not allocate a new `HttpClient`; object allocations per probe drop by >10.

19. **Title:** Replace SHA256 with a fast non‑cryptographic hash for protocol payload checksum  
    **Priority:** 2 | **Severity:** Medium | **Area:** CPU/GC overhead  
    **Evidence:** `ProtocolIntegrity.ComputePayloadChecksum` uses `SHA256` – heavy for corruption detection only.  
    **Recommendation:** Replace with `System.IO.Hashing.XxHash64` or `Crc32`.  
    **Acceptance criteria:** CPU time per checksum reduces by 10×; functional tests with the new hash continue to detect corruption.

20. **Title:** Use `ArrayPool` in `LengthPrefixedProtocolStream` to reduce allocation rate  
    **Priority:** 2 | **Severity:** Medium | **Area:** Memory/GC  
    **Evidence:** `ReadExactAsync` allocates a new `byte[]` per read.  
    **Recommendation:** Rent buffers from `ArrayPool<byte>.Shared` and return to pool.  
    **Acceptance criteria:** Under sustained load (100 quote/s), gen0 collections reduce by ≥30%; no buffer leaks.

21. **Title:** Add idle timeout for server client connections  
    **Priority:** 2 | **Severity:** Medium | **Area:** Resource leak  
    **Evidence:** No timeout; zombie connections consume memory and sockets indefinitely.  
    **Recommendation:** Use `CancellationTokenSource.CreateLinkedTokenSource` with a resetting timeout per message.  
    **Acceptance criteria:** A client that stays idle for the configured timeout is disconnected; after reconnect, it works normally.

22. **Title:** Add capacity limit to `MemoryTtlCache` to prevent unbounded growth  
    **Priority:** 2 | **Severity:** Medium | **Area:** Memory leak  
    **Evidence:** Only TTL expiration; no maximum size.  
    **Recommendation:** Add a configurable capacity with LRU eviction or use `IMemoryCache`.  
    **Acceptance criteria:** Cache size never exceeds the configured maximum; eviction does not increase cache miss rate significantly.

23. **Title:** Add retry with exponential backoff and jitter to `RetryPolicyService`  
    **Priority:** 2 | **Severity:** Medium | **Area:** Network resilience  
    **Evidence:** Linear backoff without jitter increases contention under transient failures.  
    **Recommendation:** Use `TimeSpan.FromMilliseconds(Math.Min(1000 * Math.Pow(2, attempt - 1), maxDelay)) + Random.Next(0, 200)`.  
    **Acceptance criteria:** Retry storms are eliminated; average success rate under transient failures improves.

24. **Title:** Consolidate multiple `DispatcherTimer` instances in `ScreensaverSceneControl` into one  
    **Priority:** 2 | **Severity:** Medium | **Area:** UI fluidity  
    **Evidence:** 11 separate timers cause dispatcher pressure.  
    **Recommendation:** Merge into a single 33 ms timer with internal scheduled action checks.  
    **Acceptance criteria:** Dispatcher queue depth decreases; UI rendering remains smooth under load.

25. **Title:** Batch `UpdateLayout()` calls in graph warmup to prevent per‑graph spikes  
    **Priority:** 2 | **Severity:** High | **Area:** UI jitter  
    **Evidence:** `ApplyOrUpdateGraph` calls `UpdateLayout()` synchronously for each of up to 16 graphs.  
    **Recommendation:** Defer `UpdateLayout()` to after the batch or use `InvalidateVisual()`.  
    **Acceptance criteria:** Graph population does not cause visible stutter; initial card layout completes within 2 s.

26. **Title:** Fetch RSS feeds in parallel in `FinanceNewsService`  
    **Priority:** 2 | **Severity:** Medium | **Area:** Network latency  
    **Evidence:** Three feeds fetched sequentially.  
    **Recommendation:** Use `Task.WhenAll` with a global timeout and per‑feed cancellation.  
    **Acceptance criteria:** News refresh wall‑clock time reduces from sum of latencies to the slowest single feed.

27. **Title:** Simplify `ProviderBudgetLedgerService` locking to a single lock to avoid deadlock  
    **Priority:** 2 | **Severity:** Medium | **Area:** Multithreading  
    **Evidence:** Two‑lock pattern (`_sync` and `_saveSync`) is fragile.  
    **Recommendation:** Use a single `lock` or `AsyncLock` for both state and save.  
    **Acceptance criteria:** No deadlock possible; ledger operations remain correct under concurrent calls.

28. **Title:** Add `ConfigureAwait(false)` to async calls in `StartupCoordinator` that do not need UI continuation  
    **Priority:** 2 | **Severity:** Medium | **Area:** UI dispatcher congestion  
    **Evidence:** Many `await` without `ConfigureAwait(false)` marshal back to UI thread unnecessarily.  
    **Recommendation:** Add `ConfigureAwait(false)` to all non‑UI continuations.  
    **Acceptance criteria:** WPF dispatcher queue utilization reduces; startup scene setup completes faster.

29. **Title:** Convert `ScreensaverSettingsService.Load` to async to avoid UI thread blocking  
    **Priority:** 2 | **Severity:** Medium | **Area:** UI startup responsiveness  
    **Evidence:** `File.ReadAllText` and `JsonSerializer.Deserialize` synchronous calls called from UI thread.  
    **Recommendation:** Use `File.ReadAllTextAsync` and `JsonSerializer.DeserializeAsync`; make callers async.  
    **Acceptance criteria:** Settings load does not block the UI thread; startup scene appears immediately.

30. **Title:** Parallelize weather city fetch in `WorldWeatherService`  
    **Priority:** 2 | **Severity:** Medium | **Area:** Network latency  
    **Evidence:** Cities fetched sequentially.  
    **Recommendation:** Use `Task.WhenAll` with a concurrency limit (SemaphoreSlim 5).  
    **Acceptance criteria:** Total weather retrieval time reduces to ~max single city latency; no rate limit violations.

31. **Title:** Offload `NewsFlasherControl` headline preparation from UI tick to background thread  
    **Priority:** 2 | **Severity:** High | **Area:** UI jitter  
    **Evidence:** `PrepareHeadline` runs synchronously inside a 40 ms `DispatcherTimer` tick – creates `FormattedText` and regex.  
    **Recommendation:** Use `Task.Run` for word wrapping and measurement, dispatch result to UI.  
    **Acceptance criteria:** Ticker playback does not drop frames; headline preparation completes before the next segment.

32. **Title:** Gate `TraceLog.InfoState` calls in hot ticker path behind debug condition  
    **Priority:** 2 | **Severity:** Medium | **Area:** Performance / IO  
    **Evidence:** Called on every tick (25 Hz) – thousands of trace lines per minute.  
    **Recommendation:** Mark with `[Conditional("DEBUG")]` or sample at low rate.  
    **Acceptance criteria:** Release builds produce <10 trace lines per ticker cycle; disk I/O from tracing is negligible.

33. **Title:** Use `VirtualizingStackPanel` and cached measurements in `TickerTapeControl` to avoid full rebuild  
    **Priority:** 2 | **Severity:** Medium | **Area:** UI fluidity  
    **Evidence:** `RefreshMotionMetrics` calls `UpdateLayout()` and re‑measures all items synchronously.  
    **Recommendation:** Replace manual canvas with an `ItemsControl` + `VirtualizingStackPanel`; cache item widths.  
    **Acceptance criteria:** Window resize and data updates do not cause frame drops; memory usage remains stable.

34. **Title:** Add cancellation and debouncing to `NewsFlasherControl` headline changes  
    **Priority:** 2 | **Severity:** Medium | **Area:** UI responsiveness under burst  
    **Evidence:** No cancellation when `Headlines` collection changes; can accumulate pending work.  
    **Recommendation:** Stop and restart playback timer on change; debounce multiple rapid changes (500 ms window).  
    **Acceptance criteria:** Rapid headline updates do not cause UI lag; stale headlines are not displayed.

35. **Title:** Replace 1‑s connectivity polling timer with `NetworkChange.NetworkAvailabilityChanged` event  
    **Priority:** 2 | **Severity:** High | **Area:** UI / network  
    **Evidence:** `_stateTimer` ticks every second and calls `IsInternetAvailableAsync` possibly making HTTP request.  
    **Recommendation:** Subscribe to `NetworkChange.NetworkAvailabilityChanged`; fallback to 30 s interval if event unavailable.  
    **Acceptance criteria:** No periodic network poll; connectivity state updates immediately on network change.

36. **Title:** Aggregate per‑symbol progress reports in symbol validation to reduce dispatcher load  
    **Priority:** 2 | **Severity:** Medium | **Area:** UI fluidity  
    **Evidence:** `ValidateAsync` reports progress for every symbol, flooding dispatcher with up to 200 posts.  
    **Recommendation:** Throttle to ~10 updates/second or report only on state changes.  
    **Acceptance criteria:** Validation UI remains responsive; no jitter during validation of 500 symbols.

37. **Title:** Use `ArrayPool` or slice instead of LINQ `Skip/Take` for symbol batching  
    **Priority:** 3 | **Severity:** Medium | **Area:** Allocation/performance  
    **Evidence:** `ChunkSymbols` uses `symbols.Skip(index).Take(size).ToList()` – O(N²) enumerators.  
    **Recommendation:** Use `symbols.GetRange(index, count)` or span slicing.  
    **Acceptance criteria:** Allocation for batching 500 symbols drops by factor >10; no change in batch boundaries.

38. **Title:** Replace `MemoryTtlCache<JsonDocument>` with raw string storage to avoid cloning and disposal risk  
    **Priority:** 2 | **Severity:** Medium | **Area:** Memory/performance  
    **Evidence:** `GetCachedJsonAsync` clones via `JsonDocument.Parse(cached.RootElement.GetRawText())` on every cache hit.  
    **Recommendation:** Store the raw JSON string; parse into `JsonDocument` on retrieval.  
    **Acceptance criteria:** Per‑cache‑hit allocation reduces by ~50%; no risk of accessing a disposed `JsonDocument`.

### Priority 3 (Low severity or impact, still testable and worth fixing)

39. **Title:** Fix process leak in delayed cleanup spawned via `Start-DelayedInstallRootCleanup`  
    **Priority:** 3 | **Severity:** Low | **Area:** Uninstall reliability  
    **Evidence:** Spawned background process may survive uninstall termination.  
    **Recommendation:** Move retry loop into main script and rely on `waituntilterminated`.  
    **Acceptance criteria:** After uninstall completes, no orphaned cleanup processes remain; removal eventually succeeds.

40. **Title:** Run smoke test scenarios in parallel using `ForEach-Object -Parallel`  
    **Priority:** 3 | **Severity:** Low | **Area:** Test throughput  
    **Evidence:** 18 sequential independent scenarios take 30–60 s.  
    **Recommendation:** Parallelise with fallback to sequential if PS7 unavailable.  
    **Acceptance criteria:** Wall‑clock time reduces by ≥3×;
