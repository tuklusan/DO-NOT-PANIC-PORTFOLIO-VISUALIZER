# VM Operations Runbook (Windows10Pro)

This runbook captures the current Beta 5.5 desktop-first VM validation flow.

## Current posture

- Primary product host: `PortfolioSaver.Desktop`
- Shared scene/runtime library: `PortfolioSaver.Presentation`
- Shared settings library: `PortfolioSaver.Settings`
- Legacy compatibility host: `PortfolioSaver.Screensaver`
- Current pinned SDK in repo: `.NET 10.0.201`
- Preferred VM transport/orchestration path: `SSH + SFTP`
- Legacy fallback path: VirtualBox shared-folder scripts only if SSH is unavailable

## Verified host prerequisites

- Git available
- `.NET SDK 10` available on host
- OpenSSH client available on host
- `Posh-SSH` can be installed at current-user scope for password-based guest access

## Verified guest tooling

- `sshd` running and enabled
- machine-wide `.NET 10 SDK 10.0.203`
- machine-wide `PowerShell 7.6.1`
- `git`, `python`, `jq`, `rg`, `7z`, `PsExec`, `WinAppDriver`
- canonical remote workspace root: `C:\vmharness\portfolio-saver`

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

## Canonical SSH-first VM flow

### 1. Bootstrap the guest workspace and tools

```powershell
./build/vm/Push-VmWorkspace.ps1 -Bootstrap
```

That will:
- connect to the guest over SSH
- ensure `C:\vmharness\portfolio-saver` exists
- ensure machine-wide `pwsh` and `.NET 10` exist in the guest
- upload a clean repository snapshot

### 2. Run remote restore/build/test/publish

```powershell
./build/vm/Invoke-VmBuildTest.ps1 -PushWorkspace
```

That will:
- push a fresh repo snapshot
- run remote `dotnet restore`, `dotnet build`, `dotnet test`
- run remote `build/publish-safe-temp.ps1`
- stage publish output under `C:\vmharness\portfolio-saver\publish`

### 3. Pull the latest remote result bundle

```powershell
./build/vm/Pull-VmResults.ps1
```

Default host download location:
- `build/vm/artifacts/ssh-runs`

## Canonical Beta 5.5 validation flow

### Desktop-first validation

Run through the SSH harness:

```powershell
./build/vm/Invoke-VmBuildTest.ps1 -PushWorkspace -RunUxDeep -GuestScreensaverDurationMinutes 20 -CaptureIntervalSeconds 5
```

Despite the historical parameter name, this is now the deep **desktop** UX cycle.

Expected summary signals:
- `ConfigPhaseStatus=Completed`
- `DesktopPhaseStatus=Completed`
- `ConfigVersionCheck=Passed`
- `DesktopVersionCheck=Passed`
- `FullScreenToggleStatus=Completed`
- `ScreensaverPhaseStatus=LegacyNotRun`

### Direct guest script entry points

Inside the guest, the reusable scripts now support an explicit workspace root:

```powershell
& 'C:\vmharness\portfolio-saver\repo\build\vm\Run-VmUxValidation.ps1' -RootPath 'C:\vmharness\portfolio-saver'
& 'C:\vmharness\portfolio-saver\repo\build\vm\Guest-UxDeepExercise.ps1' -RootPath 'C:\vmharness\portfolio-saver' -ScreensaverDurationMinutes 20 -CaptureIntervalSeconds 5
```

## Result locations

- Guest workspace root:
  - `C:\vmharness\portfolio-saver\results`
- Host pull root:
  - `build/vm/artifacts/ssh-runs`
- Legacy shared-folder export roots still understood by the guest scripts when available:
  - `build/vm/artifacts/vm-results`
  - `build/vm/artifacts/trace`

## Guardrails

1. Prefer SSH-first orchestration.
- Do not use `VBoxManage` or shared folders for normal build/test/result transport.

2. Keep hard timeouts on host commands.
- Long publish and VM commands should remain bounded.

3. Treat desktop app as the truth surface.
- The legacy screensaver host is compatibility-only during Beta 5.5.

4. Validate actual capture behavior, not assumed behavior.
- Windowed screenshots, fullscreen screenshots, and post-ESC screenshots are all part of the Beta 5.5 contract.
- Verify actual capture dimensions before claiming multi-resolution pass behavior.
- Keep `setvideomodehint` as a best-effort guest request, not proof by itself.

5. Keep traces with the result bundle.
- `Guest-UxDeepExercise.ps1` now copies `trace.circular.log` and `trace.circular.idx` into the local result folder so SSH pull can retrieve a self-contained artifact.
