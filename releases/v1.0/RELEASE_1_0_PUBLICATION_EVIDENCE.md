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

# Release 1.0 Publication Evidence

Publication completed and was independently rechecked on 2026-07-13.
This is the immutable publication snapshot for that cutover. Do not revise its
recorded installer hash or third-party scan outcome if a later release changes.

## Canonical GitHub Release

- Release: https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER/releases/tag/v1.0
- Tag: `v1.0`
- Installer: `DoNotPanicPortfolioVisualizerSetup-1.0.exe`
- Installer SHA-256: `f035c1810903b92b98243d6071975bb99d35981a6208d61e9ffab1384f6ecd3e`
- Checksum asset: `DoNotPanicPortfolioVisualizerSetup-1.0.sha256.txt`
- Advisory asset: `virustotal-advisory-report.md`

The installer is intentionally unsigned. The public README and release notes
tell users how to verify the checksum and handle Microsoft Defender SmartScreen
without describing VirusTotal as a certification or guarantee.

## VirusTotal Advisory

- Result: `0 malicious`, `0 suspicious`, `61 harmless`, `31 undetected`
- Project copy: `releases/v1.0/virustotal-advisory-report.md`
- Public URL report: https://www.virustotal.com/gui/url/aHR0cHM6Ly9naXRodWIuY29tL3R1a2x1c2FuL0RPLU5PVC1QQU5JQy1QT1JURk9MSU8tVklTVUFMSVpFUi9yZWxlYXNlcy9kb3dubG9hZC92MS4wL0RvTm90UGFuaWNQb3J0Zm9saW9WaXN1YWxpemVyU2V0dXAtMS4wLmV4ZQ/detection

VirusTotal is a third-party advisory signal only, not a warranty,
certification, or substitute for code signing.

## Itch.io Mirror

- Page: https://tuklusan.itch.io/do-not-panic-portfolio-viewer
- Mirror workflow: https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER/actions/runs/29254423020
- Visible version: `v1.0`
- Visible files: installer, SHA-256, MD5, and VirusTotal advisory report
- Canonical page copy: `distribution/itch/description.md`
- Public metadata: human-readable title, `Released` status, accurate
  source-available license language, and end-user installation guidance

Cache-busted public-page verification confirmed the canonical description and
all four 1.0 files. No beta file or archive-style download remained visible.

## Predecessor Retirement

After both 1.0 surfaces passed verification, GitHub Release and tag
`v0.9.0-beta7` were deleted. The 1.0 Butler channels replaced the former itch.io
downloads, leaving no beta download visible to users. Historical ticket and
validation records remain in the audit documents solely as development history.
