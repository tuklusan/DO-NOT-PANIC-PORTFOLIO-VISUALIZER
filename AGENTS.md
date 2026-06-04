# AGENTS

## Process Management note
Whenever you start a background process, local development server, or testing instance, you MUST explicitly terminate it before reporting the task as complete.

## IDE orientation
Assume the primary developer uses **Visual Studio 2022**.
Do not make the project depend on a non-Visual-Studio-first workflow.

## Design note for graph overlays
These are not giant dashboard charts. They are small floating sparkline cards. Keep them elegant, semi-transparent, and readable.

## Motion note
The graph cards and any floating overlay elements should move slowly like polite billiard balls, not pinball. Use low velocities and long-lived motion.

## Clock note
Keep the visible top-right status clock pinned to UTC.
Exchange-local times belong in the Global Markets lane and related exchange cards.
Any floating clock-style overlay behavior should stay visually consistent with the small sparkline-card language rather than becoming a large dashboard widget.

## Data note
Historical cache belongs under `%LOCALAPPDATA%\PortfolioSaver\Caches\History`. Delete history files older than 14 days.

## Canonical Codex operational rule
When running Codex-Agent, cloud requests initiated by the agent must be spaced out by at least 15 seconds after the last response.
This is a Codex operational constraint only and is not a product/runtime throttling requirement for the screensaver codebase.

## Mandatory DeepSeek code-review gate
For any code modification, including application code, XAML, scripts, harnesses, tests, project files, or build tooling, run a DeepSeek code-review pass before committing, pushing, or starting local/VM validation cycles.

Use:

```powershell
.\build\Run-DeepSeekCodeReview.ps1 -IncludeUntracked -SendForReview -AcknowledgeSecretScan
```

Resolve actionable findings and rerun the review when fixes are made. If no DeepSeek API key is available through `DEEPSEEK_API_KEY`, `PORTFOLIOSAVER_DEEPSEEK_API_KEY`, or `build\vm\test-secrets.json`, treat code commit/test/VM validation as blocked until the key is available or the user explicitly waives the gate for that specific change.

This repository is explicitly authorized by the project owner to use DeepSeek as an external code reviewer for pending code changes, but secrets and local-only credentials must never be included in review packets. The script performs best-effort secret scanning only; manually inspect/redact sensitive changes before using `-SendForReview -AcknowledgeSecretScan`. When editing the review gate itself, first run `.\build\Run-DeepSeekCodeReview.ps1 -SelfTest`, then run the normal DeepSeek review gate.

Review packets are written under ignored `build\deepseek-review\`. If a packet unexpectedly contains sensitive material, delete it immediately, remove the sensitive source from the pending changes, and do not use `-SendForReview` until the packet is clean.

The trusted DeepSeek review endpoint is `https://api.deepseek.com`. If a future endpoint is intentionally used, pass `-AcknowledgeEndpointOverride` only after verifying the destination.

Documentation-only ticket updates may be committed without this gate, but any change that can affect runtime behavior, build behavior, tests, harnesses, packaging, or developer workflow must pass the gate.

## Visual note
Green for upward segments, red for downward segments. The line should be split by movement direction, not just colored by final result.

