<!--
============================================================================
Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
Proprietary rights reserved except as expressly licensed herein.

DO NOT PANIC PORTFOLIO VISUALIZER
This file is governed by the SANYALnet Labs Non-Commercial License in the
root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
for AI/ML model training are prohibited unless separately authorized.

Attribution is required: "Based on original work by Supratim Sanyal of
SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
patent, trademark, and governing-law provisions.
============================================================================
-->

# DeepSeek Documentation Consistency Review - 2026-07-03

- Review type: tracked project documentation cross-document consistency, release sanity, accuracy, and maintainability review
- Developer of record: Codex
- Reviewer: DeepSeek v4-flash via chunked documentation packet process
- Source artifact directory: `build/deepseek-review/docs-consistency-20260703-093111`
- Document manifest: `build/deepseek-review/docs-consistency-20260703-093111/document-manifest.json`
- Packet manifest: `build/deepseek-review/docs-consistency-20260703-093111/packet-manifest.json`
- Final synthesis artifact: `build/deepseek-review/docs-consistency-20260703-093111/FINAL_SYNTHESIS.md`
- Scope: 43 tracked documentation/support artifacts in 4 packets. Binary documentation assets were represented by path, size, and SHA-256 metadata.

## Process Notes

DeepSeek was instructed to distinguish historical review/audit records from active current-facing documentation. Historical records were not treated as stale merely because they recorded earlier project states.

Codex accepted five DeepSeek findings as actionable documentation/workflow consistency CRs: CR-220 through CR-224.

## Created CRs

| CR | Priority | Severity | Area | Title | Status |
| --- | --- | --- | --- | --- | --- |
| CR-220 | P1 | High | Release workflow | Guard Itch.io publishing during 1.0 development freeze | open |
| CR-221 | P2 | Medium | VM documentation | Replace hardcoded absolute paths in VM operations runbook | open |
| CR-222 | P2 | Medium | Documentation consistency | Cross-link degraded UX contract from build and validation docs | open |
| CR-223 | P3 | Low | Itch workflow maintainability | Document or automate Butler checksum update workflow | open |
| CR-224 | P3 | Low | Itch workflow clarity | Clarify or remove Itch.io generated MD5 channel | open |

## DeepSeek Synthesis

## ACCEPTED_CANDIDATES

### 1. No guard against accidental Itch.io publishing during 1.0 development freeze  
- **Priority:** 1  
- **Severity:** High  
- **Area:** `.github/workflows/itch-publish.yml` vs `BUILD_AND_DEPLOY.md`  
- **Evidence:** `BUILD_AND_DEPLOY.md` declares a release freeze with “do not publish or replace GitHub Release assets, mirror builds to Itch.io.” Yet the `itch-publish.yml` workflow can be triggered manually via `workflow_dispatch` with no freeze-check.  
- **Recommendation:** Add an explicit abort condition early in the workflow (e.g., check a repository variable `FROZEN`) or remove `workflow_dispatch` until the freeze is lifted.  
- **Acceptance criteria:**  
  - Workflow fails gracefully with an explanatory message when freeze is active.  
  - Any developer re-enabling the trigger must explicitly disable the freeze variable.

### 2. Hardcoded absolute paths in VM runbook  
- **Priority:** 2  
- **Severity:** Medium  
- **Area:** `build/vm/VM_OPERATIONS_RUNBOOK.md`  
- **Evidence:** Lines under “Canonical host scripts” use path `D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\build\vm\Push-VmWorkspace.ps1` – will fail on any other machine.  
- **Recommendation:** Replace each absolute path with a relative repository root reference (e.g., `.\build\vm\Push-VmWorkspace.ps1`) or add a note that paths are examples.  
- **Acceptance criteria:**  
  - All script references in the runbook use relative paths or are explicitly marked as examples.  
  - A developer cloning the repo can follow the runbook without any path modification.

### 3. Missing cross-link to degraded UX contract in build/deploy guide  
- **Priority:** 2  
- **Severity:** Medium  
- **Area:** Cross-document consistency: `docs/ANOMALY_DEGRADATION_TEST_PLAN.md` vs `BUILD_AND_DEPLOY.md` and `AGENTS.md`  
- **Evidence:** The anomaly test plan defines a detailed “Reference display contract” (healthy/degraded/offline) referenced as part of CR-086 closure. `BUILD_AND_DEPLOY.md` and `AGENTS.md` do not mention this contract or cross-link to it.  
- **Recommendation:** Add a short pointer in `BUILD_AND_DEPLOY.md` (e.g., under “Manual validation checklist”) to the anomaly test plan or to a dedicated `DEGRADED_UX_CONTRACT.md`. Update `AGENTS.md` if appropriate.  
- **Acceptance criteria:**  
  - `BUILD_AND_DEPLOY.md` contains a visible link to the contract definition.  
  - Anyone performing manual validation can easily locate the expected degraded‑state visuals.

### 4. Hardcoded Butler SHA‑256 may silently fail after version update  
- **Priority:** 3  
- **Severity:** Low  
- **Area:** `.github/workflows/itch-publish.yml`  
- **Evidence:** The workflow pins `BUTLER_VERSION: v15.27.0` and hardcodes `BUTLER_SHA256`. An update of the version or a checksum change will cause a silent download failure.  
- **Recommendation:** Add a comment above the two variables explaining how to obtain the new checksum (e.g., link to Itch.io Butler releases) or automate the hash fetch inside the workflow.  
- **Acceptance criteria:**  
  - A developer can update Butler version by changing only the version number; the hash may still need manual update but the comment provides clear instructions.

### 5. Itch.io channel naming inconsistency – `windows-md5` without clear explanation  
- **Priority:** 3  
- **Severity:** Low  
- **Area:** `.github/workflows/itch-publish.yml`  
- **Evidence:** The workflow defines `ITCH_MD5_CHANNEL: windows-md5` but the MD5 file is generated only during the workflow (not part of GitHub Release assets). No comment clarifies that MD5 is an ad‑hoc extra artifact.  
- **Recommendation:** Remove the MD5 channel (since it is not a required release asset) or add a comment explaining its purpose.  
- **Acceptance criteria:**  
  - The workflow either removes `ITCH_MD5_CHANNEL` or includes a comment explaining that the MD5 is generated solely for Itch.io and is not part of the canonical asset set.

---

## REJECTED_OR_DUPLICATE

- **Stale `current_snapshot` contradicts resolved CRs** (from packet-002)  
  **Reason:** The `current_snapshot` object is a historical review record (internal audit artifact), not current product documentation. Per meta-instruction, such records are evidence and should not be flagged as stale unless they contradict active-facing docs or workflow requirements. No such contradiction was demonstrated; the snapshot simply reflects an older state. Updating it would be nice but is not a release‑blocking or testable issue. Rejected as out of scope.

---

## OVERALL_VERDICT

The active documentation and workflows are generally consistent, but several concrete issues threaten release sanity and developer onboarding. The most urgent is the unprotected Itch.io publish trigger, which could accidentally ship unreleased binaries during the 1.0 freeze. The VM runbook needs path corrections to be usable by other developers, and the degraded‑UX contract should be cross‑linked from the build guide to prevent regression. Lower‑priority maintenance items (Butler version handling and MD5 channel clarity) are minor but worth addressing before release. The audit state inconsistency flagged in packet‑002 is a historical artifact that does not affect current product documentation or workflows, and can be ignored for this release cycle. Overall, after fixing the high‑priority guard and the medium‑priority runbook and cross‑link issues, the documentation set will be sane and ready for a controlled release.

