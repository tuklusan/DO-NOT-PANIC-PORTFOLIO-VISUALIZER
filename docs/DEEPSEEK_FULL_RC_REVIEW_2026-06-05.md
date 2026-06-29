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

# DeepSeek Full Release-Candidate Review - 2026-06-05

- Review type: full tracked codebase and documentation end-to-end release-candidate review
- Reviewer: DeepSeek via `build/Run-DeepSeekCodeReview.ps1` and chunked full-repository packet process
- Source artifact directory: `build/deepseek-review/full-rc-20260605-142319`
- Tracked-file manifest: `build/deepseek-review/full-rc-20260605-142319/tracked-file-manifest.json`
- Result: NOT ACCEPTABLE for production release until Critical and High findings are resolved

Policy note: every future full tracked codebase and documentation end-to-end review must be preserved as a versioned document under `docs/` with date, artifact directory, scope, reviewer, and final synthesis.

## Synthesis of Full Release-Candidate Review

All five review chunks (2–5) have been analyzed end‑to‑end. The entire tracked codebase and documentation are covered; no files or directories were omitted from the review. Below are the consolidated findings, deduplicated across chunks, grouped by severity, with concrete file paths and remediation.

---

### 🔴 Critical (Must Fix Before Release)

#### C‑1: Deadlock risk from synchronous blocking of async calls on the UI thread
- **Files**:  
  - `src/PortfolioSaver.Config/App.xaml.cs` (lines 30, 44–49)  
  - `src/PortfolioSaver.Desktop/App.xaml.cs` (lines 28, 45–49)  
  - `src/PortfolioSaver.Screensaver/App.xaml.cs` (line 27)  
- **Issue**: `OnStartup` and `OnExit` call `.GetAwaiter().GetResult()` on async methods (`EnsureOwnedServerAsync`, `StopOwnedServerAsync`). This blocks the UI thread, can cause deadlocks, and in the screensaver also calls `GetResult()` synchronously.  
- **Remediation**: Replace with `async void` event handlers (e.g., `protected override async void OnStartup(StartupEventArgs e)`) and use `await` directly. For shutdown, use fire‑and‑forget with logging or restructure the lifecycle.

#### C‑2: Server binds to all network interfaces (0.0.0.0) – network exposure
- **File**: `docs/YFINANCE_NET_ICD.md` §4.1  
- **Issue**: The ICD states “bind target: accepts connections from any IP address”. For a desktop app that typically runs on a user’s local network, this exposes the YFinance.NET server to any machine on the same subnet.  
- **Remediation**: Default the server listener to `127.0.0.1`. Add a `--bind-address` or `--allow-remote` flag for standalone mode. Update the ICD to reflect the secure default.

#### C‑3: Partial quote results cause complete data loss for a batch
- **File**: `src/PortfolioSaver.Data/Providers/YahooFinanceQuoteProvider.cs` (line 103)  
- **Issue**: When `GetQuotesAsync` returns partial results (e.g., one typo symbol), it throws `PartialQuoteResultException`. The caller in `ScreensaverSceneControl` discards the entire batch, so a single missing symbol causes all other quotes to be lost, resulting in blank tapes and graphs.  
- **Remediation**: Remove the throw and return the partial quotes with unresolved symbols omitted, or have the caller handle `PartialQuoteResultException` to still apply the `PartialQuotes` collection.

#### C‑4: Plain‑text DeepSeek API key leaks into trace logs and settings file
- **Files**:  
  - `src/PortfolioSaver.Presentation/Services/FinanceNewsService.cs` (lines 335–339)  
  - `src/PortfolioSaver.Settings/Services/SettingsFileService.cs` (`Save` / `CreateSanitizedCopy`)  
  - `src/PortfolioSaver.Shared/Diagnostics/TraceLog.cs` (any `Enqueue` call)  
- **Issue**: The API key is resolved and used in payloads but is also stored in `AppSettings.DeepSeekApiKey`. Structured trace logs may contain the key (e.g., `"api_key"` or `"authorization"` header). No masking is applied.  
- **Remediation**: Never log the key. Add a sanitizer in `TraceLog` for any field whose key contains `key`, `secret`, `token`, or `password`. Ensure `ResolveDeepSeekApiKey()` is called only at the point of use and not stored in a loggable field.

#### C‑5: Prompt injection surface in `FinanceNewsService`
- **File**: `src/PortfolioSaver.Presentation/Services/FinanceNewsService.cs` (`BuildSummarizedNewsPrompt`)  
- **Issue**: The prompt is constructed from user‑controlled feed headlines. A malicious RSS feed could inject instructions, causing the AI to ignore constraints, leak the system prompt, or generate harmful output.  
- **Remediation**: Separate user‑provided headlines from the instruction block. Use clear delimiters and instruct the model to treat headlines as untrusted data. Add input validation/normalization on headlines before inclusion.

#### C‑6: `QuoteRefreshPolicy` returns static hard‑coded values, ignoring `AppSettings`
- **File**: `src/PortfolioSaver.Presentation/Services/QuoteRefreshPolicy.cs` (all three public methods)  
- **Issue**: All methods return `UiSequentialCadence` (1 second) regardless of user settings. This forces excessive API calls and ignores `NewsRefreshMinutes` configuration, likely causing rate limiting and degrading user experience.  
- **Remediation**: Implement real logic: use `settings.NewsRefreshMinutes` or a configurable polling interval. At minimum respect the user‑set refresh duration.

#### C‑7: `YFinanceServerProcessManager` uses unsafe executable search paths
- **File**: `src/PortfolioSaver.Shared/Services/YFinanceServerProcessManager.cs` (`ResolveLaunchCommand` ~lines 130–190)  
- **Issue**: The method scans hardcoded candidate paths, including walking the directory tree to find a `.sln` file. In a deployed RC build this is insecure (a malicious binary in a parent directory could be launched) and non‑deterministic.  
- **Remediation**: Use only a fixed path relative to `AppContext.BaseDirectory` (e.g., `Path.Combine(AppContext.BaseDirectory, "..", "server", "YFinance.NET.Server.exe")`). Validate the server binary by hash or signature. Remove `GetRepoRoot()` logic.

---

### 🔴 High (Must Fix – Strongly Recommended Before Release)

#### H‑1: Plaintext autologon password written to registry (build harness)
- **File**: `build/vm/Guest-ConfigureDesktopAutomation.ps1` (lines ~53–59)  
- **Issue**: The script writes `$Password` directly into `HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\DefaultPassword` – a plaintext registry key accessible by any local admin or malware.  
- **Remediation**: Use LSA secrets or `StoredCredentials` infrastructure. If unavoidable, add a prominent warning comment and secure the registry ACL.

#### H‑2: Plaintext password exposed in PsExec command line (build harness)
- **File**: `build/vm/Invoke-VmBuildTest.ps1` (line ~176)  
- **Issue**: Password is passed as `-p` argument to PsExec. Any local process can read the command line (WMI/Win32_Process), exposing the credential.  
- **Remediation**: Use a temporary credentials file with restricted ACL, a scheduled task, or SSH key‑based authentication.

#### H‑3: `StartupCoordinator` – `LoadQuotesAsync` leaves pending tasks unobserved
- **File**: `src/PortfolioSaver.Presentation/Services/StartupCoordinator.cs` (`QueueQuotePipelineRequests`)  
- **Issue**: `_pendingQuotePipeline[symbol] = new PendingQuoteRequest(…, yahooFinanceProvider.GetQuotesAsync(…), …);` – the task is stored but not awaited. `DrainCompletedQuotePipeline` uses `.GetAwaiter().GetResult()` which can block the UI thread. Unobserved tasks can leak.  
- **Remediation**: Use `async` drain with `await` instead of `.GetResult()`. Ensure all pending requests are awaited or cancelled on shutdown.

#### H‑4: `NtpTimeService` has no timeout on DNS resolution
- **File**: `src/PortfolioSaver.Presentation/Services/NtpTimeService.cs` (line 57)  
- **Issue**: `Dns.GetHostAddressesAsync` does not respect UDP timeouts and can block for seconds. No cancellation token is passed.  
- **Remediation**: Use `Dns.GetHostAddressesAsync` with a linked cancellation token with a short timeout (e.g., 5 seconds). Apply `Task.WhenAny` with a timeout.

#### H‑5: `ProviderBudgetLedgerService` – thread‑unsafe file I/O and lock scope
- **File**: `src/PortfolioSaver.Presentation/Services/ProviderBudgetLedgerService.cs`  
- **Issue**: `LoadLedger` called outside lock in constructor creates a race. `Directory.CreateDirectory` and `File.WriteAllText` inside the lock block all budget reservations.  
- **Remediation**: Load on first access, not in constructor. Use `AsyncLock` or move file writes outside the critical section.

#### H‑6: `FinanceNewsService` – no retry backoff and no limit on external network calls
- **File**: `src/PortfolioSaver.Presentation/Services/FinanceNewsService.cs` (line 347 retry loop, outer catch)  
- **Issue**: Retry loop for empty responses has no backoff. Outer catch swallows all exceptions and falls back to stale news, which is acceptable but not robust.  
- **Remediation**: Add exponential backoff (200ms, 400ms) between retries.

#### H‑7: `SettingsFileService.Save` writes without atomicity – risk of corruption
- **File**: `src/PortfolioSaver.Settings/Services/SettingsFileService.cs` (line 40)  
- **Issue**: `File.WriteAllText` is not atomic; a crash mid‑write corrupts the file.  
- **Remediation**: Write to a temporary file, then rename (use `File.Replace`).

---

### 🟡 Medium (Should Fix in RC2 or Immediately Post-Release)

#### M‑1: Documentation uses absolute developer‑local paths
- **Files**: `build/vm/VM_OPERATIONS_RUNBOOK.md`, `docs/MANUAL_UI_QA_RESULTS_*.md`  
- **Issue**: Many paths rooted in `D:\Users\vagab\...` break for anyone else and violate reproducibility.  
- **Remediation**: Replace with relative repository paths or environment placeholders.

#### M‑2: Harness scripts lack unit tests
- **Files**: All `.ps1` files under `build/vm/`  
- **Issue**: No Pester tests for core helper functions. Only expensive end‑to‑end validation.  
- **Remediation**: Add Pester tests for `VmSshCommon.ps1`, `VmTraceQuoteEvidence.ps1`, `PostProcess-ReferenceSpotChecks.ps1`.

#### M‑3: Reliance on deprecated `SendKeys` and `mouse_event` (harness)
- **File**: `build/vm/Guest-UxDeepExercise.ps1`  
- **Issue**: `SendKeys` is fragile; `mouse_event` deprecated.  
- **Remediation**: Prefer UI Automation pattern invocation. Use `SendInput` via P/Invoke for mouse.

#### M‑4: Hard‑coded sleep durations create timing fragility (harness)
- **File**: `build/vm/Guest-UxDeepExercise.ps1` (multiple `Start-Sleep`)  
- **Issue**: Fixed delays may be too short on loaded VMs or too long on fast machines.  
- **Remediation**: Replace with adaptive waits that poll for expected UI state.

#### M‑5: Environment variable password parser does not trim or handle special chars (harness)
- **File**: `build/vm/VmSshCommon.ps1` (`Get-VmSshCredentialPartsFromEnv`)  
- **Issue**: Regex captures trailing spaces, newlines, and does not escape special characters.  
- **Remediation**: `.Trim()` values, document encoding, consider JSON credential format.

#### M‑6: `robocopy` exit code handling may silently ignore partial copy failures (harness)
- **File**: `build/vm/VmSshCommon.ps1` (lines ~168–175)  
- **Issue**: Treats any exit code >7 as error, but some >7 codes are warnings.  
- **Remediation**: Check for bit 8+ (`$exitCode -ge 8`).

#### M‑7: `test-secrets.json` uploaded without validation (harness)
- **File**: `build/vm/Push-VmWorkspace.ps1` (lines ~89–92)  
- **Issue**: No check that JSON is valid or contains expected keys.  
- **Remediation**: Validate JSON schema before upload.

#### M‑8: `Invoke‑VmPwshCommand` ignores stderr unless exit code non‑zero (harness)
- **File**: `build/vm/VmSshCommon.ps1` (`Invoke-VmPwshCommand`)  
- **Issue**: Warnings and non‑fatal errors are not surfaced.  
- **Remediation**: Log lines containing `WARNING:` or `Error:` from output.

#### M‑9: `InternetProbeService` – race condition on cache expiry
- **File**: `src/PortfolioSaver.Shared/Services/InternetProbeService.cs` (lines 38–46)  
- **Issue**: Cache is read under lock, but probe runs outside. Two threads can both probe simultaneously, causing redundant network calls.  
- **Remediation**: Use double‑check locking: re‑check cache after re‑acquiring lock.

#### M‑10: `VmAgent` unbounded log growth
- **File**: `src/PortfolioSaver.VmAgent/Program.cs` (Log method ~line 364)  
- **Issue**: Log file grows without limit in long soak tests.  
- **Remediation**: Implement max file size with rotation (e.g., 10 MB) or circular logging.

#### M‑11: `StartupCoordinator` no `CancellationToken` propagation to `_symbolProfileStore.Load`
- **File**: `src/PortfolioSaver.Presentation/Services/StartupCoordinator.cs` (line 295)  
- **Issue**: Blocking file read inside async enumerable with no cancellation support.  
- **Remediation**: Accept cancellation in `SymbolProfileStore.Load` or use `Task.Run` with token.

#### M‑12: `GlobalMarketsTapeControl` silently swallows exceptions for missing flag images
- **File**: `src/PortfolioSaver.Render/Controls/GlobalMarketsTapeControl.xaml.cs` (`GetFlagImageSource`)  
- **Issue**: Exceptions caught and null returned; no log.  
- **Remediation**: Log a warning for missing images. Validate flag code before attempting load.

#### M‑13: `NewsFlasherControl` width dependency on `ViewportHost.ActualWidth` may be 0
- **File**: `src/PortfolioSaver.Render/Controls/NewsFlasherControl.xaml.cs` (many places)  
- **Issue**: If control not yet measured, `ActualWidth` can be 0, causing `FormattedText` to throw or produce infinite height.  
- **Remediation**: Guard with check for `ActualHeight == 0` to skip playback.

---

### 🟢 Low (Accept for RC, Fix in Next Sprint)

#### L‑1: `ReleaseManifestGuard` bypassed in DEBUG builds
- **File**: `src/PortfolioSaver.Shared/Integrity/ReleaseManifestValidator.cs`  
- **Issue**: `#if DEBUG return true;` – developers may not notice integrity checks are disabled.  
- **Remediation**: Remove the bypass or add a `#warning` directive. Provide a development‑only manifest.

#### L‑2: `PortfolioSaver.Screensaver/App.xaml.cs` sync server start (duplicate of C‑1, but less critical)
- **File**: `src/PortfolioSaver.Screensaver/App.xaml.cs` (already covered in C‑1, keep here as reminder)

#### L‑3: `NetworkWaitingDetail` string format uses outdated refresh cadence
- **File**: `src/PortfolioSaver.Presentation/Services/StartupCoordinator.cs` (line 210)  
- **Issue**: Uses `FormatRefreshCadenceText` which returns `"1 seconds"` due to C‑6.  
- **Remediation**: Fix C‑6; UI will then show correct cadence.

#### L‑4: `FinanceNewsService.TryParseSpecialHeadline` returns `true` for empty text
- **File**: `src/PortfolioSaver.Presentation/Services/FinanceNewsService.cs` (line 327)  
- **Issue**: After stripping prefix, if text is empty, still returns `true`.  
- **Remediation**: Return false if `text` is empty after stripping.

#### L‑5: `InternetProbeService` – blocking `Thread.Sleep`
- **File**: `src/PortfolioSaver.Shared/Services/InternetProbeService.cs` (line inside `ProbeInternet`)  
- **Issue**: `Thread.Sleep(250)` blocks the calling thread.  
- **Remediation**: Make probe async and use `Task.Delay`. Note: low impact given short sleep.

#### L‑6: `YFinanceServerProcessManager` – synchronous `File.Exists` in async flow
- **File**: `src/PortfolioSaver.Shared/Services/YFinanceServerProcessManager.cs`  
- **Issue**: `File.Exists` called inside async method; acceptable for startup, but could block thread briefly.  
- **Remediation**: Cache resolved path after first successful lookup.

#### L‑7: `ScreensaverSceneControl` creates unused `HttpClient`
- **File**: `src/PortfolioSaver.Presentation/Controls/ScreensaverSceneControl.xaml.cs` (line 102)  
- **Issue**: `_runtimeQuoteHttpClient` never used, never disposed.  
- **Remediation**: Remove the field and factory call.

#### L‑8: Footer attribution concatenation may truncate or overflow
- **File**: `src/PortfolioSaver.Presentation/Controls/ScreensaverSceneControl.xaml.cs` (`UpdateFooterAttribution` ~line 1440)  
- **Issue**: Long strings may exceed `MaxWidth`.  
- **Remediation**: Use `TextTrimming="CharacterEllipsis"` or `TextWrapping` with responsive sizing.

#### L‑9: Hardcoded holiday rules in NYSE calendar may go stale
- **File**: `src/PortfolioSaver.Core/Services/NyseTradingCalendarSnapshot.cs` (`CreateOfflineFallback`)  
- **Issue**: Static holiday rules require manual updates when NYSE changes schedules.  
- **Remediation**: Add periodic online fetch of holiday calendar; fall back to static only on failure.

#### L‑10: ICD specifies client‑driven pacing, but UI uses a fixed timer
- **File**: `docs/YFINANCE_NET_ICD.md` §3.1, §15 vs. `ScreensaverSceneControl.xaml.cs` (timer‑based dispatch)  
- **Issue**: Documented client‑paced behavior not implemented; fixed timer may starve symbols.  
- **Remediation**: Change dispatch to use concurrency‑based pacing. Note: not blocking for RC1 if current behavior is acceptable.

---

### ✅ Conclusion

**Full review: complete.** All tracked source files and documentation across chunks 2–5 have been examined. No binary assets were omitted from the review (their hashes are present, but their contents were not decompiled).  

**Baseline acceptability: NOT ACCEPTABLE** – seven Critical and seven High findings must be resolved before a production release can be signed off. The Critical items (deadlocks, network exposure, data loss, API key leakage, prompt injection, static refresh policy, unsafe executable search) each pose a credible risk to security, stability, or user trust.  

**Recommended action:**  
- Resolve all Critical and High findings.  
- Address Medium findings in RC2 or immediately post‑release.  
- Low findings can be backlogged but should be scheduled for the next sprint.  

Once all Critical and High items are fixed and verified, the release candidate baseline will be acceptable.
