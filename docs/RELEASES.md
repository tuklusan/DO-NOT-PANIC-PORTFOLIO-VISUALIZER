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

# Public Releases

The canonical public download channel for DO NOT PANIC PORTFOLIO VISUALIZER is GitHub Releases:

- Latest release: https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER/releases/latest
- All releases: https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER/releases

Installer `.exe` files and checksum artifacts are intentionally published as GitHub Release assets. They are not committed into a source-tree `RELEASE` directory because installers are binary build outputs that would unnecessarily bloat repository history.

SourceForge or another mirror is not required for the current public non-commercial release. Revisit mirroring only if GitHub download availability, analytics, or audience reach becomes a product requirement.

## News refresh migration note

Starting with the OpenRouter-ready beta-7 release line, existing finance-news refresh settings below 30 minutes are automatically upgraded to 30 minutes. This keeps default `openrouter/free` usage under the documented low free-tier daily request limit during continuous all-day runs.
