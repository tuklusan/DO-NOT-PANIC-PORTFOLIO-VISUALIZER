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

## Release 1.0 publication record

The owner explicitly approved the first non-prerelease `1.0` publication on
2026-07-13. The controlled cutover completed that day: GitHub Release `v1.0`
is canonical, the complete four-file set is mirrored on itch.io, the public
descriptions were verified, and the obsolete `v0.9.0-beta7` GitHub release and
tag were deleted. No beta download remains visible on itch.io. See
`releases/v1.0/RELEASE_1_0_PUBLICATION_EVIDENCE.md` for the final evidence.

The release cutover is transactional:

1. Commit and push the reviewed 1.0 source, documentation, and release tooling.
2. Build and validate a fresh `DoNotPanicPortfolioVisualizerSetup-1.0.exe`.
3. Create the GitHub `v1.0` release with the installer and matching SHA-256.
4. Submit the public installer URL to VirusTotal and attach the completed
   `virustotal-advisory-report.md`.
5. Allow the gated GitHub Actions workflow to mirror the installer, SHA-256,
   generated MD5, and VirusTotal report to Itch.io.
6. Verify the GitHub assets, checksum, workflow result, itch.io downloads, and
   public-facing descriptions.
7. Only after 1.0 is complete at both locations, delete the old GitHub beta
   release/tag and remove or replace every beta/obsolete itch.io download.

For a future transactional cutover, retain the prior public release until every
verification step succeeds. See `docs/RELEASE_1_0_BASELINE.md` for the approved
1.0 baseline.

Public release tags use a lowercase `v` prefix followed by the project version;
the tag for this release is exactly `v1.0`, while the product and installer
version remain `1.0`.

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

The canonical itch.io page description is maintained separately from the
GitHub README in `distribution/itch/description.md`. Update the itch.io project
page from that file as a deliberate release step; Butler manages downloads but
does not edit store-page prose. The GitHub README remains repository-specific
and retains the canonical GitHub Releases download link.

For the completed 1.0 cutover, the four named Butler channels replaced their
existing public downloads with version `v1.0`. The itch.io page was verified to
show only the installer, SHA-256, MD5, and VirusTotal report for 1.0. Prior
Butler revisions may remain in itch.io's internal patch history, but no beta
installer or stale archive is user-visible. The canonical end-user description
from `distribution/itch/description.md` was applied and verified on 2026-07-13.

SourceForge or another mirror is not required for the current public non-commercial release. Revisit mirroring only if GitHub download availability, analytics, or audience reach becomes a product requirement.

## News refresh migration note

Starting with the OpenRouter-ready beta7-to-1.0 line, existing finance-news refresh settings below 30 minutes are automatically upgraded to 30 minutes. This keeps default `openrouter/free` usage under the documented low free-tier daily request limit during continuous all-day runs.
