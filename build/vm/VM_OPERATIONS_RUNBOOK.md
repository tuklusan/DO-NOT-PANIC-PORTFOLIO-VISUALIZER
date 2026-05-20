# Remote Windows Validation Runbook

This runbook documents the supported Beta 5.6 remote-validation architecture:

- host transport: `SSH + SFTP`
- remote workspace: `C:\vmharness\portfolio-saver`
- interactive desktop automation: `PortfolioSaver.VmAgent` running in the logged-in Windows session
- UI control surface: `WinAppDriver` started and supervised by the agent

This flow is intentionally designed to work for either:

- a Windows VM, or
- a physical Windows PC

It does **not** depend on hypervisor features, shared folders, `VBoxManage`, or guest-control APIs.

## Harness lock

This agent-based SSH/SFTP harness is now the canonical supported workflow.

Use it as-is:

- local test/build validation
- safe-temp publish
- SSH/SFTP workspace push
- `PortfolioSaver.VmAgent` interactive desktop execution
- result pullback over SFTP

Do **not** treat the harness glue itself as an optimization target during normal feature work.
Only revisit the harness when:

- the current flow is broken, or
- a specific new requirement cannot be met with the current mechanism

## Product posture

- Primary product host: `PortfolioSaver.Desktop`
- Shared scene/runtime library: `PortfolioSaver.Presentation`
- Shared settings library: `PortfolioSaver.Settings`
- Legacy compatibility host: `PortfolioSaver.Screensaver`
- Remote desktop-session helper: `PortfolioSaver.VmAgent`
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
- `WinAppDriver.exe` available at:
  - `C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe`
- an actively logged-in interactive Windows desktop session
- canonical workspace root:
  - `C:\vmharness\portfolio-saver`

## Canonical host scripts

- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Push-VmWorkspace.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Push-VmWorkspace.ps1)
- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Invoke-VmBuildTest.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Invoke-VmBuildTest.ps1)
- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Pull-VmResults.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Pull-VmResults.ps1)
- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\VmSshCommon.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\VmSshCommon.ps1)
- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Guest-BootstrapVmRemoteTools.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Guest-BootstrapVmRemoteTools.ps1)
- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Guest-ConfigureDesktopAutomation.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Guest-ConfigureDesktopAutomation.ps1)
- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Guest-ApplyTestSecrets.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Guest-ApplyTestSecrets.ps1)
- [D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Guest-UxDeepExercise.ps1](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Guest-UxDeepExercise.ps1)

## Workspace layout on the remote Windows target

- `C:\vmharness\portfolio-saver\repo`
- `C:\vmharness\portfolio-saver\publish`
- `C:\vmharness\portfolio-saver\results`
- `C:\vmharness\portfolio-saver\commands`
- `C:\vmharness\portfolio-saver\agent`
- `C:\vmharness\portfolio-saver\scripts`

## Supported end-to-end flow

### 1. Bootstrap the target and upload a fresh repo snapshot

```powershell
./build/vm/Push-VmWorkspace.ps1 -Bootstrap
```

This does all of the following over SSH/SFTP:

- ensures the remote workspace root exists
- auto-purges stale VM build/test artifacts when free space under the harness root drops below `8 GB`
- ensures machine-wide `pwsh` and `.NET 10` are present
- uploads a clean repository snapshot
- uploads `build\vm\test-secrets.json` when present and removes any stale remote copy when absent

## Optional live secret overlay

If you want the remote Windows target to use live API credentials during validation, create the ignored local file:

- `build\vm\test-secrets.json`

Supported fields:

- `DeepSeekApiKey`

The VM harness no longer expects separate market-data provider credentials because the runtime is now YFinance.NET-only.
The harness applies them to the remote user environment before the remote build/UX cycle starts. DeepSeek is written to both:

- `DEEPSEEK_API_KEY`
- `PORTFOLIOSAVER_DEEPSEEK_API_KEY`

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

### 3. Configure desktop automation and start the agent

```powershell
./build/vm/Invoke-VmBuildTest.ps1 -PushWorkspace -RunUxDeep -GuestScreensaverDurationMinutes 20 -CaptureIntervalSeconds 5
```

Before the UX cycle starts, the harness now:

- runs `Guest-ConfigureDesktopAutomation.ps1`
- runs `Guest-ApplyTestSecrets.ps1`
- re-checks free space and auto-purges stale harness artifacts again if free space is below `8 GB`
- configures autologon-style Winlogon registry values for the dedicated test account
- disables the screen saver for that user profile
- prepares a startup launcher for `PortfolioSaver.VmAgent`
- starts `PortfolioSaver.VmAgent.exe` directly in session `1`
- waits for a live heartbeat at:
  - `C:\vmharness\portfolio-saver\agent\agent-status.json`

### 4. Queue a UX run to the desktop-session agent

Once the agent is alive, the host writes a command JSON under:

- `C:\vmharness\portfolio-saver\commands\<result>.json`

The agent acknowledges command receipt under:

- `C:\vmharness\portfolio-saver\agent\command-results\<result>.result.json`

The actual deep exercise still runs through:

- `Guest-UxDeepExercise.ps1`

but it is launched **by the agent from inside the interactive desktop session**, not by a one-shot SSH-driven PowerShell injection.

### 5. Poll completion and pull results back

The host polls:

- `C:\vmharness\portfolio-saver\results\<result>\ux-deep-summary.json`

until `FinishedAt` is present, then pulls the entire result bundle back over SFTP.

## How the interactive launch works now

This is the key behavior that finally worked reliably.

1. The host prepares the remote workspace over SSH.
2. The host publishes `PortfolioSaver.VmAgent` into the remote `publish\agent` folder.
3. The host starts the agent in session `1` with `PsExec`.
4. The agent ensures `WinAppDriver` is running inside that same interactive desktop session.
5. The host waits for the agent heartbeat file.
6. The host writes a UX command JSON into the remote command queue.
7. The agent launches the desktop app first, opens the Settings window from the desktop menu path, drives a minimal keyboard-first config smoke path, waits for Validate to succeed and the window to close naturally, then continues the same desktop-session run into the fullscreen/windowed validation flow.
   During Validate, the config app is expected to disable the Validate button, show a small Validation Progress window, and trust recent local quote/profile evidence before falling back to YFinance.NET network lookups.
8. Guest-UxDeepExercise.ps1 captures screenshots, explicitly focuses the desktop window, prefers keyboard navigation over coordinate clicks for config interaction, validates true fullscreen by comparing the live window bounds to the virtual screen, validates ESC return-to-windowed behavior, copies trace files into the result bundle, and writes:
   - `ux-deep-summary.json`
   - `vm-ux-summary.json`
9. The host polls the summary file until `FinishedAt` appears.
10. The host pulls the complete result bundle back over SFTP.

## Current known-good proof markers

Standalone YFinance.NET proof marker:
- `build/vm/artifacts/host-runs/yfinance-vm-stage5-top100-25x300-soak.log` records a full 25-cycle top-100 soak with 20 warmed history lanes, 1-second one-by-one pacing, 10-minute cache ceilings, and no `429`, `RateLimit`, `FAIL`, or `missing` lines.

The first fully re-proven clean agent run after the fullscreen and config-discovery fixes is:

- `build/vm/artifacts/ssh-runs/ux-deep-ssh-20260511-154444`

That run completed with:

- `ConfigPhaseStatus = Completed`
- `DesktopPhaseStatus = Completed`
- `ConfigVersionCheck = Passed`
- `DesktopVersionCheck = Passed`
- `FullScreenToggleStatus = Completed`

The harness details that made this pass reliable are now considered part of the locked workflow:

- config window lookup is process-bound and may use the process main window handle directly
- config smoke interaction is keyboard-first and intentionally limited to deterministic tab traversal plus Validate
- config Validate should close naturally without fallback dialogs; if validation blocks, the harness records the blocking dialog text instead of force-closing blindly
- the desktop shell exposes `DesktopMainWindow` automation metadata with semantic-version help text
- the desktop fullscreen action exposes `ViewFullScreenMenuItem` for direct UI Automation invocation
- `F11` remains only a fallback if the automation invoke path is unavailable

## Important operational notes

### Why we moved away from direct interactive PowerShell launch

Direct `PsExec ... pwsh.exe -File Guest-UxDeepExercise.ps1` was the unreliable boundary.
It matched the black popup symptom:

- `Attempting to perform the InitializeDefaultDrives operation on the 'FileSystem' provider failed.`

The agent-based model avoids that fragile startup path by keeping the orchestration owner inside the desktop session.

### Agent heartbeat and command acknowledgement

The harness must not assume the desktop automation layer is live until:

- `agent-status.json` exists and has a fresh heartbeat
- the queued UX command produces its acknowledgement JSON

If either is missing, treat the launch as failed rather than assuming the app became visible.

### Low-disk auto-purge is part of the canonical harness

The canonical scripts now call `Ensure-VmFreeSpace` before both:

- workspace push
- remote build/test/UX execution

The current minimum supported free-space floor is:

- `8 GB`

If the VM drops below that threshold, the harness is expected to purge obsolete content under the harness root before continuing. This is not an optional cleanup trick; it is now part of the locked workflow because low disk space became a recurring cause of false harness failures.

### Startup launcher behavior when the agent is not yet staged

`Guest-ConfigureDesktopAutomation.ps1` installs a startup launcher for `PortfolioSaver.VmAgent`, but that launcher now intentionally exits without error if:

- `C:\vmharness\portfolio-saver\publish\agent\PortfolioSaver.VmAgent.exe`

does not exist yet.

That behavior is intentional. It prevents noisy Windows popup failures during bootstrap or partial staging runs. Do not treat the absence of an immediate startup-launched agent as a harness defect unless the host has already staged the publish artifacts and the explicit session-1 agent start still fails.

### WinAppDriver ownership

`WinAppDriver` is part of the supported stack, but it is **not** the top-level orchestrator.
`WinAppRunner` is not part of the supported stack on this VM. The installed UI-driver binary is:

- `C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe`

`PortfolioSaver.VmAgent` owns:

- starting `WinAppDriver` if needed
- queuing UX runs
- keeping all GUI-session work inside the interactive desktop

### Summary file behavior

`ux-deep-summary.json` is written early and updated throughout the run.
The harness must wait for:

- `FinishedAt`

before treating the run as complete.

### Process cleanup

Before and after each run, the harness should ensure no stale instances remain for:

- `PortfolioSaver.Config`
- `PortfolioSaver.Desktop`
- `PortfolioSaver.Screensaver`
- `PortfolioSaver.VmAgent`
- `WinAppDriver`

Do **not** kill the remote shell owner during cleanup. In practice that means:

- do not include remote `pwsh` or `powershell` in the standard stale-process kill list
- killing the remote shell host can surface blank SSH failures that look like harness instability even when the actual app cleanup succeeded

## Expected successful summary signals

- `ConfigPhaseStatus = Completed`
- `DesktopPhaseStatus = Completed`
- `ConfigVersionCheck = Passed`
- `DesktopVersionCheck = Passed`
- `FullScreenToggleStatus = Completed`
- `ScreensaverPhaseStatus = LegacyNotRun`
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

This is a debugging convenience only. The canonical harness path is still the host-driven SSH flow mediated by `PortfolioSaver.VmAgent`.

## Guardrails

1. Use SSH/SFTP as the only supported orchestration and transport path.
2. Do not reintroduce `VBoxManage`, shared folders, guest-control APIs, or one-shot interactive PowerShell launchers as the primary UX path.
3. Treat the remote Windows target as a generic machine, not a hypervisor-dependent guest.
4. Keep traces inside the pulled result bundle so each run is self-contained.
5. Validate actual capture behavior:
   - windowed screenshots
   - fullscreen screenshots
   - post-ESC screenshots
   - verify actual capture dimensions before claiming multi-resolution pass behavior
6. Keep all long-running host commands bounded by explicit timeouts.
7. The current harness allows up to `10080` minutes per desktop phase, so multi-day soak runs remain within the supported validation range.



