# Worker B Issues

## BLOCKER: SurrealDb.Net pin 3.2.0 does not exist

Worker A pinned `SurrealDb.Net` to version `3.2.0` in `OASIS.WebAPI.csproj` and
`scripts/surrealdb/check-sdk-pin.ps1`. This version has never been released —
NuGet has exactly 12 versions from 0.1.3 to 0.10.2.

Impact:
- `dotnet restore` / `dotnet build` fails for the entire solution.
- `dotnet test tests/OASIS.WebAPI.Tests` cannot run.
- Worker B's executor tests cannot be executed to confirm correctness.

Fix: change `SurrealDbNetPinnedVersion` in `OASIS.WebAPI.csproj` from `3.2.0` to
`0.10.2`, and change the `PinnedVersion` literal in `scripts/surrealdb/check-sdk-pin.ps1`
from `3.2.0` to `0.10.2`. Then run `dotnet restore`.

## SurrealDbResponse API shape: GetResults<T>() vs GetResult<T>(index)
- Cannot verify the exact API surface of SurrealDb.Net 0.10.2 without installing
  the package (blocked by the pin issue above).
- `SurrealExecutor.cs` uses `response.GetResults<T>()` based on the documented
  SurrealDb.Net 0.10.x API. If the actual method signature is different (e.g.
  `GetResult<List<T>>(statementIndex)`), `SurrealExecutor.cs` needs updating.
- This will surface as a CS compilation error once the pin is fixed.

## Analyzer test compilation: depends on broken pin
- `SurrealQlSafetyAnalyzerTests.cs` is in `OASIS.WebAPI.Tests` which depends on
  `OASIS.WebAPI.csproj` which has the broken pin.
- The tests cannot be compiled or run until the pin is fixed.
- Alternative: create a standalone analyzer test project with no SurrealDb dependency.

## StringBuilder heuristic in analyzer
- `StringBuilder.ToString()` detection falls back to name heuristic when semantic
  model is unavailable.
- False negative: `var myQueryBuilder = new StringBuilder(); db.Query(myQueryBuilder.ToString())`
  — will be caught by semantic resolution (type is StringBuilder).
- False negative: `db.Query(GetSqlFromStringBuilder())` — this is NOT caught because
  the outer call is an arbitrary method, not a literal StringBuilder.ToString().
- This is a known limitation. The safe path is SRDB0001 will still fire on the
  outer db.Query call if the SQL argument is a call expression returning a string
  from a variable that was built via interpolation — but we can't trace this
  inter-procedurally. Document as "best-effort for direct callsites."
