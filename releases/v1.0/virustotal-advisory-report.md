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

# VirusTotal Advisory Scan Report - v1.0

This advisory report records a VirusTotal URL scan for the public GitHub Release installer download URL. VirusTotal results are useful third-party signals, but they are not a warranty, certification, or guarantee that software is safe.

## Release Asset

| Field | Value |
| --- | --- |
| Release tag | v1.0 |
| GitHub release | https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER/releases/tag/v1.0 |
| Installer asset | DoNotPanicPortfolioVisualizerSetup-1.0.exe |
| Installer URL | https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER/releases/download/v1.0/DoNotPanicPortfolioVisualizerSetup-1.0.exe |
| GitHub asset SHA-256 | be8c6cbccd07c1a024d3a12188585fdc6b1bf9a23f8027139fa0a3c0a8737886 |
| GitHub asset size | 153477703 bytes |
| Report generated | 2026-07-14 12:48:06 -04:00 |

## VirusTotal

| Field | Value |
| --- | --- |
| Submission type | Public URL scan |
| Analysis ID | u-d2979de7678a5a9d885d0679cb08d515746ecc70b2e28d531ff3547a2dc2176c-f6fdd8f2 |
| Analysis status | completed |
| Completion note | VirusTotal analysis completed before this report was generated. |
| URL object ID | aHR0cHM6Ly9naXRodWIuY29tL3R1a2x1c2FuL0RPLU5PVC1QQU5JQy1QT1JURk9MSU8tVklTVUFMSVpFUi9yZWxlYXNlcy9kb3dubG9hZC92MS4wL0RvTm90UGFuaWNQb3J0Zm9saW9WaXN1YWxpemVyU2V0dXAtMS4wLmV4ZQ |
| VirusTotal analysis API | https://www.virustotal.com/api/v3/analyses/u-d2979de7678a5a9d885d0679cb08d515746ecc70b2e28d531ff3547a2dc2176c-f6fdd8f2 |
| VirusTotal URL report | https://www.virustotal.com/gui/url/aHR0cHM6Ly9naXRodWIuY29tL3R1a2x1c2FuL0RPLU5PVC1QQU5JQy1QT1JURk9MSU8tVklTVUFMSVpFUi9yZWxlYXNlcy9kb3dubG9hZC92MS4wL0RvTm90UGFuaWNQb3J0Zm9saW9WaXN1YWxpemVyU2V0dXAtMS4wLmV4ZQ/detection |
| Release context comment status | posted |
| Release context comment ID | u-d2979de7678a5a9d885d0679cb08d515746ecc70b2e28d531ff3547a2dc2176c-d5292739 |
| Release context comment note | Release context comment posted to the VirusTotal URL object. |

## Last Analysis Stats

| Category | Count |
| --- | --- |
| malicious | 0 |
| suspicious | 0 |
| undetected | 31 |
| harmless | 61 |
| timeout | 0 |

## Operational Notes

- The release hook submits the already-public GitHub installer download URL rather than uploading the local installer binary.
- The release hook posts a bounded public VirusTotal URL comment containing the download URL, release tag, SHA-256, and compact app summary so the VirusTotal report has provenance context. That adds one extra API call per release run, plus up to two quota-aware retries if VirusTotal has not accepted comments for the URL object yet. Pass -SkipComment for scan-only advisory runs.
- Comment-post failure is recorded in the advisory report by default so the scan evidence is not lost; pass -RequireComment if a release gate must fail after report generation when the comment cannot be posted.
- VirusTotal Public API limits are 500 requests/day and 4 requests/minute; this hook polls no more often than every 20 seconds.
- A clean or low-detection result is advisory only. Users should still apply normal software-installation judgment.
