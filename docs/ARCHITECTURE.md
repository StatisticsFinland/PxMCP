# Architecture

## Solution Structure

```
PxMCP.slnx
├── PxMCP/                  # MCP server application
├── PxMCP.Tests/            # Unit tests (NUnit + Moq)
├── PxMCP.McpTests/         # MCP protocol-level tests with in-process stream transport
├── PxMCP.IntegrationTests/ # HTTP application integration tests with mocked PX API
└── PxMCP.Inspector/        # MCP Inspector (Node.js dev tool)
```

## PxMCP (Server)

### Entry Point

`Program.cs` — configures DI, HTTP client, and MCP server:

- Binds `PxApiOptions` from the `"PxApi"` config section
- Validates `PxApi:BaseUrl` at application creation; it must be an absolute HTTP(S) URL without user information, query, or fragment
- Binds `McpSecurityOptions` from the `"McpSecurity"` section into an immutable `McpRequestPolicy` singleton; invalid hosts or origins fail at application creation
- Registers `PxApiClient` as a typed `HttpClient` via `AddHttpClient<IPxApiClient, PxApiClient>`, with built-in request logging removed because URL paths contain caller-supplied database/table IDs
- Validates `PxApi:TimeoutSeconds` and `PxApi:SearchTimeoutSeconds` (greater than 0, at most 300) and the optional `PxApi:MaxConnectionsPerServer` (at least 1 when set) at application creation
- Configures one long-lived `SocketsHttpHandler` for the PX API client:
  - Optional `MaxConnectionsPerServer` from configuration; when set, excess requests wait for a pooled connection instead of opening new ones (for hosts with a limited outbound connection or port budget). When omitted, connections are not limited (.NET default).
  - `PooledConnectionIdleTimeout` of 90 s, below common NAT/load-balancer idle timeouts
  - `PooledConnectionLifetime` of 10 minutes so DNS changes are picked up; handler rotation is disabled with `SetHandlerLifetime(Timeout.InfiniteTimeSpan)`
  - HTTP/2 preferred (`DefaultRequestVersion` 2.0, `RequestVersionOrLower`; `PxApiClient` copies these onto every request message because `SendAsync` does not apply them to caller-constructed messages), falling back to HTTP/1.1 when the server or an `http://` URL does not support it; a single HTTP/2 connection multiplexes concurrent requests
  - `MaxResponseContentBufferSize` of 16 MB; a response at PX API's default 50,000-cell limit is a few MB
  - `HttpClient.Timeout` set to the larger configured timeout as a backstop; `PxApiClient` enforces the per-endpoint timeouts
  - No automatic retries
- Registers ASP.NET Core health checks with `PxApiHealthCheck` to verify PX API connectivity
- Adds MCP server with stateless Streamable HTTP transport and auto-discovered tools
- Adds `McpRequestValidationMiddleware`, which rejects `/mcp` requests with a disallowed `Host` or present-but-disallowed `Origin` header with HTTP 403 (DNS-rebinding and cross-origin protection required by the MCP Streamable HTTP transport). Defaults allow only loopback hosts; requests without `Origin` and loopback origins are accepted. Rejections are logged with the header name only, never its value. There is no authentication. The same middleware rejects `/mcp` requests whose declared `Content-Length` exceeds 1 MB (`MaxRequestBodyBytes`) with HTTP 413, and lowers `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` to 1 MB so the server (Kestrel) also enforces the limit for bodies without a declared length. A declared length over the limit is rejected before the concurrency limiter, so those requests never take a slot. Bodies without a declared length (chunked) can only be cut off while the endpoint reads them, after a slot has been taken; Kestrel then responds 413 (`BadHttpRequestException` while the MCP SDK reads the body) and the slot is released; a loopback Kestrel test verifies this through the production pipeline, since `TestServer` does not enforce this feature.
- Maps the health endpoint at `/health` via `app.MapHealthChecks()` (not subject to Host/Origin validation or the concurrency limit)
- Adds ASP.NET Core rate limiting (`UseRateLimiter`, after Host/Origin validation so rejected requests never take a slot) with a `ConcurrencyLimiter` policy applied only to `/mcp`: `McpLimits:MaxConcurrentRequests` (default 64, at least 1) requests run at once and up to `McpLimits:QueueLimit` (default 32, 0 or greater) wait oldest-first. Further requests get HTTP 503 with `Retry-After: 1` and a short `text/plain` body; the rejection is logged at Debug level (to avoid log floods under overload) without request data. Each tool call makes at most one PX API request at a time, so this also bounds PX API requests from tool calls; `/health` probes call the PX API outside this limit. Invalid limits fail at application creation.
- Maps the MCP Streamable HTTP endpoint at `/mcp` via `app.MapMcp(McpRequestValidationMiddleware.McpPath).RequireRateLimiting(...)`
- The configuration template binds `Urls` to `http://localhost:5277` so the server listens on loopback only unless explicitly reconfigured

### Folder Layout

```
PxMCP/
├── Configuration/
│   ├── McpLimitsOptions.cs      # Config POCO (MaxConcurrentRequests, QueueLimit for /mcp)
│   ├── McpSecurityOptions.cs    # Config POCO (AllowedHosts, AllowedOrigins for /mcp)
│   └── PxApiOptions.cs          # Config POCO (BaseUrl)
├── Models/                       # DTOs matching PX API OpenAPI schemas
│   ├── JsonStat2.cs             # JSON-stat 2.0 dataset (core data format)
│   ├── Dimension.cs, Category.cs, Unit.cs
│   ├── Filter.cs                # Typed polymorphic data query filters
│   ├── DatabaseListingItem.cs
│   ├── TableListingItem.cs, TableSummary.cs, MetricInfo.cs, TimeRange.cs, DimensionInfo.cs
│   └── Tools/Metadata: DimensionValuesResult.cs, HeadMetadataResult.cs, DimensionOverview.cs, SampleValue.cs
│   ├── PagedTableList.cs, PagingInfo.cs
│   ├── PxApiSearchResponse.cs, PxApiSearchResultItem.cs, SearchQueryInfo.cs
│   ├── SearchDatabaseRef.cs, MatchInfo.cs
│   ├── Link.cs
│   └── Enums: SearchTarget, MatchType
├── Services/
│   ├── IPxApiClient.cs          # Interface for testability
│   ├── PxApiClient.cs           # Typed HttpClient wrapping the PX API
│   ├── PxApiArgumentValidator.cs # Database/table/lang validation before URL construction
│   ├── PxApiArgumentException.cs # Caller-safe rejection of invalid PX API arguments
│   ├── McpRequestPolicy.cs      # Validated Host/Origin allow-lists for /mcp
│   ├── McpRequestValidationMiddleware.cs # 403 for disallowed Host/Origin, 413 for bodies over 1 MB on /mcp
│   ├── PxApiHealthCheck.cs      # Health check verifying PX API connectivity
│   └── QueryValidator.cs        # Filter validation against table metadata
├── Tools/                        # MCP tool definitions
│   ├── DatabaseTools.cs         # list_databases
│   ├── TableTools.cs            # list_tables
│   ├── SearchTools.cs           # search (compact result projection)
│   ├── MetadataTools.cs         # get_head_metadata, get_table_metadata, get_dimension_values
│   ├── DataTools.cs             # get_data, validate_query
│   └── JsonContext.cs           # Source-generated JsonSerializerContext
└── Program.cs
```

### Key Packages

| Package                           | Version | Purpose                            |
| --------------------------------- | ------- | ---------------------------------- |
| `ModelContextProtocol.AspNetCore` | 1.2.0   | MCP server SDK with HTTP transport |

### Data Flow

```
MCP Client (LLM agent)
    │
    ▼ MCP protocol (JSON-RPC over Streamable HTTP)
┌──────────────────────────────┐
│  PxMCP Server                │
│  ┌────────────────────────┐  │
│  │  Tools (McpServerTool) │  │  ← Tool classes injected with IPxApiClient
│  └──────────┬─────────────┘  │
│             │                 │
│  ┌──────────▼─────────────┐  │
│  │  PxApiClient           │  │  ← Typed HttpClient, URL construction, error handling
│  └──────────┬─────────────┘  │
└─────────────┼────────────────┘
              │ HTTP (JSON)
              ▼
        PX API Backend
```

### Transport

Stateless Streamable HTTP (`WithHttpTransport(o => o.Stateless = true)`). Each request is independent — no session tracking, horizontally scalable.

### Tools

All tools are attribute-discovered (`[McpServerToolType]` on class, `[McpServerTool]` on method). They receive `IPxApiClient` via constructor injection.

All tools use `UseStructuredContent = true` on the `[McpServerTool]` attribute, which causes the SDK to generate an output schema from the return type and populate `CallToolResult.StructuredContent` with a typed JSON object.

| Tool                   | Class           | Method               | Return Type             | PX API Endpoints Used                           |
| ---------------------- | --------------- | -------------------- | ----------------------- | ----------------------------------------------- |
| `list_databases`       | `DatabaseTools` | `ListDatabases`      | `DatabaseListResult`    | `GET /meta/databases`                           |
| `list_tables`          | `TableTools`    | `ListTables`         | `PagedTableList`        | `GET /meta/databases/{db}/tables`               |
| `search`               | `SearchTools`   | `Search`             | `SearchResult`          | `GET /meta/search`, `.../databases/{db}/search` |
| `get_head_metadata`    | `MetadataTools` | `GetHeadMetadata`    | `HeadMetadataResult`    | `GET /meta/databases/{db}/tables/{t}`           |
| `get_table_metadata`   | `MetadataTools` | `GetTableMetadata`   | `JsonStat2`             | `GET /meta/databases/{db}/tables/{t}`           |
| `get_dimension_values` | `MetadataTools` | `GetDimensionValues` | `DimensionValuesResult` | `GET /meta/databases/{db}/tables/{t}`           |
| `get_data`             | `DataTools`     | `GetData`            | `JsonStat2`             | `POST /data/databases/{db}/tables/{t}`          |
| `validate_query`       | `DataTools`     | `ValidateQuery`      | `ValidationResult`      | Metadata GET, then `HEAD /data/databases/{db}/tables/{t}` for valid filters |

### Head Metadata

The `get_head_metadata` tool returns a structural overview of a table for query construction. It includes:

- `tableId` — the table identifier
- `dimensions` — all dimensions (including metric), each with:
  - `id` — exact dimension ID (case-sensitive)
  - `label` — human-readable name
  - `role` — one of `time`, `geo`, `metric`, or `other` (mapped from JSON-stat roles)
  - `valueCount` — total number of categories
  - `sampleCodes` — up to 10 example `{ code, label }` pairs

Agents should use: `search` → `get_head_metadata` → `get_dimension_values` → `validate_query` → `get_data`.

`validate_query` checks dimension IDs, exact codes/boundaries and positional counts against metadata. For valid filters it then reads the current positive `X-Max-Cells` from the data endpoint with HEAD, without requesting data. `QueryValidator.CheckSize` multiplies exact per-dimension selection counts and compares them with this limit. A count above the limit adds an error and makes `isValid` false; equality is allowed.

The result includes `selectedCellCount`, `maxCells` and `exceedsMaxCells`, explicitly null when unknown. Counts require explicit filters for every dimension: unique exact Code values, literal inclusive From/To boundaries in category-index order, or positive First/Last counts within available values. Omitted/default selections, wildcards, empty/duplicate code selections, oversized positional counts and 64-bit multiplication overflow produce an unknown count with a warning, not a guessed safe result. Invalid filters skip HEAD. A failed HEAD request, missing/invalid/ambiguous header, or upstream timeout adds a size-check-unavailable warning while preserving filter validation and any exact count. Caller cancellation still propagates. `isValid: true` is not a guarantee of upstream success, especially when size information is unknown.

`get_data` retains metadata-only filter validation and its original POST: it does not call HEAD or enforce the preflight size result, and never modifies or truncates the selection.

The `TableSummary` type is still used by `list_tables` and `search` results for listing-level information.

### Filter Model

`Filter` is an abstract base class with five concrete subtypes, discriminated by `[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]`:

| Subtype       | `type` value | `query` type   | Example                                   |
| ------------- | ------------ | -------------- | ----------------------------------------- |
| `CodeFilter`  | `"Code"`     | `List<string>` | `{"type":"Code","query":["SSS","KU049"]}` |
| `FromFilter`  | `"From"`     | `string`       | `{"type":"From","query":"2020"}`          |
| `ToFilter`    | `"To"`       | `string`       | `{"type":"To","query":"2023"}`            |
| `FirstFilter` | `"First"`    | `int`          | `{"type":"First","query":5}`              |
| `LastFilter`  | `"Last"`     | `int`          | `{"type":"Last","query":5}`               |

This produces a discriminated `anyOf` / `oneOf` in the MCP tool schema, so clients see exactly which filter variants are valid and what `query` types each expects.

The `get_data` tool accepts `Dictionary<string, Filter>?` directly (not a string), so the MCP schema exposes filters as a proper JSON object.

Tool registration passes a copy of `McpJsonUtilities.DefaultOptions` with `AllowOutOfOrderMetadataProperties = true` to `WithToolsFromAssembly`. This allows `type` before or after `query` without changing the discriminator names, query types, or generated schema. The protocol test fixture uses the same options. This setting belongs to `JsonSerializerOptions`, not `JsonPolymorphicAttribute`.

### Serialization

`JsonContext` declares source-generated serialization metadata for filters, but the current `PxApiClient` request-body serialization uses its own `JsonSerializerOptions`, not this context. Tool response serialization is handled automatically by the MCP SDK when `UseStructuredContent = true` is set. Tool-specific response DTOs (`DimensionValuesResult`, `DatabaseListResult`) are defined under `Models/Tools/`.

The `search` tool projects upstream `PxApiSearchResponse` into `SearchResult`: results retain database, table summary and score, plus envelope paging information; upstream query echo, match details and links are not returned. JSON-stat `source` and `updated` are required but nullable; explicit per-property serialization settings preserve them as JSON null instead of allowing the SDK's default null omission to violate the output schema.

### Error Handling

- Every `PxApiClient` method validates caller-supplied URL arguments with `PxApiArgumentValidator` before building a URL. Rules mirror PX API's own identifier validation: database IDs are 1-50 letters or digits; table IDs are 1-50 letters, digits, `_` or `-`; `lang` is omitted (null/empty) or a short language tag such as `en` or `sv-FI`; search queries must be non-blank and at most 400 characters, matching PX API's search limit. This rejects `.`/`..` dot segments, which `Uri.EscapeDataString` leaves unescaped and `HttpClient` would otherwise resolve against the base address, redirecting requests to other PX API endpoints. Rejections throw `PxApiArgumentException` with a fixed message that never echoes the rejected value; tools convert it to `McpException` alongside `HttpRequestException`, and no upstream request is sent.
- `PxApiClient.EnsureSuccessAsync` throws `HttpRequestException` with the HTTP status code without including the upstream response body. Data-query HTTP 413 errors include guidance to narrow the selection or apply filters and the positive `X-Max-Cells` value from that response when valid. HEAD and 413 use the same strict positive 64-bit header parser, with no hard-coded fallback.
- Successful responses containing malformed JSON or JSON `null` produce an explicit `HttpRequestException` rather than a null result.
- HTTP 206 responses are rejected explicitly: the client does not assemble partial responses and must not expose them as complete lists or datasets.
- Each request, including buffering its response body, is limited by a linked `CancellationTokenSource`: `PxApi:SearchTimeoutSeconds` (default 15 s) for global and database search, and `PxApi:TimeoutSeconds` (default 3 s) for every other request, including the HEAD size check and data POST. A timeout — from this per-request limit or from `HttpClient.Timeout` (inner `TimeoutException`) — when the caller token is not canceled becomes a payload-safe `HttpRequestException` with retry/narrowing guidance. Caller cancellation remains cancellation. There are no retries.
- A response body larger than `MaxResponseContentBufferSize` (`HttpRequestError.ConfigurationLimitExceeded`) becomes a payload-safe `HttpRequestException` asking the caller to narrow the query, instead of the generic transport message.
- Transport `HttpRequestException`s from sending or buffering GET/POST responses receive a fixed retry-later message, without forwarding host/request diagnostics to MCP clients. The original exception, `HttpRequestError`, and status code are retained for diagnostics; HTTP status and JSON parsing errors retain their separate handling.
- Tools that need to report errors visible to the LLM throw `McpException` (from `ModelContextProtocol` namespace), which the MCP SDK translates to an error response with `isError: true`.
- `ToolErrorFilter` catches JSON binding errors, including `System.Text.Json` missing-discriminator `NotSupportedException`s, and returns actionable guidance without exposing internal type names. Its warning logs retain the exception category and original stack trace in a diagnostic exception, but deliberately omit original messages, inner exceptions and JSON paths, which can echo caller input. This describes the application filter's logs, not redaction of all SDK or HTTP-client logs.
- Upstream malformed, null or wrongly typed JSON is wrapped by `PxApiClient` before reaching tools, so it remains an upstream tool error rather than a binding error. HTTP/MCP tests exercise this path through the real API client for listing, data and metadata-dependent validation.
- Before invoking a tool, the filter rejects explicit nulls for required parameters declared with a non-nullable single JSON type in the tool's input schema. The SDK otherwise permits null reference arguments through binding. Unknown top-level arguments retain the SDK's ignore behavior; unknown tool names remain JSON-RPC invalid-params errors.

## PxMCP.Tests (Unit Tests)

Tests tool logic and API client in isolation using mocked dependencies.

```
PxMCP.Tests/
├── Services/
│   ├── PxApiClientTests.cs      # URL construction, headers, POST body, HEAD size limit, error handling, argument rejection, per-endpoint timeouts, response size limit, HTTP version per request
│   ├── PxApiArgumentValidatorTests.cs # Accepted/rejected database, table, language and search query formats
│   ├── McpRequestValidationMiddlewareTests.cs # Body size limit applied to the server feature on /mcp only
│   ├── McpRequestPolicyTests.cs # Host/Origin allow-list defaults, configuration and validation
│   ├── PxApiHealthCheckTests.cs # Cancellation propagation
│   ├── QuerySizeTests.cs        # Preflight cell counts, unknown counts, overflow, linear-time large code selections
│   └── QueryValidatorTests.cs   # Filter validation against metadata
└── Tools/
    ├── DatabaseToolsTests.cs
    ├── TableToolsTests.cs
    ├── SearchToolsTests.cs       # Routing, passthrough verification
    ├── MetadataToolsTests.cs     # Summary formatting, dimension extraction
    └── DataToolsTests.cs         # Filter types, validation, lang passthrough
```

- **Framework:** NUnit 4.3.2, Moq 4.20.72
- **Pattern:** Each test class mocks `IPxApiClient` (or `HttpMessageHandler` for `PxApiClientTests`) and tests one tool class
- `InternalsVisibleTo("PxMCP.Tests")` allows testing `internal static` methods like `MetadataTools.BuildDimensionInfo`

## PxMCP.McpTests (Protocol Tests)

Tests the full MCP protocol stack — tool discovery, JSON schema generation, parameter binding, and tool invocation — using an in-process transport.

```
PxMCP.McpTests/
├── McpTestFixture.cs            # Base class: in-process server+client via Pipe
├── ToolDiscoveryTests.cs        # Tool count, names, descriptions, schemas present
├── ToolSchemaTests.cs           # Parameter types, required/optional, descriptions
├── ToolInvocationTests.cs       # Tool calls, recursive output schemas, text/structured agreement, error paths
└── ToolDocumentationDump.cs     # Diagnostic: dumps all tool schemas to stdout
```

### In-Process Transport

`McpTestFixture` uses `System.IO.Pipelines.Pipe` to connect server and client without networking:

```
McpClient ──write──▶ Pipe A ──read──▶ McpServer (StreamServerTransport)
McpClient ◀──read── Pipe B ◀──write── McpServer
```

The server is hosted via `Host.CreateDefaultBuilder()` with `AddMcpServer().WithStreamServerTransport().WithToolsFromAssembly()`. A mock `IPxApiClient` is registered in DI. This exercises the same code paths as production, including JSON-RPC serialization, tool attribute discovery, and parameter binding.

Response contract tests compare JSON text and structured content for all eight tools and recursively validate representative nonempty results against the actual advertised output schemas using the test-only `JsonSchema.Net` dependency. Each case also corrupts a nested value and verifies rejection, exercising array items and dictionary values rather than just root properties. Fixtures include nullable JSON-stat values/provenance, units, arbitrary extensions, nested table summaries and validation issues. This is representative schema conformance, not exhaustive coverage of every possible response. Separate cases verify that metadata-dependent tools do not request data after metadata retrieval fails.

Stream-transport cancellation tests send an explicit `notifications/cancelled` message with the active request ID and verify that all eight tools cancel their upstream operation, including the size-limit HEAD stage of `validate_query`. They also verify that canceling the metadata stage of a filtered `get_data` call does not start a data request. Canceling the SDK client's local wait alone did not cancel the server operation in these tests; this is distinct from HTTP request-abort behavior, which these stream tests do not cover.

### Running the Documentation Dump

```bash
dotnet test PxMCP.McpTests --filter ToolDocumentationDump -v normal
```

Outputs every tool's name, description, and full JSON schema as an MCP client sees them.

## PxMCP.IntegrationTests (HTTP Application Tests)

Tests the production ASP.NET Core application primarily through ASP.NET Core `TestServer`. The application composition method is shared with production startup; tests configure an in-memory host and replace `IPxApiClient` with a strict Moq mock. These tests exercise dependency injection, HTTP routing, MCP transport, and tool invocation without calling the live PX API.

The project tests `/health`, MCP initialization and tool discovery/invocation at `/mcp`. Keep these distinct from `PxMCP.McpTests`: protocol tests use a stream transport to focus on MCP schemas and binding, while integration tests verify those features through the production HTTP host.

HTTP request-abort tests cancel the client's active POST through `TestServer` and verify cancellation reaches upstream data retrieval, the metadata stage of filtered queries, and the size-limit HEAD stage of query validation. A canceled metadata stage does not start a data request. These are in-process host checks, not validation of a deployed reverse proxy or real network disconnect.

Preflight HTTP/MCP cases delegate metadata GET and data HEAD to the real API client with a mocked HTTP handler, checking exact/equal/over-limit results, unknown counts, unavailable headers and HEAD failures without a data POST. Unit tests cover all five literal filter types, 64-bit overflow, strict header parsing and HEAD timeout/cancellation; protocol tests validate the extended result schema.

Identifier-rejection cases delegate to the real API client with a strict HTTP handler and verify that dot segments, separators, empty database IDs, malformed language codes and blank search queries return an actionable tool error without any upstream request.

`McpRequestValidationTests` also verifies that a body of exactly 1 MB is accepted while one byte more gets 413 (and that a disallowed origin still gets 403 first). It sends requests with explicit `Host` and `Origin` headers through `TestServer` and verifies 200 for loopback/configured values, 403 with an empty body for disallowed values (including DNS-rebinding style host/origin pairs, `null` and multiple origins), that `/health` is unaffected, that a `*` host disables only Host validation, and that invalid configuration fails at startup. A separate loopback Kestrel test runs the production application (`Program.CreateApplication` with a mocked `IPxApiClient`, one concurrency slot and no queue) and sends HTTP/1.1 chunked `application/json` bodies to the real MCP endpoint: an under-limit body reaches the MCP transport (200), a body over the limit gets 413, and a following `list_tables` call succeeds, proving the rejected request released its slot. This exercises body-size enforcement that `TestServer` does not implement.

`PxApiHttpClientConfigurationTests` resolves the production typed-client configuration from DI and verifies base address, backstop timeout, response buffer limit, HTTP version policy, the HTTP version and policy actually carried by GET, POST and HEAD requests from the production client (only the innermost handler is replaced), the primary `SocketsHttpHandler` connection cap, idle timeout and lifetime, the disabled handler rotation, the defaults when not configured (including no connection limit), and that invalid timeout or connection values fail at startup. Per-endpoint timeouts and oversized responses are covered by `PxApiClientTests`.

`McpConcurrencyLimitTests` blocks a mocked `list_tables` upstream call to fill slots deterministically and verifies: 503 with `Retry-After`, `text/plain` content type and body when the limit is full and the queue is 0; that `/health` stays available and Host/Origin validation (403) still runs first; that the slot is released after completion; that a queued request waits without reaching upstream and then succeeds while the overflow request is rejected (order-independent, since either concurrent request may win the queue position); that a full queue rejects the newest request and keeps the earlier queued one (oldest-first order); that the defaults run exactly 64 requests, queue 32 and reject the next; and that invalid limits fail at startup.

The data-fidelity case delegates its mocked API boundary to a real `PxApiClient` backed by a mocked HTTP handler. It compares an independently written upstream JSON fixture with the MCP response to cover deserialization and serialization together, including zero versus null values, synthetic status codes, dimension order, units, notes, provenance, and extensions. The fixture does not establish the meaning of real upstream status codes or validate live statistics.

Run with:

```bash
dotnet test PxMCP.IntegrationTests
```

## PxMCP.Inspector (Dev Tool)

A Node.js project containing the [MCP Inspector](https://github.com/modelcontextprotocol/inspector) — a web-based UI and CLI for interactively testing and debugging the PxMCP server.

Inspector 2.13 requires Node.js >= 22.19.0 and remains independent of the .NET
server. Its web UI and API share port 6274 (no separate proxy on port 6277);
MCP Apps use sandbox port 6275 and app-origin port 6278. The npm scripts invoke
the locked local executable with an explicit web or CLI mode. The existing
Streamable HTTP configuration is passed with `--config` as a read-only session
configuration, not a writable catalog.

```
PxMCP.Inspector/
├── package.json     # Inspector dependency and npm scripts
├── package-lock.json # Reproducible Inspector dependency tree
├── mcp.json         # Server config (Streamable HTTP → http://localhost:5277/mcp)
└── README.md        # Setup and usage instructions
```

### Usage

1. Start PxMCP (`dotnet run` from `PxMCP/`)
2. `cd PxMCP.Inspector && npm ci && npm run inspect`
3. Inspector UI opens at `http://localhost:6274`, connected to the local server
