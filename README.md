# DO NOT PANIC PORTFOLIO VISUALIZER

Market-aware Windows desktop visualizer and configuration suite by **SANYALnet Labs**, written by **Supratim Sanyal**.

**License: MIT LICENSE**

This repository ships with the full [LICENSE](LICENSE) text in-tree and is licensed under the **MIT LICENSE**, including the standard warranty and liability disclaimer.

## Overview

**DO NOT PANIC PORTFOLIO VISUALIZER** is a .NET 10 / WPF project centered on a Windows desktop app with an immersive fullscreen mode, live market display, ticker tapes, floating graph cards, news, exchange backgrounds, and animated overlays.

This repository is maintained as a **Visual Studio 2022-first** codebase for Windows x64 development and deployment.

## Core Features

- Multi-tape portfolio ticker display with configurable symbols, directions, and speed presets
- Floating graph cards designed as compact sparkline overlays
- UTC-pinned top-right status clock plus exchange-local times in the Global Markets lane
- Direction-colored graph rendering: green for rising segments, red for falling segments
- Slow, continuous free-roaming motion for graph cards and other floating overlay elements
- News headline scroller with configurable RSS mode or DeepSeek-based summarized-financial-news mode
- Dynamic background image system with managed exchange photos or user custom folders
- YFinance.NET-backed Yahoo retrieval with 1-second one-by-one pacing, batch quote refreshes, and observable rate-limit controls
- Offline-aware UI behavior and network gating for validation workflows

## Configuration App (WPF)

- Rich settings UI for:
  - YFinance.NET runtime status and refresh controls
  - DeepSeek API key, endpoint URL, and model ID for summarized financial news
  - Ticker tapes and graph overlays
  - Refresh intervals (portfolio, off-hours, news, backgrounds)
- Real-time and apply-time symbol validation flow
- Auto-name support for symbols when provider metadata resolves display names
- Bundled help/about/license reference content shipped with the config app assets
- Revision-3 branding assets are now wired into desktop/config/screensaver icons and the desktop About dialog splash surface

## Installer and Licensing UX

- Standalone installer bootstrap (`PortfolioSaverScreensaverSetup`)
- Elevation-aware installation flow (UAC)
- MIT license agreement dialog before install:
  - Full MIT text shown in a scroll box
  - **Accept** enabled only after scrolling to the end and checking agreement
  - Direct button to open the official MIT license page
- Local fallback license text is bundled so license display still works without network access

Official MIT reference:
- [MIT License (Open Source Initiative)](https://opensource.org/license/mit/)

## Data, Caching, and Runtime Paths

- Settings file: `%LocalAppData%\PortfolioSaver\settings.json`
- Protected secret store: `%LocalAppData%\PortfolioSaver\provider-secrets.json`
- News cache: `%LocalAppData%\PortfolioSaver\finance-news-cache.json`
- Historical cache: `%LocalAppData%\PortfolioSaver\Caches\History`
- Cache policy:
  - Runtime YFinance.NET memory and metadata caches are capped at 10 minutes
  - No separate app-level quote cache is maintained; quote reuse comes from YFinance.NET
  - Per-symbol JSON history files
  - Automatic purge of history files older than 14 days

## Desktop App Modes

- `PortfolioSaver.Desktop` is the primary app host
- fullscreen toggle from `View -> Full Screen`
- `Esc` exits fullscreen back to windowed mode
- legacy screensaver `/s`, `/c`, and `/p <HWND>` paths remain for compatibility during Beta 5.6

## Project Structure

- `src/PortfolioSaver.Desktop` - primary desktop executable host
- `src/PortfolioSaver.Screensaver` - legacy screensaver compatibility host
- `src/PortfolioSaver.Config` - thin configuration launcher
- `src/PortfolioSaver.Settings` - shared settings window, view models, services, and content
- `src/PortfolioSaver.Presentation` - reusable scene host and runtime presentation services
- `src/PortfolioSaver.VmAgent` - remote desktop-session agent for Windows target UX automation
- `src/PortfolioSaver.Render` - WPF visual controls and scene rendering
- `src/PortfolioSaver.Data` - runtime YFinance.NET integration, caches, and scheduling services
- `src/PortfolioSaver.Core` - domain models, constants, validation rules
- `src/PortfolioSaver.Media` - background image and transition services
- `src/PortfolioSaver.Shared` - shared helpers, identity/version, licensing utilities
- `YFinance.net` - standalone sync-friendly .NET port of `tuklusan/yfinance` plus the VM-proven exerciser
- `src/PortfolioSaver.Installer` - standalone installer bootstrap app
- `tests/PortfolioSaver.Tests` - automated unit tests

## Build and Run (Visual Studio 2022)

## Prerequisites

- Windows 10/11 x64
- Visual Studio 2022
- Desktop development with .NET workload
- .NET 10 SDK
- PowerShell 7 (`pwsh`) for mandatory DeepSeek workflow gates and autonomous validation scripts

## Recommended workflow

1. Open `PortfolioScreensaver.sln` in Visual Studio 2022.
2. Select `Debug | x64` for development.
3. Rebuild solution.
4. Use startup project:
   - `PortfolioSaver.Desktop` for runtime/visual behavior
   - `PortfolioSaver.Config` for settings work
5. For legacy screensaver argument testing only, set command args to `/s`, `/c`, or `/p 12345`.

Project workflow hard stop: DeepSeek API access is mandatory before commit, push, local validation, VM validation, or automated workflow execution. Verify access with:

```powershell
.\build\Test-DeepSeekWorkflowGate.ps1
```

If the gate cannot find a key or cannot reach DeepSeek, stop until access is restored. The key may come from `DEEPSEEK_API_KEY`, `PORTFOLIOSAVER_DEEPSEEK_API_KEY`, or ignored local `build\vm\test-secrets.json`.
Missing-key waivers and skip-review switches are intentionally unsupported; the live gate makes one minimal DeepSeek API probe before the normal review step.

## Installer Build Path

Use the scripts under `build/` for publish and packaging workflows, especially:

- `build/build-safe-temp.ps1`
- `build/publish-safe-temp.ps1`
- `build/publish-standalone-installer.ps1`

## Current Baseline

- Remote Git history has been intentionally rebased to start at the `BETA-5.4` baseline
- Current development/version lane is `BETA-7`
- Product identity:
  - Application: **DO NOT PANIC PORTFOLIO VISUALIZER**
  - Publisher: **SANYALnet Labs**
  - Author: **Supratim Sanyal**

## Documentation

The active documentation set has been intentionally reduced to a small core:

- `BUILD_AND_DEPLOY.md` - Visual Studio build, run, publish, and installer-sandbox workflow
- `docs/BETA6_AUDIT_STATE.json` - single canonical machine-maintained audit, test, and release-gate state
- `build/vm/VM_OPERATIONS_RUNBOOK.md` - repeatable SSH-first remote Windows UX validation workflow using PortfolioSaver.VmAgent and WinAppDriver in the interactive session
- `YFinance.net/PORTING_PLAN.md` - upstream sync rules, responsibility map, and standalone YFinance.NET proof plan
- `build/vm/test-secrets.json` - ignored local-only remote-test secret overlay for API keys, including DeepSeek, when you need live remote validation

## Remote Harness Policy

The current remote Windows validation harness is now the pinned supported approach:

- local green tests first
- `build/publish-safe-temp.ps1`
- `build/vm/Push-VmWorkspace.ps1`
- agent-based interactive UX validation through `PortfolioSaver.VmAgent`

Current canonical harness guardrails also include:

- automatic purge of obsolete VM build/test artifacts whenever free space under `C:\vmharness\portfolio-saver` falls below `8 GB`
- a quiet no-op startup launcher for `PortfolioSaver.VmAgent` when the staged agent executable is not present yet
- reference spot checks do not call Yahoo APIs directly; they compare displayed UI values against `QuoteResponseObserved` values in the YFinance.NET circular trace, proving UI/rendering consistency while YFinance.NET remains the sole Yahoo-facing runtime boundary
- independent upstream market-data correctness is owned by YFinance.NET-specific tests and VM proofs, not by desktop harness scripts

The current known-good clean proof path is:

- session-1 `PortfolioSaver.VmAgent`
- `Guest-UxDeepExercise.ps1` launched by the agent inside the interactive desktop
- config window discovery by process-bound UI Automation with keyboard-first tab traversal and Validate
- config validation now trusts recent local quote/profile evidence before falling back to YFinance.NET network lookups, so harness Validate runs avoid re-triggering full-symbol 429 storms
- desktop fullscreen entry triggered through the `ViewFullScreenMenuItem` automation hook
- fullscreen validation by comparing live window bounds against the virtual screen
- result bundle pullback from `build/vm/artifacts/ssh-runs/ux-deep-ssh-20260511-154444`

This harness is considered **locked in** for current development work.
Do not spend time re-optimizing or re-architecting the working harness glue unless:

- it is broken, or
- a new product requirement cannot be met with the current flow

## Autonomous Validation

For unattended visual and logic validation, use:

```powershell
.\build\validation\Invoke-AutonomousVisualValidation.ps1 -VmHost 192.168.56.102 -VmCycles 2 -RequiredConsecutiveCleanRuns 2 -GuestScreensaverDurationMinutes 30 -CaptureIntervalSeconds 10 -CreateChangeRequests -CommitBeforeValidation -PushBeforeValidation -AcknowledgeExternalReviewSecretScan
```

This wrapper runs the mandatory DeepSeek review gate, local Release restore/build/tests, optionally commits and pushes declared pending changes before VM validation, executes the SSH-first UX harness, scans pulled screenshots and trace files, and appends project-native CRs for detected anomalies. The default VM path uses 30-minute runs and the guest harness' 120-second background interval so background rotation can be observed without a multi-hour soak.

Test-artifact analysis also has a mandatory DeepSeek second-opinion step. Obtaining the report is mandatory, while the report content is advisory: the analyzer remains responsible for final pass/fail and CR creation. DeepSeek reviews bounded trace/log/screenshot metadata and writes an ignored advisory report beside each analysis JSON. Traces/logs must remain credential-free before this step runs.

## License
This project is licensed under the **MIT LICENSE**.

- Bundled full text: [LICENSE](LICENSE)
- Official text: [https://opensource.org/license/mit/](https://opensource.org/license/mit/)


