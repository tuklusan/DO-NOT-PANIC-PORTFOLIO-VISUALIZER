# Status

## Current release lane

- Active branch baseline: `BETA-5.4`
- Semantic version: `0.9.0-beta5.4`
- Active deep audit/checklist state: `docs/BETA54_AUDIT_STATE.json`
- Repository history intentionally starts at the `BETA-5.4` baseline on `main`

## Current product posture

- Visual Studio 2022-first Windows/WPF project
- MIT LICENSE clearly bundled at repo root
- Current runtime has:
  - UTC-pinned top-right clock
  - Global Markets tape with flags and open/closed status
  - in-slot tape waiting glyphs during warmup
  - conservative provider pacing with backup-first quote recovery
  - expanded bundled exchange/city backgrounds

## Current priorities

1. Keep VM validation repeatable and lightweight.
2. Preserve provider-throttle discipline and avoid regressions that reintroduce Yahoo `429` bursts.
3. Continue UX polish only where it improves readability without destabilizing the working runtime.
4. Keep secrets, scratch notes, and local VM credentials out of Git history and future pushes.

## Short roadmap

### Runtime and data
- Continue trace-driven provider optimization only when long-run traces justify it.
- Maintain cache-first historical refresh behavior under `%LocalAppData%\PortfolioSaver\Caches\History`.
- Keep provider eligibility routing conservative and explicit.

### Screensaver UX
- Continue small, readable floating sparkline cards.
- Preserve slow, low-chaos overlay motion.
- Prefer narrow, observable polish passes over broad redesigns.

### Build and release
- Keep all developer/build guidance centralized in `BUILD_AND_DEPLOY.md`.
- Keep VM workflow guidance centralized in `build/vm/VM_OPERATIONS_RUNBOOK.md`.
- Keep release gating in `docs/BETA54_AUDIT_STATE.json`.

## Canonical docs to read first

1. `README.md`
2. `BUILD_AND_DEPLOY.md`
3. `STATUS.md`
4. `docs/BETA54_AUDIT_STATE.json`
5. `build/vm/VM_OPERATIONS_RUNBOOK.md` when working with the VM harness

## Historical note

Older beta baselines and session-specific handoff notes were intentionally retired from the active doc set once the repository history was rewritten to the `BETA-5.4` root.
