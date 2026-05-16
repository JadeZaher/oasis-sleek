# MCP Surface — Plan

## Tasks

1. [ ] Choose MCP hosting approach for the .NET service (MCP server endpoint / sidecar) and document it
2. [ ] Define the MCP tool catalog: quest-reachability, holon-traverse, nft-ownership-graph, avatar-scoped-query, vector-search
3. [ ] Implement graph-traversal tools over SurrealDB `->`/`<-` (recursive, shortest-path) — no recursive-CTE fallback
4. [ ] Add `HNSW` vector index on holon/quest metadata; implement the vector-search tool
5. [ ] Enforce avatar scoping + existing auth on every MCP tool; no privileged bypass
6. [ ] Route any write-capable tool touching irreversible chain ops through the `api-safety-hardening` idempotency + state-guard path
7. [ ] Parameterized SurrealQL only for model-input tools (G3); injection tests on tool inputs
8. [ ] MCP integration tests: representative agent queries return correct, scoped results in a single graph query
9. [ ] Auth/leakage tests: cross-tenant query rejected; write tool honors idempotency
10. [ ] `dotnet build` — zero warnings; all tests passing
11. [ ] Document the MCP tool catalog + example agent workflows
