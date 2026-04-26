# Build and Deploy

## Target workflow

This repository is meant to be built primarily in **Visual Studio 2022** on **Windows x64**.

## Visual Studio prerequisites

- Visual Studio 2022 current supported release
- Desktop development with .NET workload
- .NET 8 SDK
- x64 solution platform selected

## Open and build

1. Open `PortfolioScreensaver.sln` in Visual Studio.
2. Set solution configuration to `Debug`.
3. Set solution platform to `x64`.
4. Run `Clean Solution`.
5. Run `Rebuild Solution`.

## Suggested startup configuration

For implementation work:
- startup project: `PortfolioSaver.Config`

For screensaver behavior:
- startup project: `PortfolioSaver.Screensaver`
- command line args: `/s`

For parser/config routing checks:
- command line args: `/c`
- command line args: `/p 12345`

## Manual test checklist

### Basic compile
- Core, Shared, Data, Media, Render compile.
- Config app starts.
- Screensaver app starts.

### Config app
- Settings window opens.
- Existing sample settings can be loaded or copied into runtime settings.
- Group and benchmark data survives save/reload after Codex finishes the persistence path.

### Screensaver fullscreen
- `/s` opens full screen.
- Mouse/keyboard exit works.
- Top status bar renders.
- Ticker tapes render.
- Benchmark strip renders.
- Background image fallback works even if image folder is empty.

### Data and throttles
- Finnhub quotes load.
- Twelve Data fallback is only used when intended.
- Conservative spacing and minute/day safe caps are respected.
- History fetch does not run on every live quote refresh.
- LocalAppData history cache exists and purges files older than 14 days.

### Floating overlays
- Graph cards render.
- Graph cards bounce slowly within bounds.
- Clock card renders.
- Clock shows local and New York time.
- Graph line segments show green on up moves and red on down moves.

### Preview mode
- `/p` is upgraded from placeholder status to real preview rendering.

## Publish

After Codex completes the missing work:

1. Switch to `Release | x64`.
2. Publish `PortfolioSaver.Screensaver`.
3. Target runtime: `win-x64`.
4. Rename the published executable to `.scr`.
5. Publish `PortfolioSaver.Config` as a normal `.exe`.
6. Test both locally before deploying.

## Deploy

### Safer developer path
- Keep the `.scr` and config app in a normal folder first.
- Launch the `.scr` manually with `/s` for testing.

### Final Windows integration
- Copy the `.scr` to the desired Windows screensaver location only after testing.
- Select it in Windows Screen Saver Settings.
- Verify config routing from Screen Saver Settings launches the config app path correctly.

## Important note

This repository should remain **Visual Studio first**.
Do not optimize the project around a different IDE at the cost of breaking clean Visual Studio build/debug flow.
