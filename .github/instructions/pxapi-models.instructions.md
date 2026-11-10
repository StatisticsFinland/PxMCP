---
description: "Use when modifying, creating, or reviewing DTO/model classes under Models/PxApi/. Ensures C# models stay aligned with the upstream PX API OpenAPI schema."
applyTo: "PxMCP/Models/PxApi/**"
---

# PX API Model Alignment

When changing DTO classes under `Models/PxApi/`:

- Consult the PX API OpenAPI spec via the `pxapi-reference` skill to verify property names, types, nullability, and required fields.
- Keep C# property names matching the JSON field names (use `[JsonPropertyName]` only if they differ).
- Mark nullable reference-type properties with `?` when the OpenAPI schema has `"nullable": true`.
- Model polymorphic filters as typed `Filter` subclasses discriminated by `type` (`CodeFilter`, `FromFilter`, `ToFilter`, `FirstFilter`, `LastFilter`), each with a strongly typed `Query` — see the Filter Model section in `docs/ARCHITECTURE.md`.
- Update `JsonContext` source-generated serializer if you add or rename a model type.
