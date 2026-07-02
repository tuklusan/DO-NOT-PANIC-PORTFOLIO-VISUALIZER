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

Release-specific advisory metadata, including VirusTotal URL-scan reports for public installer downloads when available, is tracked under the repository `releases/<tag>/` directory. The release hook also posts a bounded public VirusTotal URL comment with the installer download link, release tag, SHA-256, and compact app summary so the VirusTotal public page has provenance context. Comment posting is enabled by default and can be suppressed for scan-only advisory runs with `-SkipComment`. If comment posting fails, the advisory report still records the failure so scan evidence is not lost; strict release gates can use the hook's `-RequireComment` switch. These VirusTotal artifacts are third-party advisory signals only; they are not warranties, certifications, or guarantees that software is safe.

## Itch.io mirror sequence

The Itch.io project page is a secondary convenience mirror at:

- https://tuklusan.itch.io/do-not-panic-portfolio-viewer

The GitHub Release remains the source of truth. Itch publishing must happen only after the GitHub Release has all public release assets:

1. `DoNotPanicPortfolioVisualizerSetup-<version>.exe`
2. `DoNotPanicPortfolioVisualizerSetup-<version>.sha256.txt`
3. `virustotal-advisory-report.md`

The `.github/workflows/itch-publish.yml` workflow enforces that ordering by polling the GitHub Release until the complete asset set exists, then generating the MD5 checksum and pushing four single-file Butler channels:

- `windows` - installer executable
- `windows-sha256` - SHA-256 checksum
- `windows-md5` - MD5 checksum
- `windows-virustotal-report` - VirusTotal advisory report

The workflow may wait up to 60 minutes for release finalization so a newly published GitHub Release does not mirror to Itch until the VirusTotal advisory report and matching checksum are actually present. Butler creates or updates these channels as needed; pushing the same channel again replaces the current user-visible build for that channel after Itch processing completes.

Do not push the whole release-asset folder to Itch. Butler treats folder pushes as build/archive channels, which can surface as a `.zip`-style download on the Itch page. The release workflow intentionally pushes each file directly so website users receive the installer and advisory files as separate downloads.

If future release assets are mirrored to Itch, add an explicit Butler channel and single-file `butler push` for each new file; do not expand the workflow back to a folder push.

SourceForge or another mirror is not required for the current public non-commercial release. Revisit mirroring only if GitHub download availability, analytics, or audience reach becomes a product requirement.

## News refresh migration note

Starting with the OpenRouter-ready beta-7 release line, existing finance-news refresh settings below 30 minutes are automatically upgraded to 30 minutes. This keeps default `openrouter/free` usage under the documented low free-tier daily request limit during continuous all-day runs.
