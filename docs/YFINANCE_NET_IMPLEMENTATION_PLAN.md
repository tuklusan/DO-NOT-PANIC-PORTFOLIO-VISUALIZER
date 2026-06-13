# YFinance.NET Client-Server Implementation Plan

Document status: Initial revision 0.1  
Primary ticket: `NB-031`  
Companion ICD: [YFINANCE_NET_ICD.md](D:\Users\vagab\Documents\SOFTWARE-DEV\Don't-Panic-Portfolio-Visualizer\docs\YFINANCE_NET_ICD.md)  
Baseline: `BETA-7`

## 1. Objective
Implement the `NB-031` architecture transition from in-process `YFinance.NET` usage to a standalone client-server runtime while preserving current product behavior and keeping the codebase sync-friendly with the upstream Python yfinance concepts.

The implementation should proceed in small, testable layers.

## 2. Outcome definition
The ticket is complete when:
1. `YFinance.NET` runs as its own server process
2. the PortfolioSaver UI consumes market data only through the server protocol
3. the server owns Yahoo transport, cache freshness, and error normalization
4. owned-mode lifecycle works end-to-end in the desktop app and VM harness
5. the ICD and implementation stay aligned
6. obsolete in-process assumptions are removed from the active product path

## 3. Delivery strategy
Build from the center outward:
1. protocol DTOs and framing
2. server host process
3. UI client transport seam
4. lifecycle integration
5. migration cleanup
6. proof and harness/documentation completion

## 4. Project shape
### 4.1 New or repurposed projects
Recommended additions under `YFinance.net`:
1. `YFinance.NET.Protocol`
- shared message contracts, enums, error codes, framing helpers, protocol constants

2. `YFinance.NET.Server`
- standalone executable host
- TCP listener
- request dispatcher
- owned/standalone lifecycle
- dedicated circular trace

3. `YFinance.NET.Client`
- reusable TCP client transport for the UI side
- request/response correlation
- hello/goodbye/health flows

4. retain `YFinance.NET`
- core Yahoo-facing domain and service library
- quote/history/timing/cache/session logic

### 4.2 Why this split
This split keeps responsibilities clean:
- `YFinance.NET` stays the domain/runtime engine
- `Protocol` stabilizes the wire contract
- `Server` owns listener/process concerns
- `Client` gives the desktop app a small, testable transport seam

## 5. Protocol DTO and framing plan
### 5.1 Namespace strategy
Preferred shared namespace under `YFinance.NET.Protocol`:
- `YFinance.NET.Protocol.Messages`
- `YFinance.NET.Protocol.Enums`
- `YFinance.NET.Protocol.Errors`
- `YFinance.NET.Protocol.Transport`

### 5.2 Minimum contract types
Initial DTO set:
1. `ProtocolEnvelope`
2. `RequestEnvelope`
3. `ResponseEnvelope`
4. `EventEnvelope`
5. `ProtocolError`
6. `CacheMetadataDto`
7. operation-specific payload DTOs

### 5.3 Operation DTOs
Initial operations should get explicit request/response payload types:
1. `HelloRequest` / `HelloResponse`
2. `HealthResponse`
3. `ServerStatusResponse`
4. `GetQuoteRequest` / `GetQuoteResponse`
5. `GetQuotesRequest` / `GetQuotesResponse`
6. `GetHistoryRequest` / `GetHistoryResponse`
7. `GetMarketTimingRequest` / `GetMarketTimingResponse`
8. `ValidateSymbolsRequest` / `ValidateSymbolsResponse`
9. `GoodbyeResponse`

### 5.4 Transport helpers
Implement these in `YFinance.NET.Protocol.Transport`:
1. `ProtocolConstants`
2. `LengthPrefixedMessageReader`
3. `LengthPrefixedMessageWriter`
4. `ProtocolJsonSerializer`

### 5.5 Versioning rule
Hard-code initial `protocolVersion = 1` in one place and fail clearly on unsupported versions.

## 6. Server host plan
### 6.1 Executable shape
`YFinance.NET.Server` should be a console/service-style executable with:
- `Program.cs`
- startup options parsing
- listener bootstrap
- request loop
- graceful shutdown handling

### 6.2 Host services
Recommended internal services:
1. `ServerOptions`
2. `ServerLifecycleMode` enum
3. `YFinanceServerHost`
4. `YFinanceConnectionHandler`
5. `YFinanceRequestDispatcher`
6. `YFinanceServerStatusService`
7. `OwnerProcessMonitor`

### 6.3 Listener rules
Initial server requirements:
- bind to TCP `14870`
- allow any IP
- bounded concurrent connections: `1024`
- one ordinary runtime connection per UI session, with support for multiple in-flight requests on that connection

### 6.4 Request dispatcher
The dispatcher should translate protocol operations into calls against the existing `YFinance.NET` library:
- quote service
- history service
- market timing service
- symbol validation path

### 6.5 Error normalization
The server must map internal exceptions into protocol error codes such as:
- `invalid_symbol`
- `network_lost`
- `upstream_unavailable`
- `upstream_throttled`
- `timeout`
- `cache_miss`
- `internal_error`
- `unsupported_operation`
- `protocol_error`

### 6.6 Dedicated server trace
The server keeps its own circular trace separate from the UI trace, continuing the existing split-trace discipline.

## 7. UI client transport seam plan
### 7.1 New client layer
The UI should stop calling the in-process Yahoo-facing library directly.

Instead, create a thin client transport in `PortfolioSaver.Data` or a small adapter assembly that depends on `YFinance.NET.Client`.

### 7.2 Recommended adapter shape
Possible classes:
1. `YFinanceServerClientFactory`
2. `YFinanceServerQuoteProvider`
3. `YFinanceServerHistoryProvider`
4. `YFinanceServerTimingProvider`
5. `YFinanceServerValidationClient`

### 7.3 Replace active seams
Current active seams to migrate from in-process calls to protocol calls:
1. quote retrieval
2. history retrieval
3. market timing retrieval
4. symbol validation
5. profile-resolution paths where they still depend on direct YFinance runtime objects

### 7.4 Request discipline
The UI client should:
- keep a single active connection for ordinary runtime use
- be able to send multiple requests without waiting for earlier responses
- match responses by `requestId`
- render incrementally as completed responses arrive
- keep pacing under client control
- avoid opening parallel runtime connections just to compensate for jitter

No parallel client connections from the same UI runtime in the normal product path.

## 8. Owned-mode lifecycle integration plan
### 8.1 Desktop startup
Desktop app startup sequence:
1. determine whether an owned server instance is already active for this session
2. if not, launch `YFinance.NET.Server` in owned mode
3. wait for TCP readiness
4. open client connection
5. send `hello`
6. optionally send `health`
7. begin ordinary client-driven UI requests with controlled pipelining on the same connection

### 8.2 Desktop shutdown
Desktop app shutdown sequence:
1. send `goodbye`
2. close the TCP connection
3. ensure owned-mode server terminates
4. emit trace evidence for clean shutdown

### 8.3 Crash tolerance
Owned-mode server must terminate when the owner process disappears unexpectedly.

Implement either:
- owner PID monitoring
- or owner lease/heartbeat timeout
- or both if simple enough

### 8.4 Config window interaction
Configuration changes should not generally reconfigure server internals.

Instead:
- config changes remain local to the UI
- UI rebuilds its symbol request plan
- same server connection or a re-established connection continues serving the new sequence

### 8.5 Harness integration
The VM harness must be updated to:
1. launch the desktop app
2. verify the server process launches in owned mode
3. capture server trace artifacts as well as UI traces
4. verify clean server termination on desktop exit

## 9. Implementation phases
### Phase 1: protocol package
Deliver:
- `YFinance.NET.Protocol`
- framing helpers
- initial DTOs
- serializer tests

Acceptance:
- messages can round-trip in tests
- invalid payloads fail cleanly
- protocol version handling is explicit

### Phase 2: server skeleton
Deliver:
- `YFinance.NET.Server`
- hello/health/goodbye
- TCP listener
- dedicated trace
- owned/standalone mode options

Acceptance:
- server starts
- client can handshake
- owned-mode server exits on owner loss or goodbye flow

### Phase 3: quote/timing/history operations
Deliver:
- request dispatcher using existing `YFinance.NET` services
- quote, history, timing, validation operations
- error-code mapping

Acceptance:
- request/response coverage for all initial operations
- cache metadata is returned
- error normalization is deterministic

### Phase 4: UI client integration
Deliver:
- desktop-side transport seam
- replace active in-process quote/history/timing/validation calls
- maintain client-owned pacing while enabling pipelined in-flight work to reduce UI jitter

Acceptance:
- desktop app still behaves correctly using only protocol-backed data access
- no direct Yahoo market-data calls remain outside `YFinance.NET`

### Phase 5: lifecycle and harness integration
Deliver:
- owned-mode desktop startup/shutdown wiring
- harness start/stop verification
- server trace capture in artifacts

Acceptance:
- canonical VM proof shows server lifecycle correctness
- no orphan server process remains after desktop exit

### Phase 6: cleanup and closeout
Deliver:
- remove obsolete in-process assumptions from active runtime path
- update docs and test plans
- reconcile ICD and implementation

Acceptance:
- `NB-031` closure evidence is complete

## 10. Test strategy
### 10.1 Unit tests
Add unit coverage for:
1. framing read/write
2. serializer round-trip
3. protocol version checks
4. request dispatcher success paths
5. request dispatcher error mappings
6. owned-mode lifecycle helpers

### 10.2 Integration tests
Add integration tests for:
1. server process boot and hello flow
2. quote request to real in-process domain layer through the server boundary
3. history request flow
4. timing request flow
5. validate-symbols flow
6. graceful goodbye shutdown
7. out-of-order response handling for multiple in-flight requests on one connection

### 10.3 Harness and VM tests
Extend harness to prove:
1. desktop launches server
2. server accepts UI client connection
3. UI populates values through the server path
4. server trace is captured
5. server exits when desktop exits in owned mode

## 11. Cleanup targets during migration
As `NB-031` proceeds, watch for and remove:
1. direct in-process runtime assumptions in desktop startup
2. any remaining app-side throttling assumptions tied to Yahoo transport
3. server-irrelevant UI/provider abstractions left over from earlier architectures
4. duplicate lifecycle code between harness and desktop startup

## 12. Initial file targets
Likely new files:
- `YFinance.net\\YFinance.NET.Protocol\\...`
- `YFinance.net\\YFinance.NET.Server\\...`
- `YFinance.net\\YFinance.NET.Client\\...`
- `docs\\YFINANCE_NET_ICD.md`
- `docs\\YFINANCE_NET_IMPLEMENTATION_PLAN.md`

Likely modified areas:
- `src\\PortfolioSaver.Data\\...`
- `src\\PortfolioSaver.Presentation\\...`
- `src\\PortfolioSaver.Settings\\...` if validation transport is affected
- `build\\vm\\...`
- tests and harness docs

## 13. Design constraints to preserve throughout implementation
1. client-driven request/response remains primary
2. async server messages stay limited to server/connection lifecycle
3. protocol stays framed JSON, not binary
4. protocol stays product-owned, not Yahoo-shaped
5. server remains cache owner
6. UI keeps one ordinary runtime connection but may pipeline multiple in-flight requests
7. ICD stays current with implementation

## 14. Immediate next coding step
The first code step should be:
1. create `YFinance.NET.Protocol`
2. define the common envelope types
3. implement length-prefixed framing helpers
4. add serialization round-trip tests including local-time-plus-offset envelope timestamps and payload checksums

That gives the rest of `NB-031` a stable foundation.

