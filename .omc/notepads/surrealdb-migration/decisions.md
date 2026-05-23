# Worker B Decisions (Task 4 + Task 23)

## Strict mode default for SurrealQuery.Validate
- Strict mode (default=true) rejects BOTH missing params AND extra params.
- Rationale: extra params are a common sign of a copy-paste error or stale binding
  from a refactored query. The cost of being explicit > the cost of silently ignoring extra params.
- Callers can opt into lenient mode by passing `strict: false` — used in tests.

## No generic "strict vs lenient" constructor flag
- Mode is a per-call argument to `Validate()`, not a constructor option.
- Rationale: different call sites may have different needs; don't bake it into the type.

## SurrealIdentifier regex: ^[a-z][a-z0-9_]*$ (lowercase only)
- Rejected uppercase, hyphens, leading underscores.
- SurrealDB identifiers are case-sensitive. Uppercase table names would bypass the
  lowercase check, allowing USERS to resolve to a different table than users.
- Leading underscores rejected (convention ambiguity; add exception if needed).

## Record ID suffix: alphanumeric + underscore + hyphen only
- UUIDs use hyphens; alphanumeric IDs use underscores — both are safe unquoted.
- For arbitrary/opaque IDs (full UUIDs, base64) the recommendation is to use a
  typed parameter via `WithParam` instead, not string interpolation.

## SurrealExecutor uses `GetResults<T>()` (flat enumerable) not indexed access
- SurrealDb.Net 0.10.x API: `SurrealDbResponse.GetResults<T>()` returns all results
  from all statements as a flat enumerable. This means callers must pass
  single-statement queries to get predictable results.
- This is documented in the ISurrealExecutor interface doc comment.
- Indexed access (GetResult<T>(index)) may be the 0.10.x actual API — needs
  confirmation once the SDK is installed with the correct pin.

## Analyzer: heuristic + semantic hybrid for ISurrealDbClient detection
- When the SDK is not restored (like now), semantic resolution fails.
- Fallback: receiver name contains "surreal" or "db" (case-insensitive).
- This is an ERROR-severity gate — false positives (e.g. a `_db` variable on an
  unrelated type that has a method named `Query`) should be reported to the team.
  The noise is manageable because the pattern is narrow.

## Analyzer exempts Core.SurrealDb.Query namespace
- The safe-construction layer uses string interpolation internally (e.g. SelectById).
- Namespace check is based on the enclosing `BaseNamespaceDeclarationSyntax` containing
  "Core.SurrealDb.Query" as a substring.

## SurrealDb.Net pin: 3.2.0 vs 0.10.2
- Worker A pinned 3.2.0 which does not exist on NuGet (highest is 0.10.2).
- Worker B code is written against the 0.10.x API (ISurrealDbClient, RawQuery, GetResults<T>).
- Fix: Worker A must change SurrealDbNetPinnedVersion to "0.10.2" and update
  check-sdk-pin.ps1 accordingly.
