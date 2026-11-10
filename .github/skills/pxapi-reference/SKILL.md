---
name: pxapi-reference
description: "Consult the current Statistics Finland PxApi Beta OpenAPI contract when changing PxMCP API integration, models, or tools."
user-invocable: true
---

# PX API reference

Use the upstream OpenAPI document as the source of truth for endpoint paths, parameters, filter formats, response schemas, nullability, and status codes. Do not copy its endpoint or schema catalog into this skill; follow the live specification so this guidance does not drift.

- [PxApi Beta Swagger UI](https://api.stat.fi/docs/?urls.primaryName=PxApi+Beta)
- [PxApi Beta OpenAPI document](https://api.stat.fi/px/beta/openapi)

## Applying the contract in PxMCP

When adding or changing an integration:

1. Find the relevant operation in the OpenAPI document. Check its method, path, query/path parameters, request body, content types, response schema, and documented errors.
2. Keep the upstream contract distinct from PxMCP's MCP-facing behavior. Check the relevant tool, `IPxApiClient`/`PxApiClient`, DTOs, serializer context, and tests before deciding where a change belongs.
3. When changing a DTO under `PxMCP/Models/PxApi/`, compare its property names, JSON types, requiredness, and nullability with the referenced OpenAPI schemas. Follow `.github/instructions/pxapi-models.instructions.md`.
4. For filter changes, verify the `Filter` schema and endpoint-specific encoding. `get_data` currently calls the POST data operation with a JSON object mapping dimension IDs to filter objects; do not assume GET query-string encoding is interchangeable.
5. Add or update tests for both the API-client request/response behavior and any affected MCP tool contract. Prefer focused mocked tests for deterministic behavior; use the live public API only for explicit compatibility checks.

## Current PxMCP integration map

This map describes the wrapper, not the upstream API schema:

| MCP tool | PxMCP integration |
|---|---|
| `list_databases` | Lists databases through `IPxApiClient`. |
| `list_tables` | Retrieves a database's paged table listing. |
| `search` | Routes to global or database-scoped search. |
| `get_table_metadata` | Returns the upstream JSON-stat metadata response. |
| `get_head_metadata` | Builds a compact table overview from JSON-stat metadata. |
| `get_dimension_values` | Extracts one dimension's values from JSON-stat metadata. |
| `get_data` | Posts dimension filters and returns the JSON-stat dataset. |
| `validate_query` | Validates filters against retrieved table metadata without requesting the data result. |

`TableSummary` and other search/listing DTOs represent upstream response contracts; verify them against the live schemas rather than relying on this map. The API base URL is configured through `PxApiOptions.BaseUrl`.
