# Subagent Output: planner
*2026-05-16T03:05:38.269Z*

## planner (user)

Now I have a thorough understanding of the codebase. Let me compile the plan.

---

## Goal

Harden the OASIS blockchain Web API across 7 priority tracks: migrate deprecated APIs, fix endpoints, eliminate fake transaction hashes, implement real swap logic, persist bridge state, fix wallet crypto, and complete configuration.

## Plan

### Track 1: Jupiter API Migration (CRITICAL)

1. **Update `SwapManager.cs` → `GetJupiterQuoteAsync`**: Replace `https://quote-api.jup.ag/v6/quote` with `https://api.jup.ag/swap/v2/quote`. Remove the fallback `api.mainnet-beta.solana.com` endpoint (was a sanity check, not useful). Add `x-api-key` header to the request from configuration.

2. **Update `SwapManager.cs` constructor**: Add `IConfiguration` (or `IOptions<JupiterConfig>`) parameter to receive the Jupiter API key. Inject via DI.

3. **Add Jupiter config model**: Create `JupiterConfig` in `Core/Blockchain/` with `ApiKey` and `BaseUrl` properties. Bind from `Blockchain:Jupiter` in appsettings.

4. **Update `SwapQuoteRequest.cs`**: Add optional `WalletAddress` property (Jupiter v2 `/swap` endpoint needs it for swap transaction building, not quote — but useful for downstream swap execution).

5. **Update `SwapQuoteResponse.cs`**: Add `SwapTransaction` (base64 serialized transaction for client-side signing) and `QuoteId` fields. Jupiter v2 `/swap` returns a `swapTransaction` field.

6. **Add `GetSwapTransactionAsync` method to `SwapManager`**: New method to call `POST https://api.jup.ag/swap/v2/swap` with the quote response, user public key, and API key. Returns the unsigned serialized transaction for client-side signing.

7. **Update `ISwapManager` interface**: Add the new `GetSwapTransactionAsync` method signature.

8. **Update `Program.cs` DI**: Pass `IConfiguration` (or `IOptions<JupiterConfig>`) into `SwapManager` via the `AddHttpClient` factory. Add `x-api-key` default header to the HttpClient.

9. **Update `appsettings.json`**: Add `Blockchain:Jupiter` section with `ApiKey` and `BaseUrl`.

10. **Update `SwapController.cs`**: Add `POST /api/swap/execute` endpoint that calls `GetSwapTransactionAsync`.

### Track 2: Wormhole Guardian API Fix

1. **Update `WormholeConfig.cs`**: Change `GuardianRpcUrl` default from `"https://wormhole-v2-mainnet-api.certus.one"` to `"https://api.wormholescan.io"`.

2. **Update `WormholeAdapter.cs` → `FetchVAAAsync`**: Change endpoint from `/v1/signed_vaa/{emitterChainId}/{emitterAddress}/{sequence}` to `/v1/signed_vaa/{emitterChainId}/{emitterAddress}/{sequence}` (same path structure but different base URL). Verify the WormholeScan API response DTO matches — it wraps `vaaBytes` in a different envelope. Update `GuardianVAAEnvelope` / `GuardianSignedVAAResponse` DTOs to match WormholeScan's actual response format: `{ "vaaBytes": "..." }` (the WormholeScan API returns `vaaBytes` at top level, not nested in `data`).

3. **Update `Program.cs`**: The `AddHttpClient` for `IWormholeAdapter` already reads `GuardianRpcUrl` from config, so changing the config default propagates. Verify no other code hardcodes the old domain.

4. **Update `appsettings.json`**: Add `Blockchain:Wormhole:GuardianRpcUrl` explicit value set to `"https://api.wormholescan.io"`.

### Track 3: Remove All Guid.NewGuid() Fake Transactions

There are 12 fake tx sites total (9 in SolanaProvider, 3 in AlgorandProvider):

**SolanaProvider.cs** (9 sites):
- Line 125: `MintAsync` → `"sol_mint_{Guid.NewGuid():N}"`
- Line 133: `BurnAsync` → `"sol_burn_{Guid.NewGuid():N}"`
- Line 142: `TransferAsync` → `"sol_transfer_{Guid.NewGuid():N}"`
- Line 323: `LockForBridgeAsync` → `"bridge_lock_{Guid.NewGuid():N}"`
- Line 332: `MintWrappedAsync` → `"sol_wrap_{Guid.NewGuid():N}"`
- Line 341: `BurnWrappedAsync` → `"sol_burn_wrap_{Guid.NewGuid():N}"`
- Line 358: `CreateMetadataAccountAsync` → `"sol_meta_{Guid.NewGuid():N}"`
- Line 375: `CreateTokenAccountAsync` → `"sol_ta_{Guid.NewGuid():N}"`
- Line 383: `CloseTokenAccountAsync` → `"sol_close_{Guid.NewGuid():N}"`

**AlgorandProvider.cs** (3 sites):
- Line 328: `LockForBridgeAsync` → `"bridge_lock_{Guid.NewGuid():N}"`
- Line 348: `BurnWrappedAsync` → `"bridge_burn_{Guid.NewGuid():N}"`
- Line 373: `CreateASAAsync` → `"asa_create_{Guid.NewGuid():N}"`

**Plan:**

1. **Create a `PendingOperationResult` model** in `Models/Responses/`: A new response type with fields: `OperationId` (deterministic, e.g., `{chain}_{operationType}_{timestamp}_{hashOfParams}`), `Status` = `"RequiresClientSignature"`, `OperationType` (e.g., "mint", "transfer"), `Chain` (e.g., "solana", "algorand"), `UnsignedTransactionPayload` (base64 encoded unsigned tx data for client-side signing), `Message` (human-readable instructions).

2. **Create `OperationIdGenerator` utility** in `Core/`: Generates deterministic operation IDs using `SHA256(chain + operationType + parameters + timestamp)` — traceable but not a fake tx hash. Format: `op_{chain}_{opType}_{first12OfHash}`.

3. **Update `IBlockchainProvider` interface**: The return type `OASISResult<string>` for mint/burn/transfer/bridge methods stays as `string`, but the semantics change — the string is now an operation ID (not a fake tx hash). Update XML doc comments to clarify.

4. **Update `SolanaProvider.cs`**: Replace all 9 `Guid.NewGuid()` returns with deterministic operation IDs from `OperationIdGenerator`. Change each method to:
   - Record the operation parameters
   - Return `Ok(operationId, "Operation requires client-side signing. Use the operation ID to submit the signed transaction.")`
   - Remove the `_{Guid.NewGuid():N}` pattern

5. **Update `AlgorandProvider.cs`**: Replace all 3 `Guid.NewGuid()` returns with deterministic operation IDs. Same pattern as Solana.

6. **Update `CrossChainBridgeService.cs`**: The `Id` fields on `BridgeTransactionResult` already use `Guid.NewGuid()` for bridge IDs (lines 277, 330) — these are legitimate internal IDs, not fake tx hashes. Keep them but add a comment. The `LockTxHash` and `MintTxHash` fields that get the fake operation IDs from providers should now receive deterministic operation IDs, which is correct — they're operation references, not real tx hashes.

7. **Update `BlockchainOperationManager.cs`**: In `ApplyChainResult`, update the `TxHash` parameter key to be `OperationId` when the result indicates `RequiresClientSignature`. Add a check: if the result message contains "RequiresClientSignature", set status to `"AwaitingSignature"` instead of `"Completed"`.

8. **Update tests**: Fix any tests in `OASIS.WebAPI.Tests/Services/CrossChainBridgeServiceTests.cs` that assert on fake tx hash format.

### Track 4: Real Swap Implementation

1. **Add Tinyman pool lookup via Algod API** in `SwapManager.cs` → `GetTinymanQuoteAsync`:
   - Replace hardcoded `reserveIn`/`reserveOut` (1T/1T) with real Algod API calls.
   - Fetch Tinyman V2 pool app state via `GET /v2/applications/{poolAppId}?include=true` on the Algod client.
   - Parse key-value state: `a1` (asset1 reserve), `a2` (asset2 reserve), `asset_1_id`, `asset_2_id`.
   - Use Tinyman master app ID (testnet: `148607000`, mainnet configurable) to discover pool via `ApplicationIDLookup`.
   - Inject `IConfiguration` for algod URL/api-token.

2. **Add Algod pool lookup helper**: New private method `GetTinymanPoolReservesAsync(uint asset1Id, uint asset2Id)` that:
   - Computes the expected pool app ID from asset IDs (Tinyman uses `sha256(asset1Id || asset2Id || masterAppId)` for pool app ID derivation — or looks up via indexer).
   - Falls back to direct application state lookup if pool ID is known.
   - Returns `(ulong reserveIn, ulong reserveOut)`.

3. **Add Jupiter v2 `/swap` integration**: Already covered in Track 1 step 6. The `GetSwapTransactionAsync` method calls `POST https://api.jup.ag/swap/v2/swap` with `{ quoteResponse, userPublicKey, wrapAndUnwrapSol: true }` and returns `{ swapTransaction, lastValidBlockHeight }`.

4. **Add Jupiter v2 response DTOs**: New classes in `Models/Responses/` or internal to `SwapManager`:
   - `JupiterSwapRequest`: `quoteResponse` (object), `userPublicKey` (string), `wrapAndUnwrapSol` (bool)
   - `JupiterSwapResponse`: `swapTransaction` (string, base64), `lastValidBlockHeight` (long), `prioritizationFeeLamports` (object?)

5. **Add `SwapExecuteRequest` model** in `Models/Requests/`: `QuoteId`, `WalletAddress`, `Chain`.

6. **Update `SwapQuoteResponse.cs`**: Add `QuoteId` field to tie quote to subsequent swap execution.

### Track 5: Bridge Service Hardening

1. **Create `BridgeTransaction` EF entity** in `Models/`: Map `BridgeTransactionResult` to a proper EF Core entity with:
   - `Id` (string, PK)
   - `AvatarId` (Guid, indexed)
   - `SourceChain`, `TargetChain`, `SourceTokenId`, `TargetTokenId` (strings)
   - `SourceAddress`, `TargetAddress` (strings, max 512)
   - `Amount` (int)
   - `Status` (enum → int conversion)
   - `Mode` (enum → int conversion)
   - `LockTxHash`, `MintTxHash`, `RedemptionTxHash` (strings, max 256)
   - `ProofData`, `ErrorMessage` (strings, max 2048)
   - `WormholeEmitterChainId` (int?), `WormholeEmitterAddress` (string, max 128), `WormholeSequence` (long?)
   - `VaaBytes` (string, max 4096), `VaaSignatureCount` (int?)
   - `CreatedAt`, `CompletedAt` (DateTime)

2. **Add `DbSet<BridgeTransaction>` to `OASISDbContext`**: Register entity + configure indexes (AvatarId, Status, SourceChain+TargetChain composite).

3. **Add EF migration**: Run `dotnet ef migrations add AddBridgeTransaction`.

4. **Update `CrossChainBridgeService.cs`**:
   - Replace `Dictionary<string, BridgeTransactionResult> _bridgeTransactions` with `OASISDbContext` injection (scoped).
   - Convert all dictionary lookups to EF queries (`_db.BridgeTransactions.FindAsync(id)`, `_db.BridgeTransactions.Where(t => t.AvatarId == avatarId)`).
   - Change service lifetime from `Singleton` to `Scoped` in `Program.cs` (required for DbContext).
   - Add `SaveChangesAsync()` calls after mutations.

5. **Add bridge vault address configuration** to `WormholeConfig.cs`: Add `Dictionary<string, BridgeVaultConfig> BridgeVaults` with per-chain vault addresses. E.g., `"Solana": { "VaultAddress": "..." }`, `"Algorand": { "VaultAddress": "..." }`.

6. **Update `CrossChainBridgeService.GetBridgeVaultAddress`**: Read from `_wormholeConfig.BridgeVaults` instead of the current placeholder `$"{sourceChain}_bridge_vault_for_{targetChain}"`.

7. **Fix `WormholeConfig.cs` devnet/testnet addresses**: Add `TokenBridgeAddress` and `NftBridgeAddress` for both Solana and Algorand mappings. Add separate mapping entries for devnet vs mainnet (currently only one set). Either:
   - Add `DevnetChainMappings` and `MainnetChainMappings` dictionaries, or
   - Add a `Network` field to `WormholeChainMapping` with different addresses per network.
   - Correct values:
     - Solana mainnet Core Bridge: `worm2ZoG2kUd4vFXhvjh93UUH596ayRfgQ2MgjNMTth` (correct)
     - Solana devnet Core Bridge: `3u8hJUVTA4jH1wYAyUpm7MKgCtsKZ7rqWmMNEhQ5Y8Ru`
     - Algorand mainnet Token Bridge app ID: `842125965` (Core), `1088520416` (Token Bridge)
     - Algorand testnet Core Bridge: `7chg4MP27tAVdLb5qxW4K2qqL3jexaKX5vjVvMgW6m4L`

8. **Update `appsettings.json`**: Add `Blockchain:Wormhole` section with proper devnet/testnet addresses and `BridgeVaults` config.

### Track 6: Wallet Crypto Fix

1. **Add `NBitcoin` NuGet package** to `OASIS.WebAPI.csproj`: NBitcoin provides proper secp256k1 (Ethereum) and can work with BIP39 mnemonics. Alternatively, add `BouncyCastle.Cryptography` for Ed25519 + secp256k1. The project already has `Algorand2` package which includes Ed25519 support.

2. **Update `WalletKeyService.cs` → `DeriveEd25519PublicKey`**:
   - Replace HMAC-SHA512 placeholder with proper Ed25519 key derivation using `Algorand2` SDK (already in project) or `BouncyCastle`.
   - Use `Algorand.Algod.Ed25519` or `NaCl` via BouncyCastle: `new Ed25519PrivateKeyParameters(seed, 0)` → derive public key.

3. **Update `WalletKeyService.cs` → `GenerateAlgorandKeypair`**:
   - Use `Algorand2` SDK: `Algorand.Algod.Ed25519` or the SDK's `Account` class to generate proper Algorand keypairs.
   - `var account = new Algorand.Account();` → `account.Address.ToString()` for address, proper 25-word mnemonic.

4. **Update `WalletKeyService.cs` → `GenerateSolanaKeypair`**:
   - Use `Solana.Wallet` SDK (already in project at v8.7.0): `new Solana.Wallet.Wallet()` generates proper Ed25519 Solana keypairs with BIP39 mnemonic.
   - `var wallet = new Solana.Wallet.Wallet(Mnemonic.Generate(WordCount.Twelve));`

5. **Update `WalletKeyService.cs` → `DeriveSecp256k1PublicKey`**:
   - Replace HMAC-SHA256 placeholder with `NBitcoin` or `BouncyCastle` secp256k1 scalar multiplication.
   - Using NBitcoin: `new Key(privateKeyBytes).PubKey.ToBytes()` gives the 65-byte uncompressed public key.

6. **Update `WalletKeyService.cs` → `GenerateEthereumKeypair`**:
   - Use `NBitcoin`: `var key = new Key();` → proper secp256k1 key generation.
   - For address: Keccak256 of the 64-byte uncompressed public key (minus 0x04 prefix), take last 20 bytes.
   - Use `NBitcoin`'s `Hashes.SHA256` + Keccak or add `Nethereum.Util.Sha3Keccack` — or implement keccak256 via BouncyCastle.

7. **Update `WalletKeyService.cs` → `EthereumAddressFromPublicKey`**: Replace SHA-256 fallback with keccak256. Use BouncyCastle's `KeccakDigest` (256-bit).

8. **Update `WalletKeyService.cs` → `AlgorandAddressFromPublicKey`**: Fix to use SHA-512/256 (not SHA-512). Algorand uses `SHA-512/256` for the checksum. Update: `using var sha = SHA512.Create()` → use `SHA512.HashData` with truncation, or use `SHA256` on the SHA-512 output (SHA-512/256 = first 32 bytes of SHA-512).

9. **Update `WalletKeyService.cs` → `GenerateMnemonic`**: Replace the simplified wordlist-based mnemonic with proper BIP39. Either use `NBitcoin`'s `Mnemonic` class or `Algorand2`'s mnemonic support.

### Track 7: Configuration Completeness

1. **Add `IndexerUrl` for Algorand** in `appsettings.json`:
   - Devnet: `"IndexerUrl": "https://testnet-idx.algonode.cloud"`
   - Testnet: `"IndexerUrl": "https://testnet-idx.algonode.cloud"`
   - Mainnet: `"IndexerUrl": "https://mainnet-idx.algonode.cloud"`
   - The `BlockchainNetworkConfig` model already has `IndexerUrl` property — it just needs values in config.

2. **Add Jupiter API key config** in `appsettings.json`:
   - Add `"Blockchain:Jupiter": { "ApiKey": "", "BaseUrl": "https://api.jup.ag" }`
   - Add `JupiterConfig` class (Track 1 step 3).

3. **Add bridge vault address config** in `appsettings.json`:
   - Add `"Blockchain:Wormhole:BridgeVaults"` section (Track 5 step 8).
   - Per-chain vault addresses: initially placeholder format `"CHANGE_ME_{chain}_vault_address"` with comments.

4. **Add `Wormhole` config section** to `appsettings.json` (if not already bound):
   - `"Blockchain:Wormhole": { "GuardianRpcUrl": "https://api.wormholescan.io", "DefaultMode": "Wormhole", "VaaTimeoutSeconds": 120, "VaaPollIntervalMs": 2000, "MinGuardianSignatures": 13, "ChainMappings": { ... }, "BridgeVaults": { ... } }`

## Files to Modify

- **`Managers/SwapManager.cs`** — Jupiter v2 endpoint, API key header, Tinyman real pool lookup, new `GetSwapTransactionAsync`
- **`Interfaces/Managers/ISwapManager.cs`** — Add `GetSwapTransactionAsync` signature
- **`Services/WormholeAdapter.cs`** — Update VAA fetch endpoint base URL, fix response DTO
- **`Core/Blockchain/Wormhole/WormholeConfig.cs`** — Change default URL, add bridge vaults, add per-network chain mappings
- **`Core/Blockchain/Wormhole/WormholeTypes.cs`** — Update `GuardianVAAEnvelope` DTO for WormholeScan API format
- **`Providers/Blockchain/Solana/SolanaProvider.cs`** — Replace 9 `Guid.NewGuid()` fake tx sites with deterministic operation IDs
- **`Providers/Blockchain/Algorand/AlgorandProvider.cs`** — Replace 3 `Guid.NewGuid()` fake tx sites with deterministic operation IDs
- **`Services/CrossChainBridgeService.cs`** — Replace in-memory Dictionary with EF persistence, use config vault addresses
- **`Core/WalletKeyService.cs`** — Replace HMAC placeholders with real Ed25519/secp256k1, fix Algorand checksum, keccak256 for ETH
- **`Data/OASISDbContext.cs`** — Add `DbSet<BridgeTransaction>` and entity configuration
- **`Models/Requests/SwapQuoteRequest.cs`** — Add `WalletAddress` optional field
- **`Models/Responses/SwapQuoteResponse.cs`** — Add `QuoteId`, `SwapTransaction` fields
- **`Models/Responses/BridgeTransactionResult.cs`** — Convert to EF entity (add PK attribute, column lengths)
- **`Controllers/SwapController.cs`** — Add `POST /execute` endpoint
- **`Controllers/BridgeController.cs`** — Minor: ensure compatibility with new operation ID semantics
- **`Managers/BlockchainOperationManager.cs`** — Update `ApplyChainResult` to handle `AwaitingSignature` status
- **`Program.cs`** — Add Jupiter config binding, update Wormhole config, change bridge service to Scoped, add BouncyCastle/NBitcoin DI
- **`OASIS.WebAPI.csproj`** — Add `BouncyCastle.Cryptography` (or `NBitcoin`) package reference
- **`appsettings.json`** — Add IndexerUrl, Jupiter config, Wormhole config, BridgeVaults

## New Files

- **`Core/Blockchain/JupiterConfig.cs`** — Jupiter API configuration model (ApiKey, BaseUrl)
- **`Core/OperationIdGenerator.cs`** — Deterministic operation ID generation utility
- **`Models/Responses/PendingOperationResult.cs`** — Structured response for unsigned operations awaiting client signature
- **`Models/Requests/SwapExecuteRequest.cs`** — Request DTO for swap execution (QuoteId, WalletAddress, Chain)
- **`Data/Migrations/<timestamp>_AddBridgeTransaction.cs`** — EF migration for BridgeTransaction table (auto-generated)

## Risks

1. **Jupiter v2 API key requirement**: The free tier may have rate limits. The API key must be configured or the endpoint returns 401. Add graceful fallback / clear error message if key is missing.

2. **WormholeScan API response format**: The `/v1/signed_vaa` endpoint may have a different response envelope than the old certus.one API. Must test against live API or verify documentation. The current `GuardianVAAEnvelope` wrapper (`data.vaaBytes`) may need to change to top-level `vaaBytes`.

3. **Breaking API contract change (Track 3)**: Changing return values from fake tx hashes to operation IDs is a semantic breaking change for any client that parses the `Result` string as a transaction hash. The `Message` field already says "requires client-side signing" but clients may rely on the format. Consider versioning or adding a `ResultType` discriminator field.

4. **Tinyman pool discovery**: Deriving pool app IDs from asset IDs requires knowing the Tinyman master app ID and using SHA-256 based app ID derivation. This is testnet/mainnet specific and could break if Tinyman upgrades contracts. Consider a fallback to indexer search.

5. **Wallet crypto migration**: Switching from HMAC-derived keys to real Ed25519/secp256k1 means **existing wallets will have incompatible keys**. Any encrypted private keys in the database generated with the old code cannot be used with real crypto. Need a migration strategy (re-generate wallets, or version the key format).

6. **Bridge service singleton→scoped**: Changing `CrossChainBridgeService` from Singleton to Scoped may affect concurrent access patterns. The in-memory dictionary was thread-safe by virtue of concurrent requests mutating the same dict. With EF/Scoped, each request gets its own DbContext — this is correct but requires proper transaction handling for status updates.

7. **Algorand2 SDK compatibility**: The `Algorand2` NuGet package (v2.0.0.2024051911) may have API differences from expected. Verify that `Account` generation and Ed25519 key derivation work with this specific version.

8. **BouncyCastle vs NBitcoin choice**: Both provide secp256k1. NBitcoin is lighter for Bitcoin/Ethereum. BouncyCastle provides Ed25519 natively. Since `Algorand2` and `Solana.Wallet` already handle Ed25519, NBitcoin may suffice for secp256k1 + keccak256. Recommendation: **NBitcoin** (smaller dependency surface).

### stderr

ℹ️ [project-memory] Memory loaded on session start {"total":82}
ℹ️ [image-gen] Background poller stopped
ℹ️ [image-gen] Cleaned up on session shutdown


---
