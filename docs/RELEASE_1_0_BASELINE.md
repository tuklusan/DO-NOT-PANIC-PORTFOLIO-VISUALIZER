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

This document establishes the internal project baseline for work toward the first non-prerelease `1.0.0` release of DO NOT PANIC PORTFOLIO VISUALIZER.

## Baseline Identity

- Product semantic version: `1.0.0`
- Product/baseline display label: `1.0`
- Last public fallback release: `v0.9.0-beta7`
- Starting Git baseline: the commit immediately after `v0.9.0-beta7` that deferred `CR-174` pending approval
- Distribution status: frozen until 1.0 development is complete and explicitly approved for publication

## Release Freeze Rule

During 1.0 development, do not update public distribution channels:

- Do not publish or replace GitHub Release assets.
- Do not mirror new builds to Itch.io.
- Do not publish new VirusTotal release reports for unpublished development builds.
- Do not retag `v0.9.0-beta7`; it remains the current public fallback until the 1.0 release is ready.

Local, VM, and private installer builds are allowed for development validation, but they must not be treated as public distribution artifacts.

## Open Deferred Work

`CR-174` remains open and explicitly deferred:

`DEFERRED UNTIL APPROVED, KEEP OPEN WITH NO ACTION FOR NOW`

No Microsoft Store/MSIX migration work is part of the 1.0 baseline unless it is later approved and reactivated.

## Baseline Sanity Checks

The baseline is considered coherent when:

- project version metadata reports `1.0.0`
- app-visible labels report `1.0`
- living documentation identifies `v0.9.0-beta7` only as the current public fallback, not the active development lane
- release documentation clearly states that no distribution channel is updated during 1.0 development
- historical audit/review documents remain unchanged except where they are explicitly tracking current state
