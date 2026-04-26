# Visual Studio Build, Test, and Run Guide

This handoff is aimed at **Visual Studio 2022** on **Windows 10/11 x64**.

## Required environment

- Visual Studio 2022, current supported release
- **Desktop development with .NET** workload
- .NET 8 SDK
- Windows machine with WPF support

## Recommended Visual Studio setup

1. Open Visual Studio Installer.
2. Make sure **Desktop development with .NET** is installed.
3. Make sure the installed Visual Studio 2022 build is new enough for .NET 8.
4. Open `PortfolioScreensaver.sln`.
5. Let restore finish if Visual Studio prompts.
6. Set the solution platform to **x64**.

## Startup projects for normal development

Use **Multiple startup projects**:

- `PortfolioSaver.Config` → Start
- `PortfolioSaver.Screensaver` → Start

For day-to-day work, `PortfolioSaver.Config` is the safer primary startup project.
For screensaver behavior work, switch to `PortfolioSaver.Screensaver`.

## First build sequence in Visual Studio

1. `Build` → `Clean Solution`
2. `Build` → `Rebuild Solution`
3. Fix compile issues in this order:
   - `PortfolioSaver.Core`
   - `PortfolioSaver.Shared`
   - `PortfolioSaver.Data`
   - `PortfolioSaver.Media`
   - `PortfolioSaver.Render`
   - `PortfolioSaver.Config`
   - `PortfolioSaver.Screensaver`

## Debug profiles to use in Visual Studio

### Config app
- Startup project: `PortfolioSaver.Config`
- Command line arguments: none

### Screensaver full-screen
- Startup project: `PortfolioSaver.Screensaver`
- Command line arguments: `/s`

### Screensaver config redirect
- Startup project: `PortfolioSaver.Screensaver`
- Command line arguments: `/c`

### Screensaver preview mode
- Startup project: `PortfolioSaver.Screensaver`
- Command line arguments: `/p 12345`

Preview mode will not be meaningful without a real host window handle, so treat `/p 12345` as a parser smoke test until Codex wires true preview hosting.

## Expected runtime paths

- Settings JSON: `%AppData%\PortfolioSaver\settings.json`
- Quote cache: `%LocalAppData%\PortfolioSaver\quotes-cache.json`
- History cache: `%LocalAppData%\PortfolioSaver\Caches\History`
- Logs: `%LocalAppData%\PortfolioSaver\logs\`

## Required Codex work before first serious run

1. Wire real Finnhub quote calls end to end.
2. Finish Twelve Data quote provider or disable it honestly until complete.
3. Implement historical two-week data retrieval.
4. Wire floating graph cards into `FullScreenHostWindow`.
5. Wire floating clock card into `FullScreenHostWindow`.
6. Implement bounce motion timer and bounds handling.
7. Implement stale-cache and error-state UI.
8. Make `/p` preview render the real scene instead of placeholder text.

## Manual test checklist in Visual Studio

### Build smoke test
- Solution builds in Debug x64.
- Solution builds in Release x64.

### Config app
- Main window opens.
- Settings load without crash.
- Settings save without crash.
- Background folder can be changed.
- Sample ticker groups appear.

### Screensaver shell
- `/s` opens full-screen.
- Keyboard exits cleanly.
- Mouse movement exits cleanly without instant false positives.
- Status bar renders.
- Tape area renders.
- Benchmark strip renders.

### Data
- Live quotes populate from Finnhub.
- If Finnhub fails, fallback policy is visible and logged.
- Twelve Data does not exceed conservative throttles.
- History cache is created under LocalAppData.
- Cache purge removes files older than 14 days.

### Floating overlays
- Graph cards appear.
- Graph cards bounce slowly.
- Clock card appears.
- Clock shows local time and New York time.
- Graph segments color green on rising moves and red on falling moves.

## Packaging after Codex completes the missing pieces

1. Publish `PortfolioSaver.Screensaver` in **Release | x64**.
2. Output target should be `win-x64`.
3. Rename the published `.exe` to `.scr`.
4. Keep the config app as a normal `.exe`.
5. Test the `.scr` manually before dropping it into the Windows screensaver folder.

## Notes for Codex

- Prefer keeping the solution Visual Studio friendly over clever build-system experiments.
- Keep project names and paths stable.
- Avoid introducing Linux-only tooling into the main development path.
- If adding packages, keep them minimal and WPF-friendly.
- Document any new environment assumptions in this file and in `STATUS.md`.
