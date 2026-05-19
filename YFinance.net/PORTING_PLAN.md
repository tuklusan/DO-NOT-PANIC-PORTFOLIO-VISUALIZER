# YFinance.NET Porting Plan

## Goal
Build a standalone .NET 10 port of the core `tuklusan/yfinance` logic inside `YFinance.net`, independent of the existing Yahoo code in the main Portfolio Visualizer app.

The first proving target is a VM-run console exerciser that:

1. determines the top 100 S&P 500 symbols by market cap with caching,
2. refreshes them every 5 minutes,
3. repeats for 5 total cycles,
4. keeps one-by-one symbol work paced at 0.5 seconds where batching is not used,
5. completes without Yahoo 429s.

## Upstream-Alignment Rule
This port should be structured so future syncs from the user's fork of `tuklusan/yfinance` can be rolled into `YFinance.NET` deliberately.

That means:

- preserve upstream responsibility boundaries where practical,
- keep Python-module-to-.NET-component mapping explicit,
- avoid burying Yahoo-specific logic inside unrelated app code,
- keep the standalone exerciser as the first proof harness before any Portfolio Visualizer integration.

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

## VM Validation Plan

The first proving cycle should happen on the VM, not just locally:

1. restore/build the standalone `YFinance.net` solution,
2. run the exerciser on the VM,
3. verify cache warmup behavior,
4. verify 0.5-second pacing for one-by-one work,
5. verify 5-minute interval loop,
6. verify 5 cycles total,
7. verify no 429s.

## Immediate Next Step
Scaffold the standalone `YFinance.NET` library and `YFinance.NET.Exerciser` console app around this responsibility map before implementing the first transport/session classes.
