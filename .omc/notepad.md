# OMC Notepad — api-safety-hardening

## API-SAFETY CONTRACT (Wave 1 output)

Wave 1 built the shared spine. Waves 2+ implement against the contracts below.
Everything here compiled clean (`dotnet build OASIS.WebAPI.csproj -c Debug` → 0 errors)
and the EF migration `20260516224425_AddIdempotencyAndBridgeSafetyConstraints`
was scaffolded by a real build (not hand-written).

---

### 1. Idempotency store

**Interface** — `OASIS.WebAPI.Interfaces.IIdempotencyStore`
(file `Interfaces/IIdempotencyStore.cs`):

```csharp
namespace OASIS.WebAPI.Interfaces;

public sealed record IdempotencyClaim(bool Won, OASIS.WebAPI.Models.Idempotency.IdempotencyRecord Record);

public interface IIdempotencyStore
{
    Task<IdempotencyClaim> TryClaimAsync(string key, string operationType, CancellationToken ct);
    Task CompleteAsync(string key, string resultPayload, CancellationToken ct);
    Task FailAsync(string key, string error, CancellationToken ct);
    Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct);
}
```

- `IdempotencyClaim` is a positional `record` (`bool Won`, `IdempotencyRecord Record`),
  namespace `OASIS.WebAPI.Interfaces`.
- `Won == true` ⇒ THIS caller inserted the row; it owns the right to perform the
  irreversible (on-chain) effect exactly once, then MUST call `CompleteAsync`
  (success) or `FailAsync` (failure).
- `Won == false` ⇒ duplicate/concurrent request; do NOT perform the effect.
  Inspect `Record` (it may be `InProgress`, `Completed`, or `Failed`) and
  replay its cached `ResultPayload` / `Error`, or surface "in progress".
- `TryClaimAsync` throws `ArgumentException` on null/empty key.
- `CompleteAsync`/`FailAsync` throw `InvalidOperationException` if no claim
  exists for the key (must follow a winning `TryClaimAsync`).

**Implementation** — `OASIS.WebAPI.Core.Idempotency.IdempotencyStore`
(file `Core/Idempotency/IdempotencyStore.cs`), `sealed`, ctor
`IdempotencyStore(OASISDbContext db)`. Scoped lifetime.
**DI registration is NOT done — Wave 3 must register**
`services.AddScoped<IIdempotencyStore, IdempotencyStore>();`.

Atomicity: UNIQUE constraint on `IdempotencyRecord.Key` + insert-wins. The impl
catches `DbUpdateException` on the `SaveChangesAsync` insert, detaches the failed
entity, re-reads by key, and returns `Won=false` + the winning row. If no row
exists after the failure it rethrows (genuine DB error, not a unique violation).
There is also a fast-path no-tracking pre-read before the insert attempt.

**Entity** — `OASIS.WebAPI.Models.Idempotency.IdempotencyRecord`
(file `Models/Idempotency/IdempotencyRecord.cs`):

| Field | Type | Notes |
|---|---|---|
| `Key` | `string` | `[Key]`, max 200, PK + UNIQUE — the atomicity primitive |
| `OperationType` | `string` | max 64 (diagnostic scope only; uniqueness is on Key alone) |
| `State` | `IdempotencyState` | enum, stored as `int` |
| `ResultPayload` | `string?` | max 4096, cached success result for replay |
| `Error` | `string?` | max 1024, failure reason |
| `CreatedAt` | `DateTime` | |
| `UpdatedAt` | `DateTime` | |

**Enum** — `OASIS.WebAPI.Models.Idempotency.IdempotencyState`:
`{ InProgress = 0, Completed = 1, Failed = 2 }` (persisted as int).

---

### 2. Consumed-VAA replay ledger

**Entity** — `OASIS.WebAPI.Models.ConsumedVaaRecord`
(file `Models/ConsumedVaaRecord.cs`, `[Table("ConsumedVaas")]`):

| Field | Type | Notes |
|---|---|---|
| `Digest` | `string` | `[Key]`, max 128, PK + UNIQUE — the replay-protection key |
| `EmitterChainId` | `int` | |
| `EmitterAddress` | `string` | max 128 |
| `Sequence` | `long` | |
| `BridgeTransactionId` | `string?` | max 64, audit linkage to the bridge tx |
| `ConsumedAt` | `DateTime` | |

**DbSet:** `OASISDbContext.ConsumedVaas` (`DbSet<ConsumedVaaRecord>`).
Unique field: `Digest`. Also a non-unique composite index on
`(EmitterChainId, EmitterAddress, Sequence)`.

**How Wave 2 checks/inserts a consumed VAA (replay protection):**
BEFORE submitting the VAA to the target chain, insert a row keyed by the VAA
digest and `SaveChangesAsync`. Use the SAME catch-`DbUpdateException`-then-reread
pattern as `IdempotencyStore.TryClaimAsync`:

```csharp
_db.ConsumedVaas.Add(new ConsumedVaaRecord {
    Digest = vaaDigest, EmitterChainId = ec, EmitterAddress = ea,
    Sequence = seq, BridgeTransactionId = bridgeId });
try { await _db.SaveChangesAsync(ct); /* first time — proceed to redeem */ }
catch (DbUpdateException) {
    _db.Entry(/*the added record*/).State = EntityState.Detached;
    var dup = await _db.ConsumedVaas.AsNoTracking()
        .FirstOrDefaultAsync(v => v.Digest == vaaDigest, ct);
    if (dup is not null) { /* REPLAY — reject the redeem */ }
    else throw; // genuine DB error
}
```

(Recommended: insert the digest INSIDE the same transaction/flow as the redeem
state transition so a replayed VAA can never be redeemed twice.)

---

### 3. DbSets added to `OASIS.WebAPI.Data.OASISDbContext`

```csharp
public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
public DbSet<ConsumedVaaRecord> ConsumedVaas => Set<ConsumedVaaRecord>();
```
(`using OASIS.WebAPI.Models.Idempotency;` added.) Existing configs, including the
`QuestEdge` unique index, were left untouched.

---

### 4. BridgeTransactionResult / BridgeStatus

**`BridgeStatus.Minted` was REMOVED.** Evidence: repo-wide grep for the enum
member found only the definition itself; `BlockchainOperationManager.cs:111`
(`ApplyChainResult(operation, result, "Minted")`) and
`BlockchainOperationManagerTests.cs:60` use a STRING literal `"Minted"` for the
unrelated `BlockchainOperation.Status` string field — NOT `BridgeStatus`. No
code ever assigned `BridgeStatus.Minted`.

**Final `BridgeStatus` enum** (`Models/Responses/BridgeTransactionResult.cs`):
```
Initiated, Locked, AwaitingVAA, VAAReady, Redeeming, Completed, Failed, Refunded
```
Canonical lifecycle: `Initiated → Locked → AwaitingVAA → VAAReady → Redeeming
→ Completed`; `Failed` / `Refunded` terminal.

**New fields on `BridgeTransactionResult`:**
- `public string? IdempotencyKey { get; set; }` — max 200, nullable
  (back-compat). Non-unique index `IX_BridgeTransactions_IdempotencyKey`.
- `public uint Version { get; set; }` — **PostgreSQL `xmin` system column** row
  version (Npgsql idiom). Configured in DbContext as:
  ```csharp
  entity.Property(e => e.Version)
        .HasColumnName("xmin").HasColumnType("xid")
        .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
  ```
  Database-generated, read-only; every committed UPDATE bumps it. Migration
  emitted `xmin = table.Column<uint>(type: "xid", rowVersion: true, ...)`.

**New bridge indexes (in DbContext + migration):**
- `IX_BridgeTransactions_LockTxHash` — UNIQUE, filtered
  `"LockTxHash" IS NOT NULL`.
- `IX_BridgeTransactions_WormholeEmitterChainId_WormholeEmitterAddress_WormholeSequence`
  — UNIQUE, filtered to all three non-null.

**CONCURRENCY MECHANISM CHOSEN: `ExecuteUpdateAsync` with a status predicate,
assert exactly one row affected.** This is the primary recommended pattern for
Wave 2's atomic state transitions (it is a single conditional UPDATE statement —
no read-modify-write window, no reliance on tracking). Copy-pasteable:

```csharp
// Atomic VAAReady -> Redeeming, MUST run BEFORE any on-chain call.
int affected = await _db.BridgeTransactions
    .Where(b => b.Id == bridgeId && b.Status == BridgeStatus.VAAReady)
    .ExecuteUpdateAsync(s => s
        .SetProperty(b => b.Status, BridgeStatus.Redeeming)
        .SetProperty(b => b.IdempotencyKey, idempotencyKey),
        ct);

if (affected != 1)
{
    // Lost the race / not in expected state. Re-read to decide:
    //   already Redeeming/Completed by a concurrent request -> replay/return.
    //   unexpected state -> reject. DO NOT perform the on-chain mint.
    return; // or surface conflict
}
// affected == 1: this caller exclusively owns the transition. Proceed to mint,
// then transition Redeeming -> Completed the same way (WHERE Status==Redeeming).
```

Alternative (if you need change-tracking / navigation loads): load the entity,
mutate `Status`, `SaveChangesAsync`, and catch
`DbUpdateConcurrencyException` (the `xmin` token enforces the optimistic check).
Prefer the `ExecuteUpdateAsync` form for the irreversible bridge transitions.

NOTE: `ExecuteUpdateAsync` bypasses the change tracker and does NOT trigger the
`xmin` concurrency check itself — its safety comes from the
`WHERE Status == expected` predicate + the `affected == 1` assertion. The `xmin`
token is for the load/mutate/`SaveChanges` path. Use ONE consistently per flow.

---

### 5. OperationIdGenerator — now fully deterministic

`Core/OperationIdGenerator.cs` rewritten. **`DateTime.UtcNow.Ticks` removed from
both overloads.** Same inputs ⇒ same id forever (true content addressing) — it
is now usable as an idempotency key. Public signatures UNCHANGED (verified
against all callers in SolanaProvider/AlgorandProvider — 3-arg and `params`
overloads still compile):

- `Generate(string chain, string operationType, string walletAddress)`
- `Generate(string chain, string operationType, string walletAddress, params object[] parameters)`

Hash input is now `{chain.ToLowerInvariant()}|{operationType.ToLowerInvariant()}|{walletAddress}[|{params}]`,
SHA-256, first 12 hex; output format unchanged:
`op_{chain}_{operationType}_{first12hex}`. Doc-comment updated to accurately
describe deterministic content-addressing (the old false "deterministic + Ticks"
contradiction is gone).

---

### 6. EF migration + INTEGRATION RISK (read this, Wave 2/integrator)

Migration `Migrations/20260516224425_AddIdempotencyAndBridgeSafetyConstraints.cs`
scaffolded via a real `dotnet ef migrations add` build (EF Core 8.0.0, Npgsql).

**PRE-EXISTING DEFECT SURFACED:** `BridgeTransactions` was **never created by
any prior migration** (InitialCreate / AddApiKeys / AddWalletType contain zero
references; the committed model snapshot had zero `BridgeTransactions`). The
table only ever materialized via `EnsureCreated()` in tests. Therefore this
migration's `Up()` includes `CreateTable("BridgeTransactions")` (plus the new
`IdempotencyRecords` and `ConsumedVaas` tables and all indexes).

Consequence for integrators:
- Fresh DB / `Database.Migrate()` from empty: correct, creates everything.
- A DB where `BridgeTransactions` already physically exists (e.g. created by
  `EnsureCreated`) WILL FAIL on `CreateTable("BridgeTransactions")`
  ("relation already exists"). Pre-launch greenfield is fine; if any
  environment has a hand/EnsureCreated-created table, drop it or wrap the
  bridge-table creation in `CREATE TABLE IF NOT EXISTS` / split the migration
  at integration. This is NOT a Wave-1 code bug — it is a corrected baseline.
- `dotnet ef` initial run failed while a stale dev server (PID 88580, started
  pre-task) locked `bin\Debug\net8.0\OASIS.WebAPI.dll`; that stale process was
  stopped so a clean build + scaffold could run. The first (bad) scaffold made
  with `--no-build` against the stale assembly was fully removed/reverted; the
  final migration is from a correct fresh build.

Build gate final result: **Build succeeded — 0 errors**, 17 warnings (all
pre-existing in SolanaProvider / CrossChainBridgeService / SearchManager; none
from Wave 1 files).

---

## WAVE 2 — WORKER W3 OUTPUT (server-side broadcast idempotency, tasks 11/12/13)

Files touched (exclusive ownership, surgical):
- `Managers/BlockchainOperationManager.cs` — task 11
- `Core/AlgorandFaucet.cs` + `Core/IAlgorandFaucet.cs` — task 12
- `Core/Blockchain/Base/BaseBlockchainProvider.cs` — task 13

**DI ACTION REQUIRED (integrator / Program.cs owner):**
- Wave 1 already flagged: register `services.AddScoped<IIdempotencyStore, IdempotencyStore>();`
  — W3 depends on it. NOT registered by W3 (Program.cs out of scope).
- `BlockchainOperationManager` ctor is now 3-arg
  `(ProviderContext, IBlockchainProviderFactory, IIdempotencyStore)` — REQUIRED,
  not optional (per directive). API DI resolves fine once `IIdempotencyStore`
  is registered. **Two test files break** and are a later test wave's concern:
  `tests/OASIS.WebAPI.Tests/BlockchainOperationManagerTests.cs:39` and
  `tests/.../Managers/BlockchainOperationManagerExtendedTests.cs:49` (both use
  the old 2-arg ctor).
- `AlgorandFaucet` is a **singleton** (`Program.cs:130`) but `IIdempotencyStore`
  is **scoped**. W3 did NOT change the registration; instead the faucet now
  injects `IServiceScopeFactory` (singleton-safe) and resolves the scoped store
  per call via `using var scope = _scopeFactory.CreateScope()` — identical
  pattern to `Core/ApiKeyAuthenticationHandler.cs`. No Program.cs change needed
  for the faucet. (The faucet ctor signature changed: it now also takes
  `IServiceScopeFactory` — DI supplies it automatically; any test mock that
  `new AlgorandFaucet(...)`s directly must pass one. Repo currently mocks the
  `IAlgorandFaucet` interface, not the concrete class, so production compiles.)

**Idempotency key derivation (deterministic, no GUID/timestamp):**
- Ops: `OperationIdGenerator.Generate(chain, opType, walletAddress, opParams)`
  where `opParams` is a per-op-type stable tuple (Mint: tokenUri/amount/asset;
  Swap: tokenIn/out/amountIn/minOut; Transfer: source/recipient; etc.). Never
  includes `operation.Id` (fresh GUID) or time.
- Faucet 2-arg back-compat overload derives key =
  `OperationIdGenerator.Generate("Algorand","faucet-dispense", toAddress, amountInvariant)`
  so even legacy `WalletManager.cs:392` callers are deduped. Amount formatted
  with `InvariantCulture` (decimal.ToString() is locale-sensitive — do NOT feed
  raw decimals to OperationIdGenerator's params overload).

**Won==false handling (no re-execution):**
- Ops: `ReplayFromRecord` reconstructs `OASISResult<IBlockchainOperation>` from
  the idempotency `Record` alone: Completed ⇒ Status=Completed + TxHash from
  ResultPayload; Failed ⇒ IsError + prior Error; InProgress ⇒ Status=Pending
  "still in progress" (no re-broadcast). Duplicate does NOT create a 2nd op row
  (save now happens only after winning the claim — behavioral change from
  original "save-first").
- Faucet: Won==false ⇒ Completed replays prior txid; Failed throws (no
  re-submit); InProgress throws conflict (no re-submit).
- AwaitingSignature ops are NOT marked Completed (no server broadcast yet) —
  left InProgress so duplicates replay the awaiting-signature instruction.

**Retry safety (task 13):** added `RetrySafety` enum `{ Idempotent=0,
Broadcast=1 }` + optional `safety` param (default `Idempotent`) on BOTH
`ExecuteWithRetryAsync` overloads. Default path defers to the original
`virtual IsRetryable(Exception)` verbatim ⇒ ALL existing call sites (none pass
the param) are byte-for-byte unchanged; derived overrides of `IsRetryable`
still work. `Broadcast` mode retries ONLY on provable pre-send `SocketException`
(ConnectionRefused/HostNotFound/HostUnreachable/NetworkUnreachable/HostDown);
every ambiguous post-send failure (timeout, null status, 5xx, 429) is rethrown
unretried for reconciliation. NOTE: no current provider call site opts into
`Broadcast` yet — it is the mechanism for future server-side submit paths;
existing broadcasters (faucet) are guarded by the idempotency ledger instead.

**Residual risk (reconciliation territory, plan tasks 14-16, NOT this wave):**
- Faucet: if `SubmitTransaction` succeeds but the subsequent `CompleteAsync`
  throws, the catch records `Failed` though the tx landed. Fail-closed: a
  same-key retry sees Failed and refuses to re-submit (no double-spend) but the
  ledger now disagrees with chain truth → needs the reconciliation sweep.
- Ops: `Won==false`+`InProgress` after an original that crashed mid-flight
  stays a perpetual "pending" until reconciliation re-derives chain status.
- `RetrySafety.Broadcast` is wired but unused; adopting it on real future
  server submits is a follow-up.

---

## WAVE 3 — INTEGRATION OUTPUT (tasks 10/18/19 + DI wiring)

Consolidated build: `dotnet build OASIS.WebAPI.csproj -c Debug` → **0 errors,
17 warnings** (== Wave-1 baseline; zero new warnings — all 17 are the
pre-existing SolanaProvider / SearchManager:202 / CrossChainBridgeService:464
CS1998 set, none in any Wave-3-touched file).

**Stale server:** no `dotnet run` web host was holding the API DLL. The only
`dotnet` process was a `vstest.console` test runner (a later test wave's
concern), which loads testhost — not the production web host — so it does not
lock `bin\Debug\net8.0\OASIS.WebAPI.dll`. User confirmed they killed it.

### Program.cs DI (all additive)
- `AddScoped<IIdempotencyStore, IdempotencyStore>()` — REQUIRED; placed just
  before the bridge registration. CrossChainBridgeService &
  BlockchainOperationManager now resolve; faucet resolves it per-call via
  `IServiceScopeFactory` (no faucet registration change — still singleton @130).
- Reconciliation: `AddOptions<ReconciliationOptions>().Bind(GetSection("Reconciliation"))`
  + `AddScoped<IReconciliationService, ReconciliationService>()` +
  `AddHostedService<ReconciliationHostedService>()`.
- `AddValidatorsFromAssemblyContaining<Program>()` CONFIRMED still present
  (Program.cs:34) — W4 validators auto-register, no change needed.
- `AddRateLimiter(...)` (built-in `Microsoft.AspNetCore.RateLimiting`, NO NuGet
  — Web shared framework). `app.UseRateLimiter()` placed AFTER UseAuthentication
  /UseAuthorization so the partition key can fall back to the authed avatar.

### Task 19 — InMemoryStorageProvider removed from production DI
Deleted the `new InMemoryStorageProvider()` singleton + its
`AddSingleton<IOASISStorageProvider>` registration. `EfStorageProvider`
(scoped) is now the SOLE registered `IOASISStorageProvider`; the
`ProviderContext` provider enumeration resolves to it deterministically. The
`InMemoryStorageProvider` CLASS is untouched (integration tests register it
explicitly). Nothing in Program.cs referenced the `inMemoryProvider` var
beyond that one registration line.

### Task 18 — rate limiting + per-API-key metering
- Partition key (most-specific wins): `apikey:{sha256(X-Api-Key)}` →
  `avatar:{sub}` → `ip:{remoteIp}`. Per-API-key metering = partition is 1:1
  with each key (hashed so the secret never lands in limiter state/logs).
- GLOBAL fixed-window limiter on all endpoints + STRICT named policy
  `"financial"` attached via `[EnableRateLimiting("financial")]` to: swap
  execute, wallet topup, bridge initiate/redeem/reverse.
- Config section `"RateLimiting"` (Enabled/Global/Financial → Permit/Window/
  Queue) added to appsettings.json with conservative fallbacks
  (global 120/60s, financial 10/60s). `"Reconciliation"` section also added
  with W5's defaults. Rejection = 429 + Retry-After header. `Enabled:false`
  ⇒ NoLimiter (kill switch).
- Deeper metering (usage counters/billing) noted as residual — current
  approach is partition-based metering, sufficient for abuse limiting.

### Task 10 — Idempotency-Key header threading (additive everywhere)
All controllers read `Request.Headers["Idempotency-Key"]` via a private
`ReadIdempotencyKey()` → trimmed string or **null** (NEVER a random per-request
key; absence stays dedup-safe via the W1/W3 deterministic content keys).
- **Faucet topup (real irreversible broadcaster):** WalletController.TopUp →
  `IWalletManager.TopUpAsync(..., string? clientIdempotencyKey=null)` →
  WalletManager picks `DispenseAsync(addr,amt,key)` (W3 keyed overload) when
  present, else the 2-arg deterministic overload. FULLY THREADED.
- **Bridge initiate/redeem/reverse (real irreversible broadcasters):**
  BridgeController → `ICrossChainBridgeService` (added optional
  `clientIdempotencyKey` to all 3 methods) → CrossChainBridgeService prefers
  the client key verbatim over its derived `bridge-*:{...}` key (also threaded
  into the 2 private initiate helpers). FULLY THREADED.
- **Swap execute:** returns an UNSIGNED tx (client signs+broadcasts) — no
  server-side irreversible effect. Header accepted + plumbed through
  `ISwapManager.GetSwapTransactionAsync(req, key=null)` for forward-compat/
  audit only; intentionally unused (`_ = clientIdempotencyKey;`). RESIDUAL:
  not a dedup gap (nothing irreversible server-side), just not yet consumed.

### Cross-worker compile fixes (every one)
1. `Validators/BridgeInitiateRequestValidator.cs` &
   `Validators/BridgeReverseRequestValidator.cs` — added
   `using OASIS.WebAPI.Controllers;`. W4 authored these validators against
   `BridgeInitiateRequest`/`BridgeReverseRequest` which live in the
   `OASIS.WebAPI.Controllers` namespace (bottom of BridgeController.cs) with
   no using ⇒ CS0246. Pre-existing W4↔controller integration gap, not caused
   by Wave-3 edits (DTOs were never moved).
2. `Services/CrossChainBridgeService.cs` (W1-owned) — additive optional
   `string? clientIdempotencyKey = null` on InitiateBridgeAsync,
   RedeemWithVAAAsync, ReverseBridgeAsync + the 2 private initiate helpers;
   prefer client key over the derived key. Minimal; default null is
   byte-for-byte the prior W1 behavior. This is the Task-10 wiring, not a
   feature change to W1 logic.

### Residual risk (Wave 3 surface)
- Swap-execute Idempotency-Key is accepted but not consumed (no server
  broadcast to dedupe; future server-broadcast swap path would wire it).
- No `wallet-transfer` endpoint exists in WalletController (only topup is a
  server-side broadcaster) — directive's "wallet transfer" path is N/A;
  nothing to wire.
- Rate-limiter state is in-memory per-instance (fixed-window, no distributed
  store) — horizontal scale-out would need a shared store; fine for
  single-instance pre-launch. Usage-metering is partition-based only (no
  persistent counters/billing).
- EF migration baseline caveat from Wave 1 still stands (a pre-existing
  `BridgeTransactions` table from `EnsureCreated` would collide with the
  migration's CreateTable — greenfield is fine; Program.cs calls
  `db.Database.Migrate()` on startup).
