# PxMCP

> **Work in progress:** This application is under active development and is not yet a finished product. Features and interfaces may change.

An MCP (Model Context Protocol) server that exposes statistical data from a PX API as tools for LLM agents. Built with .NET 10 and the `ModelContextProtocol.AspNetCore` SDK.

The application version is **0.1.0**, defined by the `Version` property in
[`PxMCP.csproj`](../PxMCP/PxMCP.csproj).

## Compatibility

PxMCP is developed and tested against the [Statistics Finland PxApi Beta](https://api.stat.fi/docs/?urls.primaryName=PxApi+Beta) (`https://api.stat.fi/px/beta`). Other PX or PxWeb API implementations may use different endpoints and response schemas and are not supported.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Network access to the PX API, or a running PX API instance

## Quick Start

```bash
cp PxMCP/appsettings.Template.json PxMCP/appsettings.json
cd PxMCP
dotnet run
```

The server listens on `http://localhost:5277`, with the MCP endpoint at `/mcp`. `appsettings.json` and environment-specific `appsettings.*.json` files are ignored by Git; only the template is tracked.

## Configuration

The application requires a `PxApi` configuration section. Set the PX API base URL in `PxMCP/appsettings.json` (created from the template above):

```json
{
  "PxApi": {
    "BaseUrl": "https://api.stat.fi/px/beta"
  }
}
```

The base URL must include the PX API prefix (for the public Beta API, `/px/beta`). It must be an absolute HTTP or HTTPS URL without user information, query, or fragment.

Or via environment variable: `PxApi__BaseUrl=https://api.stat.fi/px/beta`

### Upstream limits

```json
{
  "PxApi": {
    "TimeoutSeconds": 3,
    "SearchTimeoutSeconds": 15,
    "MaxConnectionsPerServer": 64
  }
}
```

| Setting | Default | Notes |
| --- | --- | --- |
| `TimeoutSeconds` | 3 | Per-request timeout for every PX API call except search, including reading the response. Greater than 0, at most 300. |
| `SearchTimeoutSeconds` | 15 | Per-request timeout for search. Greater than 0, at most 300. |
| `MaxConnectionsPerServer` | not set (no limit) | Optional cap on simultaneous connections to the PX API per instance. When set, extra requests wait for a free connection. At least 1 when set; omit it for no limit. |

Timed-out requests return a tool error and are not retried. PX API responses larger than 16 MB are rejected with a tool error asking to narrow the query. HTTP/2 is used when the PX API supports it, which lets one connection carry many concurrent requests; otherwise HTTP/1.1 is used.

#### Deployment: outbound connection limits

If your host or network limits outbound connections or ports (for example through source NAT), set `MaxConnectionsPerServer` well below that limit, since a closed connection's port can stay reserved for a while. The template uses 64 as an example. Idle pooled connections are closed after 90 seconds, below common NAT idle timeouts.

### Network exposure

The server has no authentication. It is protected against DNS rebinding and cross-origin browser requests instead:

- **Loopback binding.** The template binds `Urls` to `http://localhost:5277` (loopback only), and ASP.NET Core's default without configuration is `http://localhost:5000`. Do not bind to `0.0.0.0`, `*` or `+` unless the server sits behind a trusted reverse proxy or network boundary.
- **Host and Origin validation on `/mcp`.** Requests whose `Host` header is not allowed, or whose `Origin` header is present but not allowed, are rejected with `403 Forbidden` before reaching the MCP transport. `/health` is not affected, so orchestrator probes can use any host name.

```json
{
  "McpSecurity": {
    "AllowedHosts": [],
    "AllowedOrigins": []
  }
}
```

| Setting | Default (empty) | Notes |
| --- | --- | --- |
| `AllowedHosts` | `localhost`, `127.0.0.1`, `[::1]` | Host names without ports, case-insensitive. Configured values replace the defaults. A single `"*"` disables Host validation (only behind a proxy that validates hosts). |
| `AllowedOrigins` | none | Exact `scheme://host[:port]` origins added to the always-allowed loopback origins. Requests without `Origin` (non-browser MCP clients) are always allowed. |

Invalid entries stop the application at startup. When deploying under a public host name, set `AllowedHosts` to that name, for example `McpSecurity__AllowedHosts__0=pxmcp.example.org`.

### Request limits

Concurrent requests to `/mcp` are limited per instance. When all slots are busy, requests wait in a queue (oldest first); when the queue is also full, the newest request gets `503 Service Unavailable` with `Retry-After: 1` and the plain-text body `Server is busy. Retry later.`. Rejections are logged at Debug level to avoid flooding logs during overload. `/health` is not limited.

```json
{
  "McpLimits": {
    "MaxConcurrentRequests": 64,
    "QueueLimit": 32
  }
}
```

| Setting | Default | Notes |
| --- | --- | --- |
| `MaxConcurrentRequests` | 64 | Requests processed at the same time. At least 1. |
| `QueueLimit` | 32 | Requests waiting for a free slot. 0 or greater; 0 rejects immediately when all slots are busy. |

Requests that declare a body larger than 1 MB (`Content-Length`) are rejected with `413 Payload Too Large` before they take a slot. Bodies sent without a declared length (chunked) are cut off at 1 MB by the server while being read, after the request has taken a slot; the client gets `413` and the slot is released. A large realistic filter is a few tens of KB.

Each tool call makes at most one PX API request at a time, so `MaxConcurrentRequests` also bounds concurrent PX API requests from tool calls. `/health` probes call the PX API outside this limit, so allow for them when sizing `PxApi:MaxConnectionsPerServer`. Invalid values stop the application at startup.

## Logging

The PX API typed client disables built-in `HttpClient` request logging: .NET redacts query strings by default, but URL paths still contain caller-supplied database/table IDs. This removes its automatic request/response timing logs, not tool error responses or health-check error logs.

Keep production logging at Information or higher and restrict access to diagnostic logs. SDK Trace logging can include protocol payloads in some transport paths; exception chains retained for diagnostics can also contain upstream details. The binding filter's sanitized warnings are not a global redaction guarantee. Review any added HTTP logging middleware, telemetry exporters, or reverse-proxy logging separately.

## Running

```bash
cd PxMCP
dotnet run
```

The MCP server starts on `http://localhost:5277` by default. The MCP endpoint is at `/mcp`.

## MCP Tools

| Tool                   | Purpose                                                           |
| ---------------------- | ----------------------------------------------------------------- |
| `list_databases`       | List available statistical databases                              |
| `list_tables`          | Browse tables in a database (paginated)                           |
| `search`               | Search tables/dimensions/values with optional scope               |
| `get_head_metadata`    | Structural overview of a table (dimensions, roles, sample values) |
| `get_table_metadata`   | Full JSON-stat 2.0 metadata                                       |
| `get_dimension_values` | All codes/labels for a dimension                                  |
| `get_data`             | Query data with typed filters                                     |
| `validate_query`       | Validate filters and preflight cell count against a HEAD-derived limit without fetching data |

Typical agent workflow: `list_databases` → `search` → `get_head_metadata` → `get_dimension_values` → `validate_query` → `get_data`

Database IDs (letters/digits), table IDs (letters, digits, `_`, `-`; both max 50 characters), `lang` codes and search queries (non-blank, max 400 characters, as in PX API) are validated before any PX API request; invalid values return a tool error without contacting the upstream API.

### Query-size preflight

`validate_query` validates filters against metadata, then uses a data-endpoint HEAD request to read the current `X-Max-Cells` limit. It does not fetch the dataset or hard-code a cell limit. Invalid filters skip HEAD.

The response includes:

- `selectedCellCount`: exact Cartesian-product count, or null when the selection cannot be counted reliably.
- `maxCells`: current positive header limit, or null if HEAD fails or the header is unavailable/invalid.
- `exceedsMaxCells`: true/false only when both numbers are known; otherwise null.

Exact counts require an explicit filter for **every** dimension: unique literal Code values, inclusive literal From/To boundaries in metadata order, or First/Last counts within available values. Defaults/omitted dimensions, wildcards, empty/duplicate code lists, excessive positional counts and integer overflow return an unknown count with a warning, not an estimate.

A confirmed count above the maximum makes `isValid` false and adds an error. A failed HEAD check returns filter validation with a size-check-unavailable warning; cancellation still cancels the call. Always inspect warnings and nullable size fields: `isValid: true` alone does not guarantee `get_data` will succeed.

`get_data` makes no extra HEAD request and never truncates or changes your query. If the upstream returns HTTP 413, the error includes its `X-Max-Cells` value when valid, or generic narrowing guidance otherwise.

## Testing

```bash
# All tests
dotnet test

# Unit tests only
dotnet test PxMCP.Tests

# MCP protocol tests (in-process server+client)
dotnet test PxMCP.McpTests
```

## Projects

| Project          | Description                                                   |
| ---------------- | ------------------------------------------------------------- |
| `PxMCP`          | MCP server application                                        |
| `PxMCP.Tests`    | Unit tests (tool logic, API client)                           |
| `PxMCP.McpTests` | MCP protocol tests (schema validation, end-to-end tool calls) |
| `PxMCP.IntegrationTests` | HTTP application tests with a mocked PX API             |
