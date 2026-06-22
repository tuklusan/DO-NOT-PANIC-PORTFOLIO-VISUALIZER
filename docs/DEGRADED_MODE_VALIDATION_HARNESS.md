# Degraded Mode Validation Harness

CR-064 establishes the deterministic fault-injection foundation for degraded-mode validation. The harness must reproduce degraded Yahoo/YFinance conditions without breaking SSH, desktop automation, or the VM control channel.

## Principle

- Fault injection is product-local and deterministic.
- The VM network path remains available to the harness.
- YFinance.NET server responses are delayed or failed at the protocol boundary.
- All injected conditions are traceable in both harness artifacts and YFinance circular traces.
- DeepSeek artifact review remains advisory; Codex/project ownership makes the final pass/fail and CR-generation calls.

## Supported Profiles

| Profile | Injection Timing | Server Behavior |
| --- | --- | --- |
| `none` | whole run | normal behavior |
| `offline-at-start` | before desktop/config startup | market-data requests fail with `network_lost` |
| `offline-during-config-validation` | immediately before config `Validate` | market-data requests fail with `network_lost` |
| `offline-during-runtime` | immediately before fullscreen/runtime exercise | market-data requests fail with `network_lost` |
| `offline-then-recover-runtime` | runtime fault, then recovery clear during the same run | market-data requests fail with `network_lost`, then resume normally |
| `high-latency-yfinance` | whole run | market-data requests are delayed, then continue normally |
| `upstream-throttled` | whole run | market-data requests fail with `upstream_throttled` |
| `timeout` | whole run | market-data requests delay, then fail with `timeout` |

Profiles apply to YFinance.NET market-data protocol operations only:

- `get_quote`
- `get_quotes`
- `get_history`
- `get_market_timing`
- `get_ticker_info`

Health, hello, goodbye, and server-status requests are intentionally not faulted.

## Artifact Evidence

Each VM UX run writes:

- `yfinance-fault-profile.json`: the active profile file consumed by the YFinance.NET server.
- `fault-injection-events.log`: harness-side timestamped profile transitions.
- `trace/yfinance.circular.log`: server-side `FaultInjectionProfileLoaded`, `FaultInjectionDelayStart`, and `FaultInjectionApplied` trace lines.
- `ux-deep-summary.json`: selected `FaultProfile`, `FaultProfilePath`, and `FaultTimelinePath`.

## Expected User Experience

Healthy:

- Tickers, graph cards, macro cards, world markets, news, and background continue normal behavior.
- Top-left status remains live or intentionally blank where timing data is unavailable.

Offline or upstream failure:

- UI remains responsive.
- The scene does not blank out or freeze in batches.
- Existing values are either retained as stale/degraded or replaced with consistent unavailable placeholders.
- Top-left freshness/status text must not falsely imply fresh market data.
- Config validation either completes with actionable failure text and re-enabled controls, or reaches OK/Cancel only after successful validation.

High latency:

- UI remains responsive while the affected symbol/request is pending.
- Other independent visual lanes continue operating.
- Late responses are accepted if still relevant and ignored if obsolete.

Recovery:

- When a profile file is changed back to `none`, subsequent market-data requests resume normal handling without restarting the harness.
- The first fresh response after recovery must be traceable and visibly reflected without whole-scene redraw.
- Long fault-injection runs require enough retained app and YFinance.NET trace to span the injected fault, the recovery clear, and at least one post-recovery quote response. The default circular trace cap is 32 MB per trace, configurable with `DONOTPANICPORTFOLIOVISUALIZER_TRACE_MAX_MB` for unusually long diagnostic runs.
- Runtime recovery proof must come from `runtime-freshness-events.log`, where the harness records direct UI freshness plus `trace_age_seconds`; app/server circular trace tails can corroborate recovery, but line ordering in those tails alone is not sufficient because those tail lines do not carry harness freshness-age metadata.

## Example

```powershell
.\build\vm\Invoke-VmBuildTest.ps1 -RunUxDeep -GuestScreensaverDurationMinutes 30 -FaultProfile offline-during-runtime
```

Autonomous matrix example:

```powershell
.\build\validation\Invoke-AutonomousVisualValidation.ps1 `
  -VmCycles 7 `
  -RequiredConsecutiveCleanRuns 7 `
  -GuestScreensaverDurationMinutes 30 `
  -FaultProfiles none,offline-at-start,offline-during-config-validation,offline-during-runtime,offline-then-recover-runtime,high-latency-yfinance,upstream-throttled `
  -AcknowledgeExternalReviewSecretScan
```

The autonomous loop records the configured `faultProfiles` list and each cycle's selected `faultProfile` in `autonomous-visual-validation-summary-*.json`.

## Autonomous Summary Schema

`autonomous-visual-validation-summary-*.json` is intentionally append-only for compatibility. Consumers should ignore unknown fields.

Current fields:

- `generatedAt`: local timestamp for summary creation.
- `requiredConsecutiveCleanRuns`: clean-run threshold requested by the caller.
- `consecutiveCleanRuns`: clean-run streak achieved by the loop.
- `vmCyclesRequested`: total VM cycles requested by the caller.
- `guestScreensaverDurationMinutes`: requested guest runtime per VM cycle.
- `captureIntervalSeconds`: screenshot capture cadence used by the VM harness.
- `faultProfiles`: ordered list supplied to `-FaultProfiles`; cycles use this list by modulo rotation.
- `completed`: `true` only when the clean-run threshold was reached.
- `cycles`: per-cycle records; each record includes the selected `faultProfile`.
