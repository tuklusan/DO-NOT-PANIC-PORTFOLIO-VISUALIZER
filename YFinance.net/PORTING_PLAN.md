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

# YFinance.NET Porting Plan

## Goal
Build a standalone .NET 10 port of the core `tuklusan/yfinance` logic inside `YFinance.net`, independent of the existing Yahoo code in the main Portfolio Visualizer app.

The first proving target is a VM-run console exerciser that:

1. determines the top 100 S&P 500 symbols by market cap with caching,
2. refreshes them every 5 minutes,
3. repeats for at least 5 total cycles,
4. keeps one-by-one symbol work paced at 1 second where batching is not used,
5. keeps no live cache or metadata older than 10 minutes,
6. completes without Yahoo 429s.

## Upstream-Alignment Rule
This port should be structured so future syncs from the user's fork of `tuklusan/yfinance` can be rolled into `YFinance.NET` deliberately.

That means:

- preserve upstream responsibility boundaries where practical,
- keep Python-module-to-.NET-component mapping explicit,
- avoid burying Yahoo-specific logic inside unrelated app code,
- keep the standalone exerciser as the first proof harness before any Portfolio Visualizer integration.

## Canonical Sync Requirement
Sync-friendliness with upstream `tuklusan/yfinance` is a canonical requirement of this port, not a nice-to-have.

Every meaningful implementation decision should preserve our ability to:

1. pull a newer upstream fork snapshot,
2. identify which Python module changed,
3. map that change to the corresponding .NET component quickly,
4. merge or re-port the behavior without rediscovering the whole architecture.

In practice, this means:

- keep module responsibility mapping stable over time,
- prefer explicit feature/service boundaries over clever cross-cutting abstractions,
- avoid mixing Yahoo transport logic into the Portfolio Visualizer app,
- avoid “one-off convenience” code that has no clear upstream analogue,
- document any intentional divergence from upstream behavior at the point it is introduced.

If a future implementation shortcut would make upstream merges materially harder, that shortcut should be treated as a design regression.

## Core Upstream Modules Reviewed
The first architecture pass was based on these upstream modules:

- `yfinance/__init__.py`
- `yfinance/data.py`
- `yfinance/base.py`
- `yfinance/ticker.py`
- `yfinance/tickers.py`
- `yfinance/multi.py`
- `yfinance/live.py`
- `yfinance/cache.py`
- `yfinance/config.py`
- `yfinance/exceptions.py`
- `yfinance/scrapers/quote.py`
- `yfinance/scrapers/history.py`

## Responsibility Map

### Upstream: `data.py`
Owns:

- singleton/shared transport state
- cookie and crumb acquisition
- consent handling
- request retries
- in-process caching
- rate-limit failure propagation

### Port: `YFinance.NET.Transport`
Should own:

- shared `HttpClient` + cookie container
- crumb lifecycle
- retry/backoff policy
- request pacing
- HTTP/cache boundary
- low-level endpoint helpers

### Upstream: `base.py`
Owns:

- `TickerBase`
- lazy composition of scrapers
- timezone lookup
- public method surface routing

### Port: `YFinance.NET.Core`
Should own:

- `Ticker`
- shared lazy service references
- ticker identity normalization
- minimal public object surface

### Upstream: `ticker.py`
Owns:

- the concrete public `Ticker` object
- convenience properties and specialized per-ticker methods

### Port: `YFinance.NET.Api`
Should own:

- public `Ticker`
- public `Tickers`
- first-pass `InfoAsync`, `QuoteAsync`, and `HistoryAsync`

### Upstream: `tickers.py`
Owns:

- collection wrapper
- bulk convenience access
- history/download delegation
- websocket convenience wrapper

### Port: `YFinance.NET.Api`
Should own:

- `Tickers`
- collection-oriented quote/info/history helpers
- later websocket subscription helper

### Upstream: `scrapers/quote.py`
Owns:

- `Ticker.info`
- `FastInfo`
- quote summary module aggregation
- quote endpoint enrichment

### Port: `YFinance.NET.Features.Quote`
Should own:

- quote summary fetch + normalization
- lightweight fast quote projection for refresh loops
- eventual `FastInfo`-like view

### Upstream: `scrapers/history.py`
Owns:

- chart endpoint history retrieval
- period/range/start/end normalization
- metadata retention
- repair-related branches

### Port: `YFinance.NET.Features.History`
Should own:

- chart endpoint requests
- range/period conversion
- history metadata parsing
- minimal first-pass price series model

### Upstream: `live.py`
Owns:

- websocket streaming
- subscribe/unsubscribe
- protobuf decode

### Port: `YFinance.NET.Features.Streaming`
Should own:

- optional later websocket client
- kept separate from first delivery

### Upstream: `cache.py`
Owns:

- persistent caches for selected data categories

### Port: `YFinance.NET.Caching`
Should own:

- first-pass in-memory TTL caches
- optional persistent cache later

## First Delivery Scope
Only the minimum necessary for the top-100 VM exerciser:

1. config/options
2. exceptions
3. rate limiter
4. in-memory TTL cache
5. Yahoo session manager
6. crumb/cookie bootstrap
7. quote endpoint support
8. quote summary support for `marketCap` and related fields
9. simple `Ticker`
10. simple `Tickers`
11. standalone console exerciser

## Explicitly Deferred
These should not block the first delivery:

- full `download()` parity
- dataframe-equivalent APIs
- options chain
- fundamentals breadth parity
- price repair
- persistent sqlite caches
- websocket streaming
- search/lookup/domain/screener layers

## Suggested .NET Project Layout

- `YFinance.NET`
  - `Config/`
  - `Exceptions/`
  - `Transport/`
  - `Caching/`
  - `Models/`
  - `Features/Quote/`
  - `Features/History/`
  - `Api/`

- `YFinance.NET.Exerciser`
  - standalone console proof client

## Sync-Friendly Design Rules

1. Keep endpoint-specific code isolated by feature.
2. Keep request transport logic out of public `Ticker` objects.
3. Keep normalization/parsing logic separate from raw HTTP calls.
4. Record upstream source module inspiration in code comments sparingly where helpful.
5. Add a small mapping document whenever a new upstream capability is ported.
6. When upstream behavior is intentionally simplified or deferred, record that explicitly in this plan or a nearby capability note.
7. Prefer additive wrappers around upstream-shaped behavior over reshaping the core API too early for local convenience.
8. Treat the standalone exerciser as the first compatibility proof surface before any Portfolio Visualizer integration changes.

## VM Validation Plan

The proving cycle happens on the VM, not just locally:

1. restore/build the standalone `YFinance.net` solution,
2. run the exerciser on the VM,
3. verify cache warmup behavior,
4. verify 1-second pacing for one-by-one work,
5. verify 5-minute interval loops,
6. verify at least 5 cycles total for acceptance, then longer soak cycles for confidence,
7. verify no 429s.

## Current Status

- The standalone `YFinance.NET` library and `YFinance.NET.Exerciser` are implemented and build cleanly.
- The runtime integration lane in `PortfolioSaver.Data` now uses `YFinance.NET` for quotes, history, and symbol metadata.
- VM proofs completed at top 20, top 50, top 100 short-cycle, top 100 five-cycle, and top 100 twenty-five-cycle scales.
- The longest proof is a 25-cycle, 5-minute, top-100 VM soak with 20 warmed history lanes, 1-second one-by-one pacing, 10-minute cache ceilings, and no `429`, `RateLimit`, `FAIL`, or `missing` log entries.
- Remaining follow-on work is ordinary runtime evolution and future upstream-sync maintenance, not first-proof uncertainty.
