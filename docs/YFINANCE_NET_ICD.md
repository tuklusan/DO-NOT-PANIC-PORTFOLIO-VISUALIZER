# YFinance.NET Client-Server ICD

Document status: Initial revision 0.1  
Applies to ticket: `NB-031`  
Baseline: `BETA-6`

## 1. Purpose
This Interface Control Document (ICD) defines the product-owned wire protocol and lifecycle contract between the PortfolioSaver UI client and the standalone `YFinance.NET` server process.

The protocol is intentionally:
- minimal but sufficient
- client-driven
- easy to debug and trace
- stable across internal Yahoo/YFinance implementation changes
- clean enough to preserve future reimplementation flexibility, including a possible cross-platform C++ server

This ICD is a maintained project artifact and must evolve with implementation, tests, and harness expectations.

## 2. Scope
This ICD covers:
- transport and framing
- message envelope
- request and response patterns
- async event limitations
- error model
- cache semantics
- startup and shutdown sequencing
- client scheduling expectations
- configuration interaction boundaries
- versioning and extensibility rules

This ICD does not define Yahoo upstream payloads. The server owns all Yahoo-specific behavior and normalizes it behind this product protocol.

## 3. Architectural principles
### 3.1 Primary protocol model
The primary protocol model is client-driven request/response.

The UI client:
- initiates requests
- owns pacing and sequencing
- decides what symbol or operation to request next

The server:
- accepts requests
- serves cache-backed responses
- performs Yahoo transport work when required
- normalizes errors and response payloads
- does not become the UI scheduler

### 3.2 Async server messages
Async server messages are limited to server-wide or connection-wide lifecycle events.

They are not used for ordinary market-data flow.

Ordinary lookup failures such as invalid symbol, upstream throttling, or upstream timeout must be returned as request-scoped error responses.

### 3.3 Protocol complexity rule
The protocol must be minimal but sufficient.

It must avoid speculative complexity while still carrying:
- required request types
- normalized payloads
- cache metadata
- server metadata
- explicit error states

### 3.4 Product-owned protocol
The wire protocol is a PortfolioSaver/YFinance.NET protocol, not a Yahoo-compatible or yfinance-compatible wire format.

Upstream Yahoo/yfinance internal changes are absorbed inside the server.

### 3.5 Upstream-sync friendliness
The server boundary must not destroy the maintainability goal of keeping `YFinance.NET` easy to sync conceptually with the user's upstream Python fork.

## 4. Transport
### 4.1 Protocol transport
Transport is raw TCP.

Default server listener:
- port: `14870`
- bind target: accepts connections from any IP address

### 4.2 Concurrency target
The server must support multiple client connections with a maximum of `1024` concurrent clients.

### 4.3 Connection use by the UI client
A single UI client must not open parallel connections to the server for ordinary runtime data retrieval.

The UI should use one active TCP connection for ordinary runtime retrieval.

Within that connection, the client may pipeline multiple in-flight requests using request IDs and must be prepared to process out-of-order responses.

The client still owns pacing. Pipelining exists to remove UI jitter and keep rendering smooth, not to move scheduling responsibility into the server.

## 5. Framing and encoding
### 5.1 Framing
Each message is framed as:
1. 4-byte unsigned length prefix in network byte order (big-endian)
2. UTF-8 encoded JSON payload of that exact length

### 5.2 Encoding
Payload encoding is UTF-8 JSON.

This format is chosen because it is:
- debuggable
- trace-friendly
- easy to implement in .NET
- easy to implement later in C++
- efficient enough for the expected message sizes and update cadence

## 6. Common message envelope
All protocol messages must carry a common envelope. The envelope timestamp must use local time with UTC offset, not a UTC-only `Z` timestamp.

### 6.1 Required common fields
- `protocolVersion` : integer
- `messageType` : string
- `timestamp` : ISO 8601 local timestamp string with UTC offset
- `payloadChecksum` : uppercase SHA-256 checksum of the serialized JSON payload section only

### 6.2 Request/response correlation
Request-scoped messages must also carry:
- `requestId` : caller-generated unique string
- `operation` : string operation name

### 6.3 Message types
Supported top-level message types:
- `request`
- `response`
- `event`

## 7. Message model
### 7.1 Request shape
A request message must follow this shape:

```json
{
  "protocolVersion": 1,
  "messageType": "request",
  "requestId": "req-000001",
  "timestamp": "2026-05-30T16:34:56+04:00",
  "payloadChecksum": "44136FA355B3678A1146AD16F7E8649E94FB4FC21F8DD0F2B3A6D3D4B0716F8A",
  "operation": "get_quote",
  "payload": {}
}
```

### 7.2 Response shape
A response message must follow this shape:

```json
{
  "protocolVersion": 1,
  "messageType": "response",
  "requestId": "req-000001",
  "timestamp": "2026-05-30T16:34:56+04:00",
  "payloadChecksum": "44136FA355B3678A1146AD16F7E8649E94FB4FC21F8DD0F2B3A6D3D4B0716F8A",
  "operation": "get_quote",
  "status": "ok",
  "payload": {}
}
```

### 7.3 Error response shape
A request-scoped error must still be a `response` message:

```json
{
  "protocolVersion": 1,
  "messageType": "response",
  "requestId": "req-000001",
  "timestamp": "2026-05-30T16:34:56+04:00",
  "payloadChecksum": "44136FA355B3678A1146AD16F7E8649E94FB4FC21F8DD0F2B3A6D3D4B0716F8A",
  "operation": "get_quote",
  "status": "error",
  "error": {
    "code": "invalid_symbol",
    "message": "Symbol was not recognized by upstream source.",
    "retryable": false
  }
}
```

### 7.4 Async event shape
Async server events are allowed only for connection/server lifecycle conditions:

```json
{
  "protocolVersion": 1,
  "messageType": "event",
  "timestamp": "2026-05-30T16:35:10+04:00",
  "payloadChecksum": "D6C76C72C4C22D78B7B0B9345CEBB89D4B4DFB7B9D28F7F211B8B7D9CA9F725D",
  "eventType": "server_shutting_down",
  "payload": {
    "reason": "service_stop"
  }
}
```

## 8. Initial operations
The initial protocol revision should define only the minimum set of operations needed by the product.

### 8.1 Handshake and control
- `hello`
- `goodbye`
- `health`
- `get_server_status`

### 8.2 Market data
- `get_quote`
- `get_quotes`
- `get_history`
- `get_market_timing`

### 8.3 Optional admin operations
These should be implemented only if needed by harness/admin scenarios:
- `shutdown`
- `flush_cache`

## 9. Operation semantics
### 9.1 `hello`
Purpose:
- negotiate basic compatibility
- identify client type/version
- establish capabilities

Suggested request payload:
- `clientType`
- `clientVersion`
- `machineHash`
- `ownedMode`
- optional `ownerProcessId`

Suggested response payload:
- `serverVersion`
- `protocolVersion`
- `capabilities`
- `listenerPort`
- `mode`

### 9.2 `goodbye`
Purpose:
- allow graceful disconnect
- inform the server that the client is intentionally leaving

The server should respond with `goodbye_ack` semantics using the ordinary response envelope.

### 9.3 `health`
Purpose:
- quick liveness check
- no expensive upstream dependency required

Suggested payload:
- `status`
- `uptimeSeconds`
- `activeConnectionCount`
- `cacheEntryCount`

### 9.4 `get_server_status`
Purpose:
- richer diagnostics for harness/admin use

Suggested payload:
- protocol version
- server version
- owned/standalone mode
- owner process info if applicable
- active client count
- cache stats
- upstream session state summary
- trace file location metadata if appropriate

### 9.5 `get_quote`
Purpose:
- return a normalized quote for one symbol

Suggested request payload:
- `symbol`

Suggested response payload:
- `symbol`
- `last`
- `change`
- `changePercent`
- `previousClose`
- `currency`
- `marketState`
- `exchangeTimezoneName`
- `providerTimestampUtc`
- `fetchTimestampUtc`
- `cache`

### 9.6 `get_quotes`
Purpose:
- return normalized quotes for a small set of symbols in one request when the client intentionally asks for that

Suggested request payload:
- `symbols` : array of strings

Suggested response payload:
- `quotes` : array of normalized quote objects
- `notFoundSymbols` : array of strings if needed
- `cache`

Note: the UI runtime loop may still choose a one-symbol logical cadence for rendering purposes, but the transport is allowed to keep multiple requests in flight on the same connection.

### 9.7 `get_history`
Purpose:
- return normalized history for one symbol

Suggested request payload:
- `symbol`
- `range`
- `interval`

Suggested response payload:
- `symbol`
- `bars`
- `currency`
- `exchangeTimezoneName`
- `metadata`
- `cache`

### 9.8 `get_market_timing`
Purpose:
- return the normalized market timing snapshot derived from Yahoo chart metadata

Suggested request payload:
- `symbol`

Suggested response payload:
- `symbol`
- `exchangeTimezoneName`
- `regularMarketTime`
- `currentTradingPeriod`
- `cache`

## 10. Cache semantics
### 10.1 Server-owned cache
The server owns market-data caching.

The UI does not own throttling or freshness logic for Yahoo-facing retrieval.

### 10.2 Freshness ceiling
Nothing served from cache should be older than `10 minutes` unless a future explicitly defined degraded mode marks the result as stale.

### 10.3 Cache-first behavior
For single or batch ticker requests:
1. check cache first
2. if cache entry is newer than the freshness ceiling, serve from cache
3. otherwise fetch from Yahoo
4. return fresh result
5. store fresh result back in cache

### 10.4 Cache metadata in payloads
Responses should include normalized cache metadata, for example:
- `source` : `cache` or `live`
- `ageSeconds`
- `stale`

## 11. Error model
### 11.1 Request-scoped errors
Ordinary failures must be returned as request-scoped error responses.

Suggested error codes include:
- `invalid_symbol`
- `network_lost`
- `upstream_unavailable`
- `upstream_throttled`
- `timeout`
- `cache_miss`
- `internal_error`
- `unsupported_operation`
- `protocol_error`

### 11.2 Async events reserved for server/connection conditions
Unsolicited `event` messages are reserved for conditions such as:
- `server_shutting_down`
- `server_overloaded`
- `protocol_violation`
- `connection_idle_timeout`
- `server_internal_fault`

If a condition does not affect the connection or server as a whole, it should normally not be an async event.

## 12. Startup sequence
### 12.1 Default product mode
The default product mode is owned mode.

In owned mode:
1. the UI launches the server if it is not already running
2. the UI waits for TCP readiness on port `14870`
3. the UI opens one client connection
4. the UI sends `hello`
5. the server returns a successful response
6. the UI may send `health`
7. the UI begins ordinary client-driven requests, optionally with controlled pipelining on the same connection

### 12.2 Owned mode launch contract
The launch contract should support owner awareness, for example:
- `--owned`
- `--owner-pid <pid>`
- `--port 14870`

The exact CLI can vary, but the concept is required.

## 13. Shutdown sequence
### 13.1 Graceful shutdown
In owned mode, the normal graceful flow is:
1. UI sends `goodbye`
2. server responds successfully
3. UI closes the connection
4. server exits when the owning client session/process is gone

### 13.2 Crash or abnormal UI exit
If the owning UI disappears unexpectedly, the server should detect owner process loss or session expiry and exit in owned mode.

### 13.3 Standalone mode
Standalone mode is also supported for future shared-server use.

In standalone mode, the server is not required to exit when one client disconnects.

## 14. Configuration interaction rules
### 14.1 Client-side configuration behavior
UI configuration changes should normally affect only:
- which symbols are requested
- request sequencing
- request cadence
- visual behavior

### 14.2 Server-side configuration boundary
UI configuration changes should not generally reconfigure server internals.

Server internals such as these remain server-owned concerns:
- cache freshness enforcement
- upstream session handling
- retry/backoff behavior
- listener configuration
- trace behavior
- max client policy

### 14.3 Small admin/control surface
The server may expose a small admin/control surface for harness or maintenance cases, such as:
- `health`
- `get_server_status`
- optional `shutdown`
- optional `flush_cache`

## 15. UI runtime behavior expectations
Once the caching server is functional, the UI should no longer carry Yahoo throttling responsibility.

Expected UI behavior:
1. keep one ordinary runtime connection to the server
2. request macros and global exchanges according to the UI’s chosen logical order
3. allow multiple in-flight requests on that same connection when needed to smooth rendering
4. process responses by `requestId` as they arrive, even if completion order differs from send order
5. render incrementally as results arrive
6. do not open parallel connections from the same UI session for normal runtime requests

## 16. Identity and privacy constraints
Any client identifier sent upstream to Yahoo must:
- be a unique random hash derived from the requesting machine identity
- not include the application name
- not include the author name
- not include the organization name
- not include other human-identifying product branding

## 17. Traceability and diagnostics
The protocol and implementation should be trace-friendly.

Recommended trace points include:
- connection accepted
- hello received and answered
- request send
- request receive
- response send
- response receive
- request-scoped error
- payload checksum failure
- async event sent
- async event receive
- goodbye received
- owned-mode shutdown

The UI and server should maintain separate circular traces.

## 18. Versioning and compatibility
### 18.1 Protocol version
The wire protocol must include `protocolVersion` in every message.

### 18.2 Breaking changes
Breaking wire changes require a protocol version increment and an ICD update.

### 18.3 Backward compatibility
No backward compatibility guarantee is assumed until explicitly declared in a later ICD revision.

## 19. Non-goals for initial revision
The initial revision does not require:
- binary encoding
- Yahoo-shaped wire payloads
- unsolicited market-data pushes
- subscription streaming semantics
- UI-driven mutation of server cache policy
- generalized remote administration beyond minimal health/control needs

## 20. Initial implementation guidance
The first implementation should prioritize:
1. clean TCP framing
2. stable request/response envelope
3. owned-mode lifecycle correctness
4. quote/history/timing/validation coverage
5. cache-first correctness
6. harness integration
7. documentation and trace quality

## 21. ICD maintenance rule
Any implementation change that affects:
- message shapes
- operation names
- lifecycle sequencing
- cache semantics
- error codes
- async event rules
must update this ICD in the same change set or an explicitly paired change.
