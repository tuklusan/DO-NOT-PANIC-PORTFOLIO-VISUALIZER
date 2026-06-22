<!--
============================================================================
Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
Proprietary rights reserved except as expressly licensed herein.

DO NOT PANIC PORTFOLIO VIEWER
This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
personal, educational, or hobbyist use only. Commercial exploitation,
corporate internal operations, or AI model training are strictly forbidden.

ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
which is licensed under the Apache License, Version 2.0. A copy of the Apache
License is provided within the distribution environment.

FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
It does not provide financial, investment, legal, or tax advice. All data
calculation and scraping outputs are provided 'AS IS' with zero guarantee
of real-time accuracy or upstream availability.

This file is subject to the terms and conditions defined in the LICENSE
file located in the root directory of this source code repository.
Removal or modification of this legal notice constitutes copyright infringement.
============================================================================
-->

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

First verify live DeepSeek access:

```powershell
.\build\Test-DeepSeekWorkflowGate.ps1
```

Then run the review:

```powershell
.\build\Run-DeepSeekCodeReview.ps1 -IncludeUntracked -SendForReview -AcknowledgeSecretScan
```

Resolve actionable findings and rerun the review when fixes are made. In the non-critical CR lane, a commit/push is not required before DeepSeek review. Commit and push remain mandatory before any local validation, VM validation, harness run, or other test cycle. DeepSeek API access and a valid key are mandatory for this project workflow. If `DEEPSEEK_API_KEY`, `PORTFOLIOSAVER_DEEPSEEK_API_KEY`, or `build\vm\test-secrets.json` cannot provide working DeepSeek access, hard stop: do not commit, push, run local validation, run VM validation, or proceed with workflow steps until access is restored. There is no missing-key waiver for this project.

Removed workflow switches are intentional: `-AllowMissingKeyWaiver` and `-SkipDeepSeekReview` are not supported. The live workflow gate performs one minimal DeepSeek API probe before the normal review packet is sent, so a reviewed change normally makes at least two small DeepSeek calls.

This repository is explicitly authorized by the project owner to use DeepSeek as an external code reviewer for pending code changes, but secrets and local-only credentials must never be included in review packets. The script performs best-effort secret scanning only; manually inspect/redact sensitive changes before using `-SendForReview -AcknowledgeSecretScan`. When editing the review gate itself, first run `.\build\Run-DeepSeekCodeReview.ps1 -SelfTest`, then run the normal DeepSeek review gate.

Review packets are written under ignored `build\deepseek-review\`. If a packet unexpectedly contains sensitive material, delete it immediately, remove the sensitive source from the pending changes, and do not use `-SendForReview` until the packet is clean.

The trusted DeepSeek review endpoint is `https://api.deepseek.com`. If a future endpoint is intentionally used, pass `-AcknowledgeEndpointOverride` only after verifying the destination.

Documentation-only ticket updates may be committed without this gate, but any change that can affect runtime behavior, build behavior, tests, harnesses, packaging, or developer workflow must pass the gate.

## DeepSeek delegation and token-conservation workflow
Treat Codex as the chief architect and the configured DeepSeek review/generation model as the high-throughput generation assistant. Codex context is scarce; DeepSeek context is expendable. Prefer orchestration, targeted verification, and integration over large local reads or hand-written boilerplate.

- Blind drop: For heavy boilerplate, WiX/installer authoring, large XAML layouts, generated tests, or complex algorithm scaffolding, have DeepSeek generate the file and write it directly to disk. Do not read the generated file unless build, tests, review, or targeted validation fail.
- Delegated reading: Do not read large files just to understand them. Ask DeepSeek to summarize the file structure, key methods/properties, and exact line targets, then operate from the summary plus small local spot checks.
- Test generation: After implementation, delegate xUnit test creation to DeepSeek and have it save tests directly under the test project. Codex should review failures and stitch integration, not manually author repetitive test bodies.
- Review gate still applies: All generated or modified files must still pass the mandatory DeepSeek code-review gate before staging, committing, pushing, or local/VM validation. Non-critical CR work does not require a commit/push before the DeepSeek review itself; commit/push is retained as the required checkpoint before local or VM test cycles.
- Full reviews: Full tracked codebase and documentation end-to-end reviews must be preserved as versioned synthesis documents under `docs/` using names like `DEEPSEEK_FULL_RC_REVIEW_YYYY-MM-DD.md`, with review date, reviewer, scope, artifact directory, verdict, and synthesis. Raw packets remain transient under ignored `build/deepseek-review/`.
- Local inspection is still required for safety-critical final checks: compile/test failures, diffs before commit, small targeted code reads, generated-file smoke checks, git status, process cleanup, and any place where DeepSeek output conflicts with build/runtime evidence.

## Autonomous visual validation workflow
Use `build\validation\Invoke-AutonomousVisualValidation.ps1` for unattended visual and logic release-candidate checks. The script runs the DeepSeek gate, local restore/build/tests, commits and pushes pending changes before VM validation, runs the SSH-first VM UX harness, analyzes pulled screenshots/traces, and can create audit CRs from anomalies without chat prompting.

## Mandatory DeepSeek test-artifact second-opinion gate
Whenever a workflow analyzes test result artifacts such as traces, screenshots, logs, summaries, or pulled VM result bundles, get an advisory second opinion from DeepSeek before finalizing the interpretation. The deterministic analyzer and Codex remain the final authority for pass/fail and CR generation, but DeepSeek must be used as an assistant to identify possible missed anomalies, weak proof, false positives, and follow-up checks.

The canonical artifact analyzer `build\validation\Analyze-VisualValidationArtifacts.ps1` invokes `build\validation\Invoke-DeepSeekArtifactReview.ps1` by default and writes an ignored advisory report next to the deterministic analysis JSON. Do not use `-SkipDeepSeekArtifactReview` unless debugging the analyzer itself; normal validation and CR-closure work must keep the advisory gate enabled. The process of obtaining the second opinion is mandatory and failure to obtain it blocks validation; the content of that opinion is advisory and does not override deterministic analyzer/developer judgment. Artifact producers and operators must ensure traces/logs/screenshots do not contain credentials before advisory review; the script sends screenshot metadata, not pixel data, and performs best-effort text secret scanning, but secret hygiene remains mandatory at source.

Default unattended command:

```powershell
.\build\validation\Invoke-AutonomousVisualValidation.ps1 -VmHost 192.168.56.102 -VmCycles 2 -RequiredConsecutiveCleanRuns 2 -GuestScreensaverDurationMinutes 30 -CaptureIntervalSeconds 10 -CreateChangeRequests -CommitBeforeValidation -PushBeforeValidation -AcknowledgeExternalReviewSecretScan
```

The VM guest harness currently forces a 120-second background rotation interval during these validation runs so background transitions are observable in finite time. Commit and push are explicit switches so the operator deliberately opts into that project checkpoint; if later validation fails, the resulting checkpoint commit must be fixed forward or reverted deliberately. Treat any generated CR as canonical work queue input: fix, review, build/test, commit/push before the next VM cycle, and rerun until the requested consecutive clean runs are achieved. The process-management rule still applies: do not report completion while any local project process or test instance remains running.

## Visual note
Green for upward segments, red for downward segments. The line should be split by movement direction, not just colored by final result.
