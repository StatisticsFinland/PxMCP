# Testing Setup

This project has three test projects with distinct purposes. None of them contact the live PX API.

## PxMCP.Tests — Unit Tests

Direct method-level tests for tool logic and the API client. Each test class instantiates the tool class directly, passing a `Mock<IPxApiClient>` via constructor.

- **Framework:** NUnit 4.3.2 + Moq 4.20.72
- **Run:** `dotnet test PxMCP.Tests`
- **Pattern:** Arrange mock → call tool method → assert on the typed return value and/or verify mock calls

These tests validate:

- Correct parameters are forwarded to `IPxApiClient` methods
- Return values are properly constructed from API responses
- Filter types (Code, From, To, First, Last) are passed through correctly
- Error cases like invalid dimensions throw `McpException`
- Structural overview formatting (e.g. `get_head_metadata` output)
- Head metadata logic (`BuildHeadMetadata`)

Unit tests do **not** exercise JSON-RPC serialization, tool attribute discovery, parameter binding from JSON, or output schema generation.

## PxMCP.McpTests — MCP Protocol Integration Tests

End-to-end tests through the full MCP protocol stack using an in-process transport. These test what an MCP client actually sees.

- **Framework:** NUnit 4.3.2 + Moq 4.20.72 + ModelContextProtocol SDK client
- **Run:** `dotnet test PxMCP.McpTests`
- **Base class:** `McpTestFixture` — sets up an in-process server and client connected via `System.IO.Pipelines.Pipe`

### How McpTestFixture works

Each test gets a fresh MCP server and client:

1. Creates two `Pipe` instances (client→server and server→client)
2. Builds a host with `AddMcpServer().WithStreamServerTransport().WithToolsFromAssembly()`
3. Registers a `Mock<IPxApiClient>` in DI (same mock pattern as unit tests)
4. Creates an `McpClient` connected to the other end of the pipes
5. Tests call `Client.ListToolsAsync()` or `Client.CallToolAsync()` — real MCP JSON-RPC messages flow through the pipes

This exercises the same code paths as production, including:

- Tool attribute discovery (`[McpServerToolType]`, `[McpServerTool]`)
- JSON schema generation from method signatures and `[Description]` attributes
- Parameter deserialization from JSON-RPC into typed .NET arguments
- Structured content serialization (`UseStructuredContent = true`)
- Error handling (McpException → `isError: true` result)

### What the McpTests validate

**ToolDiscoveryTests** — Tool registration:

- All expected tools are discovered by the server
- Every tool has a non-empty description and JSON schema

**ToolSchemaTests** — Input schema correctness:

- Parameter types are correct (e.g. `filters` is `object`, not `string`)
- Required vs optional parameters match the method signatures
- All parameters have descriptions

**ToolInvocationTests** — End-to-end tool calls:

- Tools return structured content (`result.StructuredContent`) with correct data
- Parameter values are correctly deserialized and forwarded (e.g. filters, lang, pagination)
- Search scoping (global vs database vs table) routes to the correct API method
- Validation results contain proper `isValid`/`statusCode` fields
- Error cases return `result.IsError == true`

**ToolDocumentationDump** — Diagnostic (not a real assertion):

- Dumps every tool's name, description, and full JSON schema to stdout
- Run with: `dotnet test PxMCP.McpTests --filter ToolDocumentationDump -v normal`
- Useful for reviewing what an MCP client sees after changes

## PxMCP.IntegrationTests — HTTP Application Tests

Tests the production ASP.NET Core application (`Program.CreateApplication`) through `TestServer`, with `IPxApiClient` replaced by a strict Moq mock or a real `PxApiClient` over a mocked `HttpMessageHandler`. One test runs a loopback Kestrel server to verify request body size limits.

- **Framework:** NUnit 4.3.2 + Moq 4.20.72 + `Microsoft.AspNetCore.TestHost`
- **Run:** `dotnet test PxMCP.IntegrationTests`

These tests validate DI and HTTP client configuration, `/health`, the `/mcp` Streamable HTTP endpoint, Host/Origin validation, request body and concurrency limits, request cancellation, and that framework logs do not contain caller input.

### When to add McpTests, integration tests or unit tests

- Add a **unit test** when testing tool logic, API client behavior, data transformation, or error handling in isolation.
- Add an **McpTest** when verifying something that depends on the MCP protocol layer: schema shape, parameter binding from JSON, structured content output, tool discovery, or how errors surface to clients.
- Add an **integration test** when verifying the production HTTP host: startup configuration and validation, middleware, routing, limits, or HTTP client setup.
- If you add a new tool, add entries in both: a unit test for the method logic and McpTests for discovery + invocation through the protocol.
