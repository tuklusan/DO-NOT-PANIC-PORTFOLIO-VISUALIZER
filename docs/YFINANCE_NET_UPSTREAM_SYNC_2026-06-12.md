<!--
============================================================================
Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
Proprietary rights reserved except as expressly licensed herein.

DO NOT PANIC PORTFOLIO VIEWER
This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
personal, educational, or hobbyist use only. Commercial exploitation,
corporate internal operations, or AI model training are strictly forbidden.

ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
which is licensed under the Apache License, Version 2.0. A copy of the Apache
License is provided within the distribution environment.

FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
It does not provide financial, investment, legal, or tax advice. All data
calculation and scraping outputs are provided 'AS IS' with zero guarantee
of real-time accuracy or upstream availability.

This file is subject to the terms and conditions defined in the LICENSE
file located in the root directory of this source code repository.
Removal or modification of this legal notice constitutes copyright infringement.
============================================================================
-->

# YFinance.NET Upstream Sync Review - 2026-06-12

## Scope
CR-062 reviewed current `ranaroussi/yfinance` upstream and the user's documented `tuklusan/yfinance` fork lineage against the repo-local `YFinance.NET` clean-room port.

The goal was not full Python API parity. The goal was to identify whether recent upstream changes affect the product-critical YFinance.NET responsibilities used by Do Not Panic Portfolio Visualizer:

- Yahoo quote retrieval
- Yahoo quoteSummary retrieval
- chart/history retrieval
- market timing from chart metadata
- cache freshness behavior
- session/transport/rate-limit behavior
- upstream-sync maintainability

## Upstream Revisions Examined
Both repositories resolved to the same synchronized commit during the review:

- `ranaroussi/yfinance`: `125b12e058fe37971390e32333d2cf9edb2a8a50`
- `tuklusan/yfinance`: `125b12e058fe37971390e32333d2cf9edb2a8a50`
- Commit date: `2026-05-28T21:01:28+01:00`
- Commit subject: `Version 1.4.1`

This confirmed the user's fork was synchronized with current upstream at the time of review.

## Recent Upstream Changes Mapped

### yfinance 1.4.1
- Preserve Date/Datetime index names in `yf.download()` output.
- YFinance.NET mapping: not applicable.
- Rationale: the .NET port does not expose pandas/DataFrame download output. The desktop product consumes typed quote/history DTOs and does not have a DataFrame index-name surface.

### yfinance 1.4.0
- Added auth/login class.
- YFinance.NET mapping: deferred/not applicable.
- Rationale: current YFinance.NET uses the anonymous Yahoo Finance web endpoints needed by the product. No product requirement currently depends on authenticated Yahoo account APIs.

- Added region scoping for Sector/Industry and lang/region scoping for `Ticker`.
- YFinance.NET mapping: implemented for active product Yahoo calls.
- Rationale: the .NET port now carries `YFinanceOptions.Language` and `YFinanceOptions.Region`, defaulting to upstream-compatible `en-US` and `US`, and applies them to quote, quoteSummary, chart/history, and market-timing requests.

- Made `curl_cffi` optional with fallback to `requests`.
- YFinance.NET mapping: not applicable.
- Rationale: the .NET port uses `HttpClient`, cookie handling, request pacing, and its own transport abstraction rather than Python HTTP client stacks.

- Added `repair` option to `get_history_metadata()`.
- YFinance.NET mapping: deferred.
- Rationale: the product uses chart metadata primarily for market timing and price history. Full upstream-style price repair remains explicitly deferred in `YFinance.net/PORTING_PLAN.md`.

- Added defensive metadata parsing and fixed `TypeError` when `data["chart"] is None`.
- YFinance.NET mapping: implemented for active history and market-timing parsing.
- Rationale: Yahoo's known `chart:null` case now fails soft. History returns an empty response and market timing returns null instead of throwing a structural parsing exception. Missing or non-object chart nodes still throw `YFinanceApiException` because those shapes indicate malformed or unexpected payloads rather than the upstream-handled null-chart condition.

- Fixed `_dts_in_same_interval("1mo")` year handling.
- YFinance.NET mapping: not applicable.
- Rationale: the current product does not expose upstream Python repair/date-interval logic.

- Fixed localized intraday `download()` UTC handling.
- YFinance.NET mapping: not applicable to current product surface.
- Rationale: YFinance.NET requests chart data with explicit Unix UTC timestamps and returns typed bars. There is no pandas localized-intraday download output path.

- Made `yf.download()` reentrant by removing shared globals.
- YFinance.NET mapping: already covered by architecture.
- Rationale: the product's UI requests are client-driven and sequential, and the YFinance.NET server/client protocol avoids the Python `download()` shared-global model.

- Dividend repair/unlisted dividend fixes.
- YFinance.NET mapping: not applicable.
- Rationale: dividend DataFrame repair is not exposed by the current product.

- Market region validation.
- YFinance.NET mapping: partially implemented through configurable region; broader sector/industry market validation remains outside current product scope.

## Implemented YFinance.NET Changes

1. Added `YFinanceOptions.Language` and `YFinanceOptions.Region`.
2. Added a shared locale query helper to apply `lang` and `region` consistently.
3. Applied locale scoping to:
   - `/v7/finance/quote`
   - `/v10/finance/quoteSummary/{symbol}`
   - `/v8/finance/chart/{symbol}` history requests
   - `/v8/finance/chart/{symbol}` market-timing requests
4. Hardened chart parsing so Yahoo `chart:null` payloads do not crash history or market timing, while missing or malformed chart nodes still throw explicit `YFinanceApiException`.
5. Added regression tests for default/custom locale query parameters and null chart parsing.

## Deliberate Non-Changes

The following upstream changes were intentionally not ported because they are outside the current baseline product surface:

- pandas/DataFrame `download()` output behavior
- Python `curl_cffi`/`requests` transport selection
- authenticated Yahoo login
- full price repair
- dividend repair
- sector/industry APIs
- websocket/live parity

These remain candidates for future CRs only if the desktop product requires the corresponding feature.

## Validation Plan

Required closure evidence for CR-062:

- DeepSeek pre-commit review of the YFinance.NET sync delta
- focused local tests for locale and null chart behavior
- full local Release test suite
- practical live YFinance.NET validation because the change affects Yahoo request query parameters
