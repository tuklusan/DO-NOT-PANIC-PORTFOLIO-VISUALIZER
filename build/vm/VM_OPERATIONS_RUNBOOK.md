# VM Operations Runbook (Windows10Pro)

This runbook captures repeatable methods that have already worked, so we do not rediscover mechanics each cycle.

## 1) Baseline Prereqs (Verified)

- Host has VirtualBox CLI at:
- `D:\Program Files\Oracle\VirtualBox\VBoxManage.exe`
- Host ripgrep path (use explicit path if `rg` alias fails in PowerShell):
- `C:\Program Files\WinGet\Links\rg.exe`
- Target VM name:
- `Windows10Pro`
- Guest Additions present and operational (`VBoxControl` available in guest).
- Active guest desktop session confirmed with:
- `query user` -> `console` session active.
- WinAppDriver availability confirmed for future UI automation:
- `C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe` (version `1.2.1.0`)

## 2) Proven Workflow: Build + Stage VM Payload

1. Build and publish artifacts on host:
- `.\build\build-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300`
- `.\build\publish-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300`
- `.\build\publish-standalone-installer.ps1` when installer validation is specifically needed
2. Prepare guest payload root:
- `C:\Users\<guest-user>\Desktop\PortfolioVmUx`
3. Copy publish folders with exact required layout:
- `publish\config\...`
- `publish\screensaver\...`
- `Run-VmUxValidation.ps1`
4. Verify guest paths exist before launch:
- `...\publish\config\PortfolioSaver.Config.exe`
- `...\publish\screensaver\PortfolioSaver.Screensaver.exe`

Why this matters:
- `Run-VmUxValidation.ps1` throws immediately if those exact paths are missing.

### 2.1 Canonical Build Rule (MSB3491-safe)

When local WPF builds fail under repo paths (`*_wpftmp.csproj` / `MSB3491 Access denied`), use the temp-workspace build:

- `.\build\build-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300`

This script:
- Mirrors `src/` + `tests/` into `%TEMP%\PortfolioSaverBuildWorkspace`
- Seeds local `project.assets.json` caches
- Builds there (where WPF temp project creation succeeds)
- Copies `bin/` outputs back to the repo

Do not run unbounded restore/build commands directly from the repo during VM cycles.

### 2.1.1 Host SDK Resolver Note

If `dotnet` fails from the repo root with:

- `Requested SDK version: 8.0.420`
- installed SDKs only showing a newer SDK, for example `10.0.201`

do not edit `global.json` just to run tests. The repo intentionally preserves the Visual-Studio/pinned-SDK policy.

For local test-only verification, use the non-invasive resolver workaround:

- create or reuse a temp working directory outside the repo
- run `dotnet test "<absolute path to tests\PortfolioSaver.Tests\PortfolioSaver.Tests.csproj>" ...` from that temp directory

This keeps `global.json` intact while allowing the installed SDK resolver to target the project successfully.

### 2.2 Canonical Publish Rule (stale-build-safe)

Use the temp-workspace publish for VM/UI runs:

- `.\build\publish-safe-temp.ps1 -Configuration Release -TimeoutSeconds 300`

What this does:
- mirrors `src/` into `%TEMP%\PortfolioSaverPublishWorkspace`
- restores only the two runnable projects
- publishes self-contained `win-x64` artifacts into `build\artifacts\publish-safe-temp`
- generates `release-manifest.json` for both apps

Why this is the canonical path now:
- direct repo-path publish has been the most fragile path in this project
- the temp-workspace publish was verified to complete in about 54 seconds on 2026-04-12
- VM staging now recognizes `publish-safe-temp` and prefers it for fresh UX cycles

Observed failure mode to avoid:
- the earlier custom PowerShell subprocess wrapper created false "hung build" behavior
- plain timestamped `dotnet restore/publish` steps from the temp workspace were reliable
- prefer direct commands plus host-level timeout caps over nested in-script process supervision

### 2.3 Timeout discipline (hard rule)

Do not run long build/publish commands as one opaque terminal wait.

Use these rules:
- host tool timeout for any single build/publish command: `<= 180000 ms`
- if a command exceeds 3 minutes, stop it and investigate the specific step
- prefer timestamped scripts that show step boundaries (`prepare`, `restore`, `publish`, `manifest`)
- kill leftover `dotnet`/`msbuild` processes before retrying after an interrupted run

### New reliable staging/export helpers (share-first)

- Add transient shared folder:
- `VBoxManage sharedfolder add Windows10Pro --name codexrepo --hostpath "<repo-root>" --automount --transient`
- In guest PowerShell, run:
- `& "\\VBOXSVR\codexrepo\build\vm\Guest-PrepareVmUxFromShare.ps1"`
- Launch validation from same guest shell:
- `& "$env:USERPROFILE\Desktop\PortfolioVmUx\Run-VmUxValidation.ps1"`
- Export latest guest results back to host artifacts:
- `& "$env:USERPROFILE\Desktop\PortfolioVmUx\Guest-ExportLatestVmUxResult.ps1"`

This avoids fragile `guestcontrol copy*` dependencies when guest credentials are unavailable.

## 3) Proven Workflow: Tool Discovery / Inventory

Successful method:
- Use `VBoxManage guestcontrol ... run` with encoded PowerShell to avoid quoting breakage.
- Generate and store inventory under guest:
- `C:\Users\<guest-user>\Desktop\PortfolioVmUx\tool-inventory`
- Copy inventory back to host:
- `build\vm\tool-inventory\YYYY-MM-DD\`

Current recorded inventory:
- [VM_TOOL_RECORD.md](/D:/Users/vagab/Development/PortfolioScreensaver-Codex-Handoff-VisualStudio/build/vm/tool-inventory/2026-04-10/VM_TOOL_RECORD.md)
- [summary.json](/D:/Users/vagab/Development/PortfolioScreensaver-Codex-Handoff-VisualStudio/build/vm/tool-inventory/2026-04-10/summary.json)
- [program-files-scan-summary.csv](/D:/Users/vagab/Development/PortfolioScreensaver-Codex-Handoff-VisualStudio/build/vm/tool-inventory/2026-04-10/program-files-scan-summary.csv)

## 4) Robust Command Patterns (Use These)

- Prefer encoded guest PowerShell:
```powershell
$enc=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($guestScript))
VBoxManage guestcontrol <VM> run --exe "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" --username <user> --password <pass> --wait-stdout -- -NoProfile -ExecutionPolicy Bypass -EncodedCommand $enc
```

- For file transfer:
- `guestcontrol copyto` for host -> guest
- `guestcontrol copyfrom --recursive --target-directory ...` for guest -> host

- For validation polling:
- Query latest `results\YYYYMMDD-HHMMSS`
- Check for `vm-ux-summary.json`
- Count screenshot files (`*.png`)

## 5) Verified Interactive UI Validation Strategy (Current Best)

Use host-side `controlvm` keyboard injection to launch validator from the real logged-in desktop session:

1. Ensure VM frontend is GUI:
- `VBoxManage showvminfo Windows10Pro --machinereadable` -> `SessionName="GUI/Qt"`
2. Open Run dialog via scancode:
- `VBoxManage controlvm Windows10Pro keyboardputscancode e0 5b 13 93 e0 db` (Win+R)
3. Inject command text:
- `VBoxManage controlvm Windows10Pro keyboardputstring "powershell -NoProfile -ExecutionPolicy Bypass -File %USERPROFILE%\Desktop\PortfolioVmUx\Run-VmUxValidation.ps1"`
4. Submit Enter:
- `VBoxManage controlvm Windows10Pro keyboardputscancode 1c 9c`
5. Wait ~115 seconds, then poll `results\` and check for `vm-ux-summary.json`.

Optional verification:
- Use `VBoxManage controlvm Windows10Pro screenshotpng <path>` before/after launch to confirm desktop/UI state.

This method is now validated end-to-end and produces full result artifacts.

## 6) Latest Successful Validation Records

- Validation export (1366 baseline):
- [vm-results\20260410-182354-1366x768](/D:/Users/vagab/Development/PortfolioScreensaver-Codex-Handoff-VisualStudio/build/vm/artifacts/vm-results/20260410-182354-1366x768)
- Validation export (attempted 1920 run):
- [vm-results\20260410-182659-1920x1080](/D:/Users/vagab/Development/PortfolioScreensaver-Codex-Handoff-VisualStudio/build/vm/artifacts/vm-results/20260410-182659-1920x1080)
- Validation export (attempted 3440 run):
- [vm-results\20260410-183004-3440x1440](/D:/Users/vagab/Development/PortfolioScreensaver-Codex-Handoff-VisualStudio/build/vm/artifacts/vm-results/20260410-183004-3440x1440)
- Installer/uninstall probe PASS:
- [t039-probe-20260410-184303.json](/D:/Users/vagab/Development/PortfolioScreensaver-Codex-Handoff-VisualStudio/build/vm/artifacts/vm-results/t039-probe/t039-probe-20260410-184303.json)

## 7) Known Failure Modes (Do Not Reuse Blindly)

- Running UI capture directly via `guestcontrol run`:
- Fails with `CopyFromScreen ... handle is invalid` (non-interactive desktop context).

- `schtasks /Run` from guestcontrol in this environment:
- Repeated `Element not found` / unstable behavior.

- `/IT` scheduled task time triggers:
- Tasks can remain stuck at `0x00041303` (never actually launched).

- `PsExec -i <session> -d` launched from guestcontrol:
- Failed to install service (`The handle is invalid`) in this context.

- Unencoded inline multi-layer quoting:
- Causes parser failures and command corruption.

- VM resolution hint limitations:
- `setvideomodehint` / `setscreenlayout` attempts can still leave guest framebuffer fixed at `1366x768` in this VM window configuration; verify actual capture dimensions before claiming multi-resolution pass.

- UAC prompts during install/uninstall probe:
- The probe requires interactive UAC confirmations on secure desktop; host-side scancodes (`Left` then `Enter`) worked to approve `Yes` in this environment.

## 8) Deep UX Harness Workflow (Config + 20-Min Screensaver)

Purpose:
- Exercise config inputs/buttons across tabs with screenshot evidence per widget.
- Run screensaver for 20 minutes with one screenshot every 5 seconds.
- Capture host-side monitor checkpoints while the user observes live VM behavior.

Implementation artifact:
- Guest script: `build/vm/Guest-UxDeepExercise.ps1`

### 8.1 Launch Pattern (interactive desktop, no guest credentials)

1. Ensure VM is in GUI frontend:
- `VBoxManage showvminfo Windows10Pro --machinereadable` -> `SessionName="GUI/Qt"`
2. Ensure transient repo share is available:
- `VBoxManage sharedfolder add Windows10Pro --name codexrepo --hostpath "<repo-root>" --automount --transient`
3. Start guest script via Run dialog injection:
- Win+R scancode: `e0 5b 13 93 e0 db`
- command:
- `powershell -NoProfile -ExecutionPolicy Bypass -File "\\VBOXSVR\codexrepo\build\vm\Guest-UxDeepExercise.ps1"`
- Enter scancode: `1c 9c`

### 8.2 What the script does

1. Kills prior `PortfolioSaver.Config` and `PortfolioSaver.Screensaver` processes.
2. Launches config app and discovers the window by partial title.
3. Enumerates tabs and visible controls (`Edit`, `Button`, `CheckBox`, `ComboBox`, `Slider`).
4. Performs non-destructive per-control exercise:
- Edits: focus/value roundtrip
- Checkboxes: toggle on/off
- Combos: expand/collapse
- Sliders: step + restore
- Buttons: focus-only (no invoke) to avoid external browser/modal traps
5. Captures screenshot after each exercised widget.
6. Launches screensaver and captures 240 screenshots at 5-second intervals (~20 minutes).
7. Exits screensaver (`Esc`) and writes `ux-deep-summary.json`.

### 8.3 Evidence paths

- Guest output root:
- `%USERPROFILE%\Desktop\PortfolioVmUx\results\ux-deep-<timestamp>`
- Host monitor checkpoints (manual):
- `build\vm\artifacts\live-ux-20260411\deep-run3-monitor-*.png`

### 8.4 Operational guardrails learned from this run

1. Do not blindly invoke all buttons during automated per-widget pass.
- Invoking `License`/`About` can spawn browser/modals and deadlock traversal.
2. Always include modal cleanup loop.
- Dismiss child windows with `WindowPattern.Close`, then fallback to `OK`, then `Esc`.
3. Keep one dedicated script in repo for repeatability.
- Avoid retyping ad-hoc Win+R command chains each cycle.
4. Treat export as separate step.
- Deep UX runs may require explicit post-run copy from guest results to host artifacts.

## 9) Mandatory Validation Workflow (Current Standard)

Use this workflow for every VM UX cycle:

1. Always log remote script execution.
- Run guest scripts with transcript/log output enabled (`ux-deep-run.log`) and keep `ux-deep-summary.json`.
2. Always validate run outcome from summary fields.
- Confirm `ConfigPhaseStatus` and `ScreensaverPhaseStatus` are `Completed`.
- Investigate any `Failed` status before trusting screenshots.
3. Always validate version markers after UX launch.
- Config launch: window title must include `BETA-5.4`.
- Screensaver launch: version text containing `beta5` must be detected in UIA (`ScreensaverVersionCheck=Passed`).
4. Always export latest guest result folder to host artifacts before analysis.
- Use `Guest-ExportLatestVmUxResult.ps1` and verify copied capture counts.

## 10) Multi-Resolution Fallback Strategy (Do This Order)

When running thorough UX passes across multiple resolutions, use this exact sequence:

1. Attempt host-side VirtualBox mode hint first.
- `VBoxManage controlvm Windows10Pro setvideomodehint <W> <H> 32`
- Immediately capture one screenshot and verify actual dimensions before proceeding.
2. If capture dimensions do not change, keep VM in GUI frontend and switch in guest display settings.
- Open desktop display settings in guest (`ms-settings:display`) and set target resolution manually.
- Re-capture and verify dimensions again before running UX script.
3. Only after dimension truth passes, launch deep UX runner.
- `\\VBOXSVR\\codexrepo\\build\\vm\\Guest-UxDeepExercise.ps1`
4. Write actual resolution into run notes.
- Do not trust folder label alone; trust pixel dimensions from saved screenshots.

## 11) Latest Reliable Deep-Run Method (2026-04-11)

Use this exact pattern to avoid silent launch failures and run contamination:

1. Launch verification gate (10-second rule).
- After any injected launch command, always take a VM screenshot at +10 seconds.
- If target window is not visible, do not wait; relaunch immediately.
2. Run-dialog focus gate.
- Verify Run dialog is actually open before injecting command text.
- Stale text in Run dialog can cause malformed command execution; clear/reopen if needed.
3. Use known guest workspace path.
- Guest user profile is `%USERPROFILE%`.
- Working UX root is `%USERPROFILE%\Desktop\PortfolioVmUx`.
4. Start deep runner minimized and with no further keyboard injection during capture window.
- Launch:
- `powershell -WindowStyle Minimized -NoProfile -ExecutionPolicy Bypass -File \\VBOXSVR\codexrepo\build\vm\Guest-UxDeepExercise.ps1`
- Any injected input during screensaver phase contaminates `screensaver-*.png` evidence.
5. Persist saver during automated capture.
- `Guest-UxDeepExercise.ps1` now sets `PORTFOLIOSAVER_DISABLE_INPUT_EXIT=1` while saver captures are running.
- This prevents incidental cursor/key movement from terminating the saver during test automation.
6. End-of-run retrieval fallback that worked.
- If automated copy/export fails, copy latest run directly from guest using `xcopy`:
- `xcopy /E /I /Y %USERPROFILE%\Desktop\PortfolioVmUx\results\ux-deep-<timestamp> \\VBOXSVR\codexrepo\build\vm\artifacts\vm-results\ux-deep-<timestamp>`

## 12) Runtime Trace Retrieval (for Debug/Defect Analysis)

Trace output now exists in-app and is always available even when UX capture is noisy.

Trace files:

- `%APPDATA%\PortfolioSaver\Trace\trace.circular.log` (fixed 4MB circular log)
- `%APPDATA%\PortfolioSaver\Trace\trace.circular.idx` (next write offset)
- UDP forwarding now retries endpoint DNS resolution automatically (backoff) after transient failures.
- Trace lines now also include `tid=` plus structured `event=... | key=value` state snapshots for:
  - warmup batch planning/application
  - quote refresh planning/provider results/remaining-symbol summaries
  - macro meter snapshots
  - clock/global-market population summaries
  - scene/timer configuration snapshots

### 12.1 Quick copy from guest to host

From guest PowerShell:

```powershell
$traceRoot = Join-Path $env:APPDATA "PortfolioSaver\\Trace"
$outRoot = "\\VBOXSVR\\codexrepo\\build\\vm\\artifacts\\trace"
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null
Copy-Item (Join-Path $traceRoot "trace.circular.log") $outRoot -Force
Copy-Item (Join-Path $traceRoot "trace.circular.idx") $outRoot -Force
```

### 12.2 Decode circular file into chronological text

Run on host or guest after both files are copied:

```powershell
$log = "trace.circular.log"
$idx = [int](Get-Content "trace.circular.idx")
$bytes = [IO.File]::ReadAllBytes($log)
if ($idx -lt 0 -or $idx -ge $bytes.Length) { $idx = 0 }
$ordered = New-Object byte[] $bytes.Length
[Array]::Copy($bytes, $idx, $ordered, 0, $bytes.Length - $idx)
[Array]::Copy($bytes, 0, $ordered, $bytes.Length - $idx, $idx)
$text = [Text.Encoding]::UTF8.GetString($ordered)
$lines = $text -split "`r?`n" | Where-Object { $_ -match '^\d{4}-\d{2}-\d{2}T' }
$lines | Set-Content "trace.reconstructed.log"
```

### 12.3 Suggested first-pass filters

- Startup flow:
  - `Select-String -Path trace.reconstructed.log -Pattern "StartupCoordinator|Warmup|Provider"`
- Data population/tickers:
  - `Select-String -Path trace.reconstructed.log -Pattern "Quotes resolved|no quotes|stale|cache"`
- Structured quote planning:
  - `Select-String -Path trace.reconstructed.log -Pattern "event=QuoteRefreshPlan|event=ProviderReturnedQuotes|event=ProviderFailed|event=QuoteResolutionSummary"`
- Macro / world clock debugging:
  - `Select-String -Path trace.reconstructed.log -Pattern "event=MacroSnapshot|event=ClockMarketDataSummary|event=ClockDataRefresh"`
- Runtime exits/render issues:
  - `Select-String -Path trace.reconstructed.log -Pattern "InputExitMonitor|Unhandled|ERROR"`

## 13) Command Timeout Discipline (Mandatory)

To avoid stalled cycles:

1. Every host command launched by automation must use a hard 5-minute timeout (`300s`).
2. If a command exceeds timeout, terminate child processes and continue via chunked/polling flow.
3. For long VM flows, launch asynchronously and poll artifacts in short bounded checks.

Suggested watchdog pattern:

```powershell
$p = Start-Process powershell -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','.\build\vm\Invoke-VmUxDeepCycle.ps1' -PassThru
if (-not (Wait-Process -Id $p.Id -Timeout 300 -ErrorAction SilentlyContinue)) {
  Stop-Process -Id $p.Id -Force
  Get-Process dotnet,MSBuild,VBCSCompiler -ErrorAction SilentlyContinue | Stop-Process -Force
}
```

## 14) Additional Guardrails Learned (2026-04-18)

1. `publish-safe-temp.ps1` restore must include runtime identifier.
- Safe publish can fail with `NETSDK1112` if restore is done without `-r win-x64` before self-contained publish.
- Treat runtime-specific restore as mandatory, not optional.
2. Do not trust a rerun if stale guest shells are still open on the desktop.
- A leftover elevated `cmd.exe` or prior saver/config instance can contaminate the next deep UX pass.
- Before relaunching, explicitly close/kill stale `cmd`, `PortfolioSaver.Config`, and `PortfolioSaver.Screensaver` in the guest.
3. Trust the JSON summary over folder names.
- The reliable pass gate is:
  - `ConfigPhaseStatus=Completed`
  - `ScreensaverPhaseStatus=Completed`
  - `ConfigVersionCheck=Passed`
  - `ScreensaverVersionCheck=Passed`
4. Treat live screenshots plus trace as the fastest truth source.
- In the 2026-04-18 run, live screenshots immediately showed:
  - ticker values were back
  - macro strip was still empty
  - Global Markets was still masking tape lanes
- The trace then confirmed the exact underlying cause split:
  - Yahoo macro/world lane `429`
  - main ETF/equity fallback healthy
  - Twelve Data minute-credit exhaustion later in the same run
