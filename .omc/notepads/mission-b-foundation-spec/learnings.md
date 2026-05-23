# Mission B — W2 Manager Migrations (learnings)

## Phase-2 W2: ProviderContext → I*Store migration (8 managers)

- God→store name map applied verbatim:
  - `LoadXAsync(id)` → `GetByIdAsync(id, default)`
  - `LoadAllXsAsync()` → `GetAllAsync(default)` (or `QueryAsync(null, default)` for Holon)
  - `LoadAllHolonsAsync(q)` → `_holonStore.QueryAsync(q, default)`
  - `SaveXAsync(x)` → `UpsertAsync(x, default)`
  - `DeleteXAsync(id)` → `DeleteAsync(id, default)`
  - `LoadWalletsByAvatarAsync(id)` → `_walletStore.GetByAvatarAsync(id, default)`
  - `LoadBlockchainOperationsByAvatarAsync(id)` → `_blockchainOperationStore.GetByAvatarAsync(id, default)`
  - NFT: `Save*` → `Upsert*`, `Load*ById/ByTokenId/ByAvatarNFT` → `Get*`, `Delete*` unchanged.
- No manager method carried a real CancellationToken → all store calls pass `default`.
  Public manager signatures kept stable (only ctor changed) per DI-rewire-in-Phase-3.
- `Activate()` prologue removal is behavior-preserving: it only errored on
  "no provider available", impossible post-single-provider (EF always present).
- GOTCHA: `NftManager.MintAsync` set `holon.ProviderName = _providerContext.CurrentProvider.ProviderName`.
  No store method exposes provider identity. `EfStorageProvider.ProviderName` is the
  hardcoded literal `"PostgreSQL"` (EfStorageProvider.cs:21), so substituting the
  literal `"PostgreSQL"` is byte-identical behavior — did NOT invent a store method
  or edit an interface. Flagged for coordinator awareness.
- `IWalletStore` has no query method; `WalletManager.QueryAsync` keeps its existing
  client-side LINQ filter over `GetAllAsync()` (god `LoadAllWalletsAsync()` also had
  no query overload — already client-side, no behavior change).
- `BlockchainOperationManager` exactly-once/idempotency spine preserved verbatim;
  only the two `SaveBlockchainOperationAsync` persist calls swapped to
  `_blockchainOperationStore.UpsertAsync(operation, default)`. No safety assertion touched.
- `AvatarNFTService`: deleted the `NftProvider` cast member entirely (was
  `(IOASISStorageProviderNFTExtensions)_providerContext.CurrentProvider`); all calls
  now go through injected `INftStore` directly.
