# DO NOT PANIC PORTFOLIO VISUALIZER

Market-aware Windows screensaver and configuration suite by **SANYALnet Labs**, written by **Supratim Sanyal**.

## Overview

**DO NOT PANIC PORTFOLIO VISUALIZER** is a .NET 8 / WPF project that turns a Windows screensaver into a live market display with ticker tapes, floating graph cards, news, exchange backgrounds, and animated overlays.

This repository is maintained as a **Visual Studio 2022-first** codebase for Windows x64 development and deployment.

## Core Features

- Multi-tape portfolio ticker display with configurable symbols, directions, and speed presets
- Floating graph cards designed as compact sparkline overlays
- Floating clock overlay with both local machine time and New York time
- Direction-colored graph rendering: green for rising segments, red for falling segments
- Slow, bounded bounce-style overlay motion for graph and clock cards
- News headline scroller with configurable RSS source and validation
- Dynamic background image system with managed exchange photos or user custom folders
- Provider-aware quote retrieval with budget/rate-limit policy controls
- Offline-aware UI behavior and network gating for validation workflows

## Configuration App (WPF)

- Rich settings UI for:
  - Data providers and API keys
  - Ticker tapes and graph overlays
  - Refresh intervals (portfolio, off-hours, news, backgrounds)
  - Data source policy budgets (hour/day + single/batch query controls)
- Real-time and apply-time symbol validation flow
- Auto-name support for symbols when provider metadata resolves display names
- Help, About, and License document windows
- New **License** button that opens full MIT text and an official license link

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

- Settings file: `%AppData%\PortfolioSaver\settings.json`
- Quote cache: `%LocalAppData%\PortfolioSaver\quotes-cache.json`
- Historical cache: `%LocalAppData%\PortfolioSaver\Caches\History`
- Cache policy:
  - Per-symbol JSON files
  - Automatic purge of history files older than 14 days

## Screensaver Modes

- `/s` full-screen screensaver mode
- `/c` route into configuration workflow
- `/p <HWND>` preview-host mode (for Windows screensaver preview integration)

## Project Structure

- `src/PortfolioSaver.Screensaver` - screensaver executable host
- `src/PortfolioSaver.Config` - configuration app
- `src/PortfolioSaver.Render` - WPF visual controls and scene rendering
- `src/PortfolioSaver.Data` - provider clients, cache, and scheduling services
- `src/PortfolioSaver.Core` - domain models, constants, validation rules
- `src/PortfolioSaver.Media` - background image and transition services
- `src/PortfolioSaver.Shared` - shared helpers, identity/version, licensing utilities
- `src/PortfolioSaver.Installer` - standalone installer bootstrap app
- `tests/PortfolioSaver.Tests` - automated unit tests

## Build and Run (Visual Studio 2022)

## Prerequisites

- Windows 10/11 x64
- Visual Studio 2022
- Desktop development with .NET workload
- .NET 8 SDK

## Recommended workflow

1. Open `PortfolioScreensaver.sln` in Visual Studio 2022.
2. Select `Debug | x64` for development.
3. Rebuild solution.
4. Use startup project:
   - `PortfolioSaver.Config` for settings work
   - `PortfolioSaver.Screensaver` for runtime/visual behavior
5. For screensaver argument testing, set command args to `/s`, `/c`, or `/p 12345`.

## Installer Build Path

Use the scripts under `build/` for publish and packaging workflows, especially:

- `build/publish.ps1`
- `build/publish-standalone-installer.ps1`
- `build/make-screensaver.ps1`

## Current Baseline

- Final `BETA-5.3.2` baseline is tagged in Git as `BETA-5.3.2-final`
- Current development/version lane is `BETA-5.4`
- Product identity:
  - Application: **DO NOT PANIC PORTFOLIO VISUALIZER**
  - Publisher: **SANYALnet Labs**
  - Author: **Supratim Sanyal**

## Documentation

- `VISUAL_STUDIO_HANDOFF.md`
- `BUILD_AND_DEPLOY.md`
- `STATUS.md`
- `ROADMAP.md`
- `CODEX_HANDOFF_CHECKLIST.md`

## License

This project is licensed under the **MIT License**.

- Official text: [https://opensource.org/license/mit/](https://opensource.org/license/mit/)
