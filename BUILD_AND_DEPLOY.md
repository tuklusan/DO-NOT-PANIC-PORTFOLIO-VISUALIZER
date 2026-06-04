# Build and Deploy

This is the single developer-facing build, run, publish, and first-pass validation guide for the project.

## Target workflow

This repository is meant to be built primarily in **Visual Studio 2022** on **Windows x64**.

## Visual Studio prerequisites

- Visual Studio 2022 current supported release
- Desktop development with .NET workload
- .NET 10 SDK
- `x64` solution platform selected

## First session checklist

1. Open `PortfolioScreensaver.sln` in Visual Studio 2022.
2. Set solution configuration to `Debug`.
3. Set solution platform to `x64`.
4. Run `Clean Solution`.
5. Run `Rebuild Solution`.
6. Start `PortfolioSaver.Desktop`.
7. Start `PortfolioSaver.Config`.
8. If validating legacy screensaver compatibility, also test `/s`, `/c`, and `/p 12345`.

## Suggested startup configuration

For implementation work:
- startup project: `PortfolioSaver.Desktop`

For settings work:
- startup project: `PortfolioSaver.Config`

For legacy screensaver behavior:
- startup project: `PortfolioSaver.Screensaver`
- command line args: `/s`

For legacy parser/config routing checks:
- command line args: `/c`
- command line args: `/p 12345`

## Manual validation checklist

### Basic compile
- Core, Shared, Data, Media, Render compile.
- Config app starts.
- Desktop app starts.
- Legacy screensaver host starts.

### Config app
- Settings window opens.
- In desktop-first validation, open Settings from the desktop `Options` menu and confirm `Validate` closes the window naturally after a successful run.
- During validation, confirm the `Validate` button is disabled, the transient validation-progress window logs ticker/provider results, and the progress window closes when validation ends.
- Existing sample settings can be loaded or copied into runtime settings.
- Ticker groups survive save/reload.
- Validate flow works online and blocks bad symbols.
- Summarized Financial News mode accepts a DeepSeek API key from the config screen and survives save/reload through protected local storage.

### Desktop full screen
- desktop app opens windowed.
- `View -> Full Screen` enters fullscreen.
- `Esc` returns to windowed mode.
- Top market/status band renders.
- Ticker tapes render.
- Global Markets tape renders.
- Background image fallback works even if image folder is empty.

### Data and throttles
- Quotes load from the current YFinance.NET-backed Yahoo runtime lane.
- One-by-one YFinance.NET work respects the 1-second pacing floor, and live caches/metadata should not stay older than 10 minutes.
- History fetch does not run on every live quote refresh.
- `%LocalAppData%\PortfolioSaver\settings.json` exists for the installed app settings root.
- `%LocalAppData%\PortfolioSaver\provider-secrets.json` exists only when protected provider secrets have been saved.
- `%LocalAppData%\PortfolioSaver\Caches\History` exists and purges files older than 14 days.
- Summarized news uses DeepSeek no more often than every 15 minutes and falls back cleanly when the key is unavailable.

### Floating overlays
- Graph cards render.
- Graph cards roam behind foreground content.
- Macro indicators render.
- Top-right UTC clock renders.
- Graph segments show green on up moves and red on down moves.

### Preview mode
- `/p` renders the real scene, not a placeholder stub.

## Publish

Preferred path:

1. Switch to `Release | x64`.
2. Use the scripts under `build\`:
   - `build\build-safe-temp.ps1`
   - `build\publish-safe-temp.ps1`
   - `build\publish-standalone-installer.ps1`
3. Publish `PortfolioSaver.Desktop` for `win-x64`.
4. Publish `PortfolioSaver.Config` as a normal `.exe`.
5. Publish `PortfolioSaver.Screensaver` only if legacy compatibility is still needed.
6. Test desktop + config locally before deployment.

## Remote Windows validation secrets

For live remote validation against the SSH-first Windows target, place optional local-only secrets in:

- `build\vm\test-secrets.json`

Supported fields:

- `DeepSeekApiKey`

Market-data validation no longer needs separate provider secrets because the runtime is now YFinance.NET-only.
The remote harness uploads this ignored file when present, applies the values to the remote user environment, and clears stale remote test-secret overlays when the local file is absent.

## Standalone YFinance.NET proof path

Use the standalone exerciser before deeper runtime changes:

1. `build\vm\Push-VmWorkspace.ps1`
2. Remote build: `dotnet build .\YFinance.net\YFinance.NET.slnx -c Release --nologo`
3. Remote exerciser: `dotnet run --project .\YFinance.net\YFinance.NET.Exerciser\YFinance.NET.Exerciser.csproj -c Release -- --top-count 100 --cycles 25 --refresh-seconds 300 --history-warmup-count 20 --history-lookback-days 5 --cache-ttl-minutes 10 --per-ticker-delay-ms 1000`

Acceptance for the current YFinance.NET lane:
- top 100 S&P 500 symbols resolve from the cached market-cap list
- history warmup succeeds for the warmed subset
- 25 five-minute quote cycles complete on the VM
- no `429`, `RateLimit`, `FAIL`, or `missing` entries appear in the pulled proof log

## Deploy

### Safer developer path
- Keep the desktop app and config app in a normal folder first.
- Launch `PortfolioSaver.Desktop.exe` directly for primary testing.

### Final Windows integration
- Treat the desktop app as the primary shipped experience.
- Only copy the `.scr` to the Windows screensaver location if legacy support is explicitly required.
- If legacy support is installed, verify Screen Saver Settings still lists `PortfolioSaver.Screensaver`.

## Remote validation policy

The current remote Windows UX harness is the supported baseline and should be treated as frozen working infrastructure:

1. DeepSeek review gate: for any code modification, run mandatory DeepSeek code review with `build\Run-DeepSeekCodeReview.ps1 -IncludeUntracked -SendForReview -AcknowledgeSecretScan`, resolve actionable findings, and rerun the review if fixes were made
2. Commit/push gate: commit and push only the code that passed the DeepSeek review gate before starting local or VM validation
3. Local validation gate: run local tests
4. Publish gate: publish with `build\publish-safe-temp.ps1`
5. VM push gate: push with `build\vm\Push-VmWorkspace.ps1`
6. Remote UX gate: run remote interactive UX validation through `PortfolioSaver.VmAgent`

The DeepSeek review gate applies to application code, XAML, scripts, harnesses, tests, project files, build tooling, and packaging changes. It is intentionally before commit/push and before local/VM validation so reviewer findings are resolved before spending long test cycles. Documentation-only ticket updates are exempt unless they change developer workflow or validation policy.

Two current canonical harness behaviors are easy to miss but are now intentional:

1. the harness auto-purges stale VM build/test artifacts whenever free space under `C:\vmharness\portfolio-saver` falls below `8 GB`
2. the configured startup launcher for `PortfolioSaver.VmAgent` exits quietly if the staged agent executable is not present yet, so bootstrap does not throw noisy Windows popup errors

Current known-good clean proof details:

1. `PortfolioSaver.VmAgent` must be started in session `1`
2. the config app is discovered through process-bound UI Automation, not by title-only desktop scans
3. fullscreen is entered through the `ViewFullScreenMenuItem` automation hook, with `F11` only as fallback
4. fullscreen is considered valid only when the live desktop window bounds match the virtual screen
5. a fully green 20-minute proof was recorded in `build\vm\artifacts\ssh-runs\ux-deep-ssh-20260511-154444`

Do not try to optimize the current harness glue just because another path looks cleaner on paper.
Only change the harness when:

- the current path is broken, or
- a concrete new validation requirement cannot be satisfied by the existing agent-based flow

## Installer sandbox smoke test

1. Double-click `build\sandbox\PortfolioSaverInstallerTest.wsb`.
2. In Windows Sandbox, open `C:\Users\WDAGUtilityAccount\Desktop\PortfolioSaverWorkspace\build\artifacts`.
3. Run `PortfolioSaverScreensaverSetup.exe`.
4. Accept the UAC prompt and let the installer finish.
5. Open Screen Saver Settings and verify `PortfolioSaver.Screensaver` appears in the list.
6. In PowerShell inside the sandbox, run:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\WDAGUtilityAccount\Desktop\PortfolioSaverWorkspace\build\sandbox\Validate-PortfolioSaverState.ps1 -ExpectedState Installed
```

7. Uninstall from Programs and Features, or run:

```powershell
powershell -ExecutionPolicy Bypass -File "C:\ProgramData\PortfolioSaverScreensaver\Uninstall-PortfolioSaverScreensaver.ps1"
```

8. Run the validator again with `-ExpectedState Uninstalled`.

## Important note

This repository should remain **Visual Studio first**.
Do not optimize the project around a different IDE at the cost of breaking clean Visual Studio build/debug flow.


