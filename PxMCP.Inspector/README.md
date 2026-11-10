# PxMCP Inspector

MCP Inspector configured for interactive testing and debugging of the PxMCP server.

## Prerequisites

- **Node.js** >= 22.19.0
- **PxMCP server** running locally on `http://localhost:5277`

## Setup

```bash
cd PxMCP.Inspector
npm ci
```

Inspector 2 replaces the earlier 0.21 release and its patch-only dependency
overrides. The npm scripts use the locally installed, locked executable.
Audit dependencies with `npm audit` before use, and keep the Inspector restricted
to local development. Do not use `npm audit fix --force` without reviewing the
resulting upgrades.

## Usage

### 1. Start the PxMCP server

From the solution root:

```bash
cd PxMCP
dotnet run
```

### 2. Launch the Inspector UI

```bash
npm run inspect
```

This opens the MCP Inspector web UI at `http://localhost:6274`, pre-configured to connect to the PxMCP server via Streamable HTTP at `http://localhost:5277/mcp`.

### 3. CLI mode

For scripting or quick checks without the UI:

```bash
# List tools through the CLI
npm run inspect:cli -- --method tools/list

# List all available tools
npm run inspect:list-tools

# Call a specific tool directly
npm run inspect:cli -- --method tools/call --tool-name list_databases
```

## Configuration

The server connection is defined in [`mcp.json`](mcp.json). Update the `url` if your PxMCP server runs on a different port or host.

Inspector 2 treats `--config` as a read-only session configuration; edits in the
UI are not saved to this file. Use its separate `--catalog` option if you need a
writable server catalog. Mode flags (`--web`, `--cli`, `--tui`) must precede
configuration options when invoking `mcp-inspector` directly.

If your environment uses an HTTP proxy, include `localhost`, `127.0.0.1`, and
`::1` in `NO_PROXY` so local MCP connections do not go through the proxy.

## Ports

| Service          | Default Port |
|------------------|-------------|
| Inspector UI     | 6274        |
| MCP Apps sandbox | 6275        |
| MCP Apps origin  | 6278        |
| PxMCP Server     | 5277        |

Inspector 2 serves its UI and API together on port 6274; the old standalone proxy
on port 6277 is no longer used.
