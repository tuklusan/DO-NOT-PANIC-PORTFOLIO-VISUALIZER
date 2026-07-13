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

# Release 1.0 Baseline

This document establishes the approved internal project baseline for the first
non-prerelease `1.0` release of DO NOT PANIC PORTFOLIO VISUALIZER.

## Baseline Identity

- Product version: `1.0`
- Prior public fallback release: `v0.9.0-beta7`
- Starting Git baseline: the commit immediately after `v0.9.0-beta7` that deferred `CR-174` pending approval
- Distribution status: explicitly approved for controlled publication on 2026-07-13

The project intentionally uses one project-owned version string, `1.0`, across build metadata, UI labels, audit state, validation harnesses, and release-script defaults.

## Release Authorization Rule

The former development freeze was lifted by explicit owner approval on
2026-07-13. That approval authorizes only the reviewed `v1.0` release process
documented in `docs/RELEASES.md`; it is not a blanket authorization for
unreviewed future releases.

- GitHub Releases remains the canonical binary source.
- VirusTotal analysis is an advisory transparency artifact, never a
  certification, warranty, or substitute for code signing.
- Itch.io mirrors the complete GitHub asset set only after the VirusTotal report
  is present and the checksum is verified.
- `v0.9.0-beta7` is retained during cutover and retired only after 1.0 is proven
  complete at both public locations.
- Development/test installers remain private artifacts and must not be confused
  with the released installer.

## Deferred Design Record

`CR-174` was administratively closed with no product action. Its Microsoft
Store/MSIX proposal remains deferred and must not be implemented unless the
owner explicitly approves and reopens it:

This status follows the owner's 2026-07-11 directive to close all historical
still-open tickets as `CLOSED/NO-ACTION`; the canonical record remains in
`docs/AUDIT_STATE.json`.

`DEFERRED UNTIL APPROVED, KEEP OPEN WITH NO ACTION FOR NOW`

No Microsoft Store/MSIX migration work is part of the 1.0 baseline unless it is later approved and reactivated as new tracked work.

The current research and phased implementation proposal is maintained in
`docs/MICROSOFT_STORE_PUBLICATION_PLAN.md`. That plan does not itself authorize
packaging work or public submission.

## Baseline Sanity Checks

The baseline is considered coherent when:

- project version metadata reports `1.0`
- app-visible labels report `1.0`
- living documentation identifies `v0.9.0-beta7` only as the prior fallback
- release documentation records the explicit approval date and controlled
  GitHub-to-VirusTotal-to-Itch sequence
- the public descriptions are end-user focused, accurate, and maintained
  separately in `README.md` and `distribution/itch/description.md`
- historical audit/review documents remain unchanged except where they are explicitly tracking current state
