# VM Operations Runbook (Windows10Pro)

This runbook captures the current Beta 5.5 desktop-first VM validation flow.

## Current posture

- Primary product host: `PortfolioSaver.Desktop`
- Shared scene/runtime library: `PortfolioSaver.Presentation`
- Shared settings library: `PortfolioSaver.Settings`
- Legacy compatibility host: `PortfolioSaver.Screensaver`
- Current pinned SDK in repo: `.NET 10.0.201`

## Verified host prerequisites

- VirtualBox CLI available
- Git available
- .NET SDK 10 available on host
- Shared-folder based guest workflow remains the preferred transport path

## Verified guest tooling note

- The guest has `.NET 10 SDK 10.0.203` installed in the user profile.
- Proof artifacts:
  - `build/vm/artifacts/host-runs/dotnet10-vm-install-proof/dotnet10-install-result.json`
  - `build/vm/artifacts/host-runs/dotnet10-vm-install-proof/guest-install-log.txt`
- Caveat:
  - a fresh default guest shell still resolves bare `dotnet` to the older machine-wide `8.0.420`
  - use the explicit user-profile SDK path if guest-side CLI work is needed before machine-wide path precedence is changed

## Canonical host build/publish flow

1. Build in temp workspace:
- `./build/build-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300`

2. Publish in temp workspace:
- `./build/publish-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300`

3. Optional installer build:
- `./build/publish-standalone-installer.ps1`

Safe-temp publish now stages three runnable payloads:
- `build/artifacts/publish-safe-temp/desktop`
- `build/artifacts/publish-safe-temp/config`
- `build/artifacts/publish-safe-temp/screensaver`

## Canonical guest staging flow

Run from guest PowerShell:

```powershell
& "\\VBOXSVR\codexrepo\build\vm\Guest-PrepareVmUxFromShare.ps1"
```

Expected staged layout under `%USERPROFILE%\Desktop\PortfolioVmUx\publish`:
- `config\PortfolioSaver.Config.exe`
- `desktop\PortfolioSaver.Desktop.exe`
- `screensaver\PortfolioSaver.Screensaver.exe`
- each with `release-manifest.json`

## Canonical Beta 5.5 validation flow

### Desktop-first validation

Run from guest PowerShell:

```powershell
& "$env:USERPROFILE\Desktop\PortfolioVmUx\Run-VmUxValidation.ps1"
```

Expected desktop checks:
- Config app launches
- Desktop app launches windowed
- Fullscreen toggle works (`F11`)
- `Esc` returns to windowed mode
- Screenshots land in results folder

### Deep UX desktop cycle

Run from guest PowerShell:

```powershell
& "$env:USERPROFILE\Desktop\PortfolioVmUx\Guest-UxDeepExercise.ps1" -ScreensaverDurationMinutes 20 -CaptureIntervalSeconds 5
```

Despite the historical parameter name, this is now the deep **desktop** UX cycle.

Expected summary signals:
- `ConfigPhaseStatus=Completed`
- `DesktopPhaseStatus=Completed`
- `ConfigVersionCheck=Passed`
- `DesktopVersionCheck=Passed`
- `FullScreenToggleStatus=Completed`
- `ScreensaverPhaseStatus=LegacyNotRun`

## Result locations

- Guest root:
  - `%USERPROFILE%\Desktop\PortfolioVmUx\results`
- Host export roots:
  - `build/vm/artifacts/vm-results`
  - `build/vm/artifacts/trace`

## Guardrails

1. Keep the shared folder workflow.
- It is still the most reliable path for staging and result export.

2. Keep hard timeouts on host commands.
- Long publish and VM commands should remain bounded.

3. Treat desktop app as the truth surface.
- The legacy screensaver host is compatibility-only during Beta 5.5.

4. Validate actual capture behavior, not assumed behavior.
- Windowed screenshots, fullscreen screenshots, and post-ESC screenshots are all part of the Beta 5.5 contract.

## Known follow-up

If guest-side CLI work becomes common, do a machine-wide .NET 10 install in the VM or update PATH precedence so bare `dotnet` resolves to 10.x by default.
