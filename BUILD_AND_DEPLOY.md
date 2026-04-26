# Build and Deploy

This is the single developer-facing build, run, publish, and first-pass validation guide for the project.

## Target workflow

This repository is meant to be built primarily in **Visual Studio 2022** on **Windows x64**.

## Visual Studio prerequisites

- Visual Studio 2022 current supported release
- Desktop development with .NET workload
- .NET 8 SDK
- `x64` solution platform selected

## First session checklist

1. Open `PortfolioScreensaver.sln` in Visual Studio 2022.
2. Set solution configuration to `Debug`.
3. Set solution platform to `x64`.
4. Run `Clean Solution`.
5. Run `Rebuild Solution`.
6. Start `PortfolioSaver.Config`.
7. Start `PortfolioSaver.Screensaver` with `/s`.
8. If validating screensaver routing, also test `/c` and `/p 12345`.

## Suggested startup configuration

For implementation work:
- startup project: `PortfolioSaver.Config`

For screensaver behavior:
- startup project: `PortfolioSaver.Screensaver`
- command line args: `/s`

For parser/config routing checks:
- command line args: `/c`
- command line args: `/p 12345`

## Manual validation checklist

### Basic compile
- Core, Shared, Data, Media, Render compile.
- Config app starts.
- Screensaver app starts.

### Config app
- Settings window opens.
- Existing sample settings can be loaded or copied into runtime settings.
- Ticker groups survive save/reload.
- Validate flow works online and blocks bad symbols.

### Screensaver full screen
- `/s` opens full screen.
- Mouse/keyboard exit works.
- Top market/status band renders.
- Ticker tapes render.
- Global Markets tape renders.
- Background image fallback works even if image folder is empty.

### Data and throttles
- Quotes load from the current provider ladder.
- Conservative spacing and hour/day provider caps are respected.
- History fetch does not run on every live quote refresh.
- `%LocalAppData%\PortfolioSaver\Caches\History` exists and purges files older than 14 days.

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
3. Publish `PortfolioSaver.Screensaver` for `win-x64`.
4. Publish `PortfolioSaver.Config` as a normal `.exe`.
5. Test both locally before deployment.

## Deploy

### Safer developer path
- Keep the `.scr` and config app in a normal folder first.
- Launch the `.scr` manually with `/s` for testing.

### Final Windows integration
- Copy the `.scr` to the desired Windows screensaver location only after testing.
- Select it in Windows Screen Saver Settings.
- Verify config routing from Screen Saver Settings launches the config app path correctly.

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
