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

# Degraded UX Contract

Applies to: CR-087, CR-089, CR-090, CR-091

This contract defines what users should see, hear through UI Automation/screen readers, and be able to do when market data, news, background images, or configuration validation degrade.

## Accessibility States

- Runtime freshness must expose the same text visually and through UI Automation. The current automation id is `RuntimeDataFreshnessText`, and the name must match the visible freshness text.
- Healthy quote flow is announced as `LIVE quote feed`.
- Startup with no data is announced as `LOADING - waiting for data`.
- Offline with no data is announced as `OFFLINE - waiting for data`.
- Offline with last-known values is announced as `OFFLINE - showing last values`.
- Stale cache is announced as `STALE - cached values present`.
- Config status must expose a stable automation id, `ConfigStatusText`, so validation progress and failure text can be inspected without relying on pixels.
- Config primary/cancel buttons must expose `ConfigPrimaryButton` and `ConfigCancelButton`; after successful validation the user-visible choice is OK or Cancel.

## Placeholder Contract

- Unknown, loading, or unavailable numeric values use the stable placeholder `--`.
- Placeholders must not resize rows/cards/ribbons when neighboring symbols update.
- Placeholders must not show raw provider exceptions, protocol errors, ports, checksums, stack traces, JSON text, or HTTP status codes.
- If a last successful fetch time exists, it belongs in trace/detail metadata or tooltip-style detail, not in the fixed-width primary value field.
- Cached/stale values should remain visible when available; the freshness/status text communicates whether the data is live, cached, stale, or offline.

## Config Error Clarity

- Slow validation disables the Validate/primary action while work is in progress and keeps Cancel available.
- Failed validation re-enables Validate/Retry and keeps Cancel available.
- Plain-language guidance should tell the user whether to retry later, check connectivity, correct a symbol, wait out throttling, or cancel safely.
- User-facing config messages must avoid implementation details such as port conflicts, crumbs, checksum failures, JSON parse errors, stack traces, and raw HTTP status codes.
- Progress text should fit in the validation progress window by wrapping and scrolling rather than clipping.

## Interaction Responsiveness

- Cancel and Escape must close config dialogs promptly without waiting for pending network operations.
- The VM harness must retain keyboard-first paths for Settings, Validate/OK/Cancel, scrolling, Escape, and fullscreen toggles.
- High-latency or offline network work must not block the dispatcher or batch-freeze the scene.
- Disabled controls should look disabled through normal WPF disabled styling and should not silently discard user input.

## High Contrast

- Degraded-state communication must not rely on color alone. The text labels `OFFLINE`, `STALE`, `LOADING`, `LIVE`, and `--` are the canonical non-color signals.
- Existing WPF disabled styling and readable foreground/background contrast are required to remain intact under Windows high contrast themes.
