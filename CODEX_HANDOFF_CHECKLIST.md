# Codex Handoff Checklist

## Environment
- [ ] Windows machine
- [ ] Visual Studio 2022 installed
- [ ] Desktop development with .NET workload installed
- [ ] .NET 8 SDK installed
- [ ] Solution opened in Visual Studio
- [ ] Solution platform set to x64

## First pass
- [ ] Read `VISUAL_STUDIO_HANDOFF.md`
- [ ] Read `STATUS.md`
- [ ] Read `CODEX_PROMPT.md`
- [ ] Build Debug x64
- [ ] Fix compile errors
- [ ] Start `PortfolioSaver.Config`
- [ ] Start `PortfolioSaver.Screensaver` with `/s`

## Data
- [ ] Verify Finnhub key path
- [ ] Verify Twelve Data key path
- [ ] Verify conservative throttles
- [ ] Implement/verify real quote parsing
- [ ] Implement/verify history fetch path

## Visuals
- [ ] Tapes render
- [ ] Benchmarks render
- [ ] Background slideshow works
- [ ] Floating graphs render and move
- [ ] Floating clock renders and moves
- [ ] Graph colors split by rising/falling segments

## Packaging
- [ ] Release x64 build works
- [ ] Screensaver publish works
- [ ] `.scr` rename/package works
- [ ] Config app publish works
