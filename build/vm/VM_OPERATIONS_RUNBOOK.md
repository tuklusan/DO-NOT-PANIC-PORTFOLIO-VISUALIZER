# Remote Windows Validation Runbook

This runbook documents the only supported remote validation method for Beta 5.5:

- host transport: `SSH + SFTP`
- remote workspace: `C:\vmharness\portfolio-saver`
- interactive UI launch: guest-side `PsExec` wrapper into the logged-in desktop session

This flow is intentionally designed to work for either:

- a Windows VM, or
- a physical Windows PC

It does **not** depend on hypervisor features, shared folders, `VBoxManage`, or guest-control APIs.

## Product posture

- Primary product host: `PortfolioSaver.Desktop`
- Shared scene/runtime library: `PortfolioSaver.Presentation`
- Shared settings library: `PortfolioSaver.Settings`
- Legacy compatibility host: `PortfolioSaver.Screensaver`
- Current pinned SDK in repo: `.NET 10.0.201`

## Required host capabilities

- Git
- `.NET SDK 10`
- OpenSSH client
- PowerShell
- ability to install/use `Posh-SSH` at current-user scope

## Required remote Windows target capabilities

- `sshd` running and reachable
- machine-wide `.NET 10 SDK`
- machine-wide `PowerShell 7`
- `PsExec.exe` available at:
  - `C:\Program Files\SysinternalsSuite\PsExec.exe`
- an actively logged-in interactive Windows desktop session
- canonical workspace root:
  - `C:\vmharness\portfolio-saver`

## Canonical host scripts

- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Push-VmWorkspace.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Push-VmWorkspace.ps1)
- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Invoke-VmBuildTest.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Invoke-VmBuildTest.ps1)
- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Pull-VmResults.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Pull-VmResults.ps1)
- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\VmSshCommon.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\VmSshCommon.ps1)
- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Guest-BootstrapVmRemoteTools.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Guest-BootstrapVmRemoteTools.ps1)
- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Guest-UxDeepExercise.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Guest-UxDeepExercise.ps1)

## Workspace layout on the remote Windows target

- `C:\vmharness\portfolio-saver\repo`
- `C:\vmharness\portfolio-saver\publish`
- `C:\vmharness\portfolio-saver\results`
- `C:\vmharness\portfolio-saver\scripts`

## Supported end-to-end flow

### 1. Bootstrap the target and upload a fresh repo snapshot

```powershell
./build/vm/Push-VmWorkspace.ps1 -Bootstrap
```

This does all of the following over SSH/SFTP:

- ensures the remote workspace root exists
- ensures machine-wide `pwsh` and `.NET 10` are present
- uploads a clean repository snapshot

### 2. Run remote restore/build/test/publish

```powershell
./build/vm/Invoke-VmBuildTest.ps1 -PushWorkspace
```

This runs inside the remote workspace:

- `dotnet restore`
- `dotnet build`
- `dotnet test`
- `build/publish-safe-temp.ps1`

It then stages runnable artifacts under:

- `C:\vmharness\portfolio-saver\publish`

### 3. Run the deep interactive desktop cycle

```powershell
./build/vm/Invoke-VmBuildTest.ps1 -PushWorkspace -RunUxDeep -GuestScreensaverDurationMinutes 20 -CaptureIntervalSeconds 5
```

Despite the historical parameter name, this is the canonical **desktop** UX cycle.

## How the interactive launch works

This is the key behavior that finally worked reliably.

1. The host prepares the remote workspace over SSH.
2. The host writes a guest-side launcher file under:
   - `C:\vmharness\portfolio-saver\scripts\launch-<result>.cmd`
3. That launcher runs:
   - `PsExec.exe -i 1 -d ... pwsh.exe -File Guest-UxDeepExercise.ps1`
4. `PsExec` injects the UX script into the logged-in interactive desktop session.
5. `Guest-UxDeepExercise.ps1` launches:
   - `PortfolioSaver.Config.exe`
   - `PortfolioSaver.Desktop.exe`
6. The guest script captures screenshots, validates fullscreen/ESC behavior, copies trace files into the result bundle, and writes:
   - `ux-deep-summary.json`
   - `vm-ux-summary.json`
7. The host polls the summary file until `FinishedAt` appears.
8. The host pulls the complete result bundle back over SFTP.

## Important operational notes

### `PsExec` exit behavior

`PsExec` may return a non-zero exit code that is actually the spawned child PID, while still launching successfully.

The harness intentionally treats the launch as successful when `PsExec` output contains:

- `started on ... with process ID ...`

Do not regress this behavior to a strict zero-exit-only check.

### Summary file behavior

`ux-deep-summary.json` is written early and updated throughout the run.

The harness must wait for:

- `FinishedAt`

to appear before treating the run as complete.

Do not assume the first observed summary payload is final.

### Process cleanup

Before and after each run, the harness should ensure no stale instances remain for:

- `PortfolioSaver.Config`
- `PortfolioSaver.Desktop`
- `PortfolioSaver.Screensaver`

## Expected successful 20-minute summary signals

- `ConfigPhaseStatus = Completed`
- `DesktopPhaseStatus = Completed`
- `ConfigVersionCheck = Passed`
- `DesktopVersionCheck = Passed`
- `FullScreenToggleStatus = Completed`
- `ScreensaverPhaseStatus = LegacyNotRun`
- `PlannedScreensaverDurationMinutes = 20`
- `FinishedAt` present

## Result locations

### On the remote target

- `C:\vmharness\portfolio-saver\results`

### On the host after pullback

- `build/vm/artifacts/ssh-runs`

## Direct guest entry point

If you are already logged into the remote Windows desktop and want to run the deep exercise locally there, use:

```powershell
& 'C:\vmharness\portfolio-saver\repo\build\vm\Guest-UxDeepExercise.ps1' -RootPath 'C:\vmharness\portfolio-saver' -ScreensaverDurationMinutes 20 -CaptureIntervalSeconds 5
```

This is a debugging convenience only. The canonical harness path is still the host-driven SSH flow.

## Guardrails

1. Use SSH/SFTP as the only supported orchestration and transport path.
2. Do not reintroduce `VBoxManage`, shared folders, guest-control APIs, or scheduled-task launchers.
3. Treat the remote Windows target as a generic machine, not a hypervisor-dependent guest.
4. Keep traces inside the pulled result bundle so each run is self-contained.
5. Validate actual capture behavior:
   - windowed screenshots
   - fullscreen screenshots
   - post-ESC screenshots
   - verify actual capture dimensions before claiming multi-resolution pass behavior
   - treat `setvideomodehint` as a best-effort display request, not proof by itself
6. Keep all long-running host commands bounded by explicit timeouts.
