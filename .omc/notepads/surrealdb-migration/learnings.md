# SurrealDB Migration — Worker A Learnings

## SurrealDb.Net Package Discovery
- Spec cited "~0.10.2" — CONFIRMED correct. NuGet shows 12 versions; 0.10.2 is the highest.
- Package ID on NuGet: `SurrealDb.Net` (not `surrealdb.net`)
- The version 3.2.0 I initially tried does NOT exist. `0.10.2` is pinned.
- Pin: `[0.10.2]` (exact MSBuild bracket) in OASIS.WebAPI.csproj

## Project Assets
- `obj/project.assets.json` in the main project already had a failed restore for `SurrealDb.Net >= 3.2.0`
  from a previous attempt (Worker B's context added it before Task 1 was done).
  After my fix to `[0.10.2]`, restore should succeed.

## Integration Test Harness Architecture
- Old harness: `EnsureDeleted`/`EnsureCreated` on `OASISDbContext` — race-prone, EF-only
- New harness: `IAsyncLifetime` (per-class lifecycle) + SurrealDB namespace isolation
  - `InitializeAsync` → creates `test{guid}` namespace + applies schemas if Worker C provided them
  - `DisposeAsync` → drops the test namespace (REMOVE NAMESPACE)
  - G3 compliant: namespace in HTTP header, never interpolated into SurrealQL body
- HTTP-based seeding replaces direct EF entity insertion
  - `SeedAvatarAsync` → POST api/avatar/register
  - `SeedHolonAsync` → POST api/holon
  - `SeedWalletAsync` → POST api/wallet (uses JWT claim for AvatarId, not builder value)
  - `SeedSTARODKAsync` → POST api/starodk
  - `SeedBlockchainOperationAsync` → returns builder stub (no HTTP endpoint available)

## Data Isolation Gap (Wave 1 → Wave 2)
- The app backend is still EF-backed (wave 2 swaps to SurrealDB adapters).
- HTTP-seeded data goes into the shared EF store, NOT per-test SurrealDB namespaces.
- Tests that check `HaveCount(N)` may be polluted by data from other tests in the same run.
- RESOLUTION: these tests will be fixed in wave 2 when the SurrealDB adapter provides
  per-test namespace scoping end-to-end. Until then, `IntegrationTests` is compile-only gate;
  unit test suite (537+) remains the authoritative green gate per spec.

## AvatarBuilder Wallet Seeding
- `WalletController.Create` reads `AvatarId` from JWT claim, not from body.
- Tests that call `SeedWalletAsync(w => w.ForAvatar(someOtherAvatarId))` will get
  `TestAuthHandler.DefaultAvatarId` as the wallet's AvatarId regardless of the builder value.
- Wave 2 fix: wallet seed endpoint or store-level seeding via SurrealDB client.

## HolonBuilder.BuildCreateModel() Fix
- Original `BuildCreateModel()` omitted `ParentHolonId` — fixed to include it.
- This was a silent bug in the EF harness (parent was set on the entity directly, bypassing API).

## SurrealDB Server Version Compatibility
- SurrealDb.Net 0.10.2 (Apr 2024) targets SurrealDB server 1.x protocol.
- docker-compose.surrealdb.yml uses `surrealdb/surrealdb:v1.5.4`.
- `SURREAL_SYNC_DATA=true` is a 2.x env var; `SURREAL_KV_ROCKSDB_SYNC=true` added for 1.x.
- When upgrading to SurrealDB 2.x server, also upgrade SDK pin.

## MSBuild VerifySurrealSdkPin Target
- Reads project.assets.json via `check-sdk-pin.ps1` to find resolved version.
- If drift detected: exits non-zero → Build fails with "[G4 SDK-PIN DRIFT DETECTED]" message.
- The bracket `[0.10.2]` already prevents NuGet from resolving other versions;
  the target is defense-in-depth for manual csproj edits that forget to update assets.
- `EchoOff="false"` ensures the OK/FAIL message appears in build output.

## AvatarNFTControllerIntegrationTests Rewrite
- Was: `IClassFixture<WebApplicationFactory<Program>>` with InMemory EF swap inside constructor.
- Now: extends `IntegrationTestBase` (no EF, no InMemory swap).
- All data seeded via mint/bind/POST endpoints; no direct DB injection.
- `GetAvatarNFTsByAvatarAsync` test may intermittently count >2 due to shared store — wave 2 fix.
