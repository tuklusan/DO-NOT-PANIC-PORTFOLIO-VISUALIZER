# Status

## BETA-5.4 active release lane (April 25, 2026)

- Current runtime label advanced to `BETA-5.4` (`0.9.0-beta5.4`).
- Active audit/checklist file is now `docs/BETA54_FEATURE_AUDIT_TEST_CHECKLIST.md`.
- Development continues from the final `BETA-5.3.2` baseline below.

## BETA-5.3.2 final baseline (April 25, 2026)

- Git baseline tag created at `BETA-5.3.2-final`.
- Final `BETA-5.3.2` revision includes:
  - UTC-pinned top-right clock
  - in-slot tape waiting glyphs during warmup
  - improved Global Markets tape seam behavior
  - expanded bundled exchange/city backgrounds

## BETA-5 baseline (April 10, 2026)

- Git baseline tag created at `BETA-5`.
- Runtime label advanced to `BETA-5` (`PortfolioVersion`, config window title, about text).
- BETA-3 feature-audit checklist reconciled to current code status in both root and `docs/` copies.
- Repository remains on the sanitized single-branch history (`main`) with placeholder-only API key templates.

## BETA-4.1 baseline (April 10, 2026)

- Git baseline tag created at `BETA-4.1`.
- Brand identity updated to `DO NOT PANIC PORTFOLIO VISUALIZER`.
- Publisher set to `SANYALnet Labs`; author set to `Supratim Sanyal`.
- Installer and config app now include MIT license UX with official-link access.

## BETA-3 baseline (April 10, 2026)

- Filesystem baseline snapshot created at `build/baselines/BETA-3/snapshot`.
- Source archive: `build/baselines/BETA-3/PortfolioScreensaver-BETA-3-20260410-095304.zip`.
- Baseline installer copy: `build/baselines/BETA-3/PortfolioSaverScreensaverSetup-BETA-3-20260410-095304.exe`.
- Metadata manifest: `build/baselines/BETA-3/BETA-3-metadata-20260410-095304.json`.

## BETA-3 candidate baseline (April 9, 2026)

- Filesystem baseline snapshot created at `build/baselines/BETA-3-CANDIDATE`.
- Source archive: `PortfolioScreensaver-BETA-3-CANDIDATE-20260409-230617.zip`.
- Baseline installer copy: `PortfolioSaverScreensaverSetup-BETA-3-CANDIDATE-20260409-230617.exe`.
- Metadata manifest: `BETA-3-CANDIDATE-metadata-20260409-230617.json`.

## Beta-2 baseline (April 9, 2026)

- Filesystem baseline snapshot created at `build/baselines/BETA-2`.
- Source archive: `PortfolioScreensaver-BETA-2-20260409-212956.zip`.
- Baseline installer copy: `PortfolioSaverScreensaverSetup-BETA-2-20260409-212956.exe`.
- Metadata manifest: `BETA-2-metadata-20260409-212956.json`.

## Handoff posture

This repository is intentionally oriented toward **Visual Studio 2022 + .NET 8 + WPF on Windows x64**.

It is suitable for Codex handoff and continuation, but it is **not yet build-certified**.
Treat it as a scaffold with known gaps, not as a finished product.

## What is already in place

- solution and project structure for Visual Studio
- WPF screensaver executable project
- WPF config executable project
- core models and shared enums
- seeded startup settings for Tape 1 through Tape 4
- API keys are user-supplied and should not be committed to source control
- conservative provider throttle settings
- floating graph and floating clock scaffolding
- LocalAppData-rooted historical cache design (`%LocalAppData%\\PortfolioSaver\\Caches\\History`)
- build and deployment notes
- Codex continuation notes

## Known gaps Codex must finish

### Runtime/data
- real Finnhub quote implementation must be verified end to end
- Twelve Data quote provider is still scaffold-level
- historical two-week price retrieval is still scaffold-level
- failover policy exists in notes/settings but is not fully battle-tested

### Screensaver visuals
- floating graph cards are scaffolded but not fully wired into the host runtime
- floating clock card is scaffolded but not fully wired into the host runtime
- bounce motion controller exists as design direction and partial code path, not as proven runtime behavior
- green/red segmented graph rendering still needs full implementation polish

### Preview/config
- `/p` preview mode is still placeholder-level and should be upgraded to render the real scene
- config UI is basic and should not be mistaken for feature-complete CRUD

### Build quality
- compile and runtime validation must happen on a Windows machine with Visual Studio
- packaging scripts exist but still need live verification

## Important mismatch fixed in this handoff revision

- `sample-settings.initial-tapes.json` now matches the C# settings model more closely:
  - `groups[].tickers` is now an array of ticker objects
  - `benchmarks` is now an array of benchmark objects

## Order Codex should work in

1. Build solution in Visual Studio Debug x64.
2. Fix compile issues.
3. Make Finnhub quote flow real and observable.
4. Make sample settings load cleanly.
5. Wire floating overlays into `FullScreenHostWindow`.
6. Add historical fetch + LocalAppData cache + purge flow.
7. Upgrade `/p` preview mode.
8. Publish Release x64 and test `.scr` packaging.

## Files Codex should read first

1. `VISUAL_STUDIO_HANDOFF.md`
2. `CODEX_PROMPT.md`
3. `BUILD_AND_DEPLOY.md`
4. `PROVIDER_THROTTLE_NOTES.md`
5. `FLOATING_OVERLAY_NOTES.md`
6. `HISTORY_CACHE_NOTES.md`
