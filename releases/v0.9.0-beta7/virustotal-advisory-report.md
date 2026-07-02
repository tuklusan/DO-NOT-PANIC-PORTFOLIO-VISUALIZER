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

# VirusTotal Advisory Scan Report - v0.9.0-beta7

This advisory report records a VirusTotal URL scan for the public GitHub Release installer download URL. VirusTotal results are useful third-party signals, but they are not a warranty, certification, or guarantee that software is safe.

## Release Asset

| Field | Value |
| --- | --- |
| Release tag | v0.9.0-beta7 |
| GitHub release | https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER/releases/tag/v0.9.0-beta7 |
| Installer asset | DoNotPanicPortfolioVisualizerSetup-0.9.0-beta7.exe |
| Installer URL | https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER/releases/download/v0.9.0-beta7/DoNotPanicPortfolioVisualizerSetup-0.9.0-beta7.exe |
| GitHub asset SHA-256 | 67c3e986e3d289ecd68afe4604545c053469ad24e0886fdfe1b6e0887ebedc11 |
| GitHub asset size | 269956635 bytes |
| Report generated | 2026-07-02 14:22:04 -04:00 |

## VirusTotal

| Field | Value |
| --- | --- |
| Submission type | Public URL scan |
| Analysis ID | u-39af96d0d870b0a32402fe3cb902f5c71846c78cad327ac0581553b5f5ed117a-63b4aa1e |
| Analysis status | completed |
| Completion note | VirusTotal analysis completed before this report was generated. |
| URL object ID | aHR0cHM6Ly9naXRodWIuY29tL3R1a2x1c2FuL0RPLU5PVC1QQU5JQy1QT1JURk9MSU8tVklTVUFMSVpFUi9yZWxlYXNlcy9kb3dubG9hZC92MC45LjAtYmV0YTcvRG9Ob3RQYW5pY1BvcnRmb2xpb1Zpc3VhbGl6ZXJTZXR1cC0wLjkuMC1iZXRhNy5leGU |
| VirusTotal analysis API | https://www.virustotal.com/api/v3/analyses/u-39af96d0d870b0a32402fe3cb902f5c71846c78cad327ac0581553b5f5ed117a-63b4aa1e |
| VirusTotal URL report | https://www.virustotal.com/gui/url/aHR0cHM6Ly9naXRodWIuY29tL3R1a2x1c2FuL0RPLU5PVC1QQU5JQy1QT1JURk9MSU8tVklTVUFMSVpFUi9yZWxlYXNlcy9kb3dubG9hZC92MC45LjAtYmV0YTcvRG9Ob3RQYW5pY1BvcnRmb2xpb1Zpc3VhbGl6ZXJTZXR1cC0wLjkuMC1iZXRhNy5leGU/detection |
| Release context comment status | posted |
| Release context comment ID | u-39af96d0d870b0a32402fe3cb902f5c71846c78cad327ac0581553b5f5ed117a-b24287e4 |
| Release context comment note | Release context comment posted to the VirusTotal URL object. |

## Last Analysis Stats

| Category | Count |
| --- | --- |
| malicious | 0 |
| suspicious | 0 |
| undetected | 32 |
| harmless | 60 |
| timeout | 0 |

## Operational Notes

- The release hook submits the already-public GitHub installer download URL rather than uploading the local installer binary.
- The release hook posts a bounded public VirusTotal URL comment containing the download URL, release tag, SHA-256, and compact app summary so the VirusTotal report has provenance context. That adds one extra API call per release run, plus up to two quota-aware retries if VirusTotal has not accepted comments for the URL object yet. Pass -SkipComment for scan-only advisory runs.
- Comment-post failure is recorded in the advisory report by default so the scan evidence is not lost; pass -RequireComment if a release gate must fail after report generation when the comment cannot be posted.
- VirusTotal Public API limits are 500 requests/day and 4 requests/minute; this hook polls no more often than every 20 seconds.
- A clean or low-detection result is advisory only. Users should still apply normal software-installation judgment.
