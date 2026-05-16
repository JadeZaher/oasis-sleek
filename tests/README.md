# Tests

All .NET test projects for OASIS.WebAPI live here, alongside mutation-test output.

| Project | Type | Purpose |
|---------|------|---------|
| `OASIS.WebAPI.Tests` | xUnit | Unit tests for managers, controllers, providers, validation |
| `OASIS.WebAPI.IntegrationTests` | xUnit + `WebApplicationFactory` | In-process HTTP integration tests |
| `OASIS.WebAPI.LiveTests` | Console harness | JSONL-driven tests against a *running* API |
| `StrykerOutput/` | Artifacts | Stryker.NET mutation-test reports (git-ignored) |

## Running

From the repo root (or anywhere — the script resolves paths itself):

```powershell
# Unit + integration suites (the default "all tests")
./tests/run-tests.ps1

# Release configuration
./tests/run-tests.ps1 -Configuration Release

# Also exercise the live HTTP harness (needs a running API)
./tests/run-tests.ps1 -Live -LiveUrl https://localhost:5001

# Mutation testing -> tests/StrykerOutput
./tests/run-tests.ps1 -Mutation
```

Equivalent raw commands:

```bash
dotnet test oasis-sleek.sln                                   # unit + integration
dotnet run --project tests/OASIS.WebAPI.LiveTests             # live harness
dotnet stryker --output tests/StrykerOutput                   # mutation testing
```

> `OASIS.WebAPI.LiveTests` is a console `Exe`, not a `dotnet test` project, so it
> is excluded from `dotnet test` discovery and must be launched explicitly (see
> its own [README](OASIS.WebAPI.LiveTests/README.md)).

## Paths

These projects sit one directory deeper than before, so each `.csproj`
references the app with `..\..\OASIS.WebAPI.csproj`. The solution
(`oasis-sleek.sln`), `stryker-config.json`, and the main `OASIS.WebAPI.csproj`
source excludes all point at `tests/`.
