# Subagent Output: reviewer
*2026-05-16T20:55:27.108Z*

## reviewer (user)

Now I have a comprehensive picture. Let me compile the full architecture review.

---

## Files Reviewed

- `Core/ProviderContext.cs` (1-103)
- `Core/Decorators/ProviderDecorator.cs` (1-96)
- `Core/Decorators/HealthRecordingProviderDecorator.cs` (1-88)
- `Core/ProviderSelection/HealthScoreStrategy.cs` (1-17)
- `Core/ProviderSelection/LowestLatencyStrategy.cs` (1-17)
- `Core/ProviderSelection/RoundRobinStrategy.cs` (1-27)
- `Core/ProviderSelection/StickySessionStrategy.cs` (1-56)
- `Core/ProviderSelection/WeightedStrategy.cs` (1-49)
- `Core/ProviderHealthMonitor.cs` (1-97)
- `Core/ProviderHealthScore.cs` (1-56)
- `Core/BlockchainProviderFactory.cs` (1-76)
- `Core/BlockchainNetworkConfig.cs` (1-35)
- `Core/DebugExceptionMiddleware.cs` (1-54)
- `Core/DynamicProviderMode.cs` (1-34)
- `Core/Blockchain/Base/BaseBlockchainProvider.cs` (1-187)
- `Core/Blockchain/Base/BlockchainConfigurationManager.cs` (1-75)
- `Core/Blockchain/JupiterConfig.cs` (1-18)
- `Core/Blockchain/TinymanV2PoolLocator.cs` (1-179)
- `Core/Blockchain/Wormhole/WormholeConfig.cs` (1-104)
- `Core/Blockchain/Wormhole/WormholeTypes.cs` (1-80)
- `Interfaces/IOASISStorageProvider.cs` (1-55)
- `Interfaces/IOASISStorageProviderNFTExtensions.cs` (1-28)
- `Interfaces/IBlockchainProvider.cs` (1-90)
- `Interfaces/IBlockchainProviderModule.cs` (1-55)
- `Interfaces/IProviderSelectionStrategy.cs` (1-22)
- `Interfaces/IProviderHealthMonitor.cs` (1-44)
- `Interfaces/ICrossChainBridgeService.cs` (1-55)
- `Interfaces/Managers/IDexAdapter.cs` (1-70)
- `Providers/EfStorageProvider.cs` (1-410)
- `Providers/InMemoryStorageProvider.cs` (1-246)
- `Managers/SwapManager.cs` (1-120)
- `Managers/Dex/JupiterDexAdapter.cs` (1-146)
- `Managers/Dex/TinymanDexAdapter.cs` (1-229)
- `Services/CrossChainBridgeService.cs` (1-295)
- `Services/WormholeAdapter.cs` (1-236)
- `Models/Responses/OASISResult.cs` (1-86)

---

## Critical (must fix)

### 1. **Security: Debug stack traces leak to clients in production**
- `OASISResult.cs:39-42` — `OASISResultDebug.Enabled` is a `static` mutable global toggled once at startup. The `Detail` property on `OASISResult<T>` emits full stack traces + inner exception chains whenever `Enabled` is true. The default in `appsettings.Development.json` is likely `true`, but there is no safeguard preventing this from being `true` in production if someone misconfigures. Worse, `DebugExceptionMiddleware.cs:47` calls `CaptureException` which always populates the `Exception` field — if debug is accidentally on, full stack traces go over the wire.

### 2. **Security: `BlockchainNetworkConfig.PrivateKey` stored in plaintext config**
- `Core/BlockchainNetworkConfig.cs:8` — `PrivateKey` is a `string?` on a POCO bound from `IConfiguration`. This means the private key lives in appsettings.json, environment variables, or other config providers in plaintext. No `SecureString`, no key vault integration, no `IOptionsMonitor` snapshot safety. Any logged config dump or error serialisation that touches this object leaks the key.

### 3. **Security: Wormhole VAA verification is a stub — no actual cryptographic verification**
- `Services/WormholeAdapter.cs:118-132` — `VerifyVAAAsync` checks only the signature count against a configurable minimum and the version byte. It explicitly says "For now, we trust the Guardian API response structure." This means anyone who can MITM the Guardian API or replay a VAA can mint arbitrary wrapped assets on the target chain. The bridge currently has **zero cryptographic verification**.

### 4. **Bug: `DeriveSequenceFromTxHash` is non-cryptographic and will produce collisions**
- `Services/WormholeAdapter.cs:211-216` — Uses `string.GetHashCode()` which is **not deterministic across process restarts** (.NET 5+ randomizes hash codes per-appdomain). A VAA sequence derived this way will not match between the server process that initiated the bridge and a restarted process trying to fetch the VAA. Cross-chain bridges will **break on server restart**.

### 5. **Bug: `BlockchainProviderFactory` stores singleton providers but doesn't handle concurrent access**
- `Core/BlockchainProviderFactory.cs:18-49` — `_activeProviders` is a plain `Dictionary<string, IBlockchainProvider>` with no locking. The factory is likely registered as Singleton, and `GetProvider` can be called concurrently from multiple requests, causing `KeyNotFoundException` or duplicate initialization.

### 6. **Bug: `ProviderContext.CurrentProvider` can be `null!` at runtime**
- `Core/ProviderContext.cs:18` — `CurrentProvider` is initialized to `null!` and only set after `Activate()` is called. If any code accesses `CurrentProvider` before `Activate()`, it gets a `NullReferenceException` with no guard or meaningful error message.

---

## Warnings (should fix)

### 7. **`IOASISStorageProvider` is a God Interface — 45+ methods**
- `Interfaces/IOASISStorageProvider.cs` + `IOASISStorageProviderNFTExtensions.cs` — The interface forces every provider to implement Avatar, Wallet, Holon, BlockchainOperation, STARODK, Quest, QuestTemplate, QuestNodeTemplate, AvatarNFT, HolonNFTBinding, and WalletNFTBinding operations. This is a clear ISP (Interface Segregation Principle) violation. Adding a new domain entity means touching every implementation and every decorator.

### 8. **Decorator explosion — every new method must be added to 3+ files**
- `ProviderDecorator.cs`, `HealthRecordingProviderDecorator.cs`, and the `IOASISStorageProvider` interface must all be updated in lockstep for any new method. With 45+ methods already, this is extremely fragile. A source generator or dynamic proxy (DispatchProxy / Castle DynamicProxy) would eliminate the boilerplate.

### 9. **`EfStorageProvider` does unsafe interface→concrete casts**
- `Providers/EfStorageProvider.cs` — Every `SaveXAsync` method casts from the interface to the concrete type, e.g., `(Avatar)avatar`, `(Wallet)wallet`, `(Holon)holon`. If an `IAvatar` implementation that isn't `Avatar` is passed, this throws `InvalidCastException` at runtime with no compile-time safety. The same applies to all `SetValues()` calls.

### 10. **`InMemoryStorageProvider` NFT stubs silently succeed on save, fail on read**
- `Providers/InMemoryStorageProvider.cs:195-210` — `SaveAvatarNFTAsync` returns success but data is discarded (not stored). `LoadAvatarNFTAsync` returns `IsError = true`. This means save→load round-trips silently lose data, which will cause confusing test failures or production bugs if InMemory is used in any non-trivial flow.

### 11. **`SwapManager._quoteCache` is a static mutable dictionary with no eviction beyond expiry**
- `Managers/SwapManager.cs:26-27` — The `_quoteCache` is `static`, meaning it's shared across all `SwapManager` instances and never fully cleared. Expired entries are only cleaned up on `CacheQuote` (insert), not on `TryGetCachedQuote` (read). Under low quote volume, stale entries accumulate indefinitely — a slow memory leak.

### 12. **`ProviderHealthScore` is a mutable POCO shared across threads**
- `Core/ProviderHealthMonitor.cs:25-55` — `RecordSuccess`/`RecordFailure` use `AddOrUpdate` which returns the same object reference that's in the `ConcurrentDictionary`. Multiple threads read/write fields like `SuccessCount`, `ConsecutiveFailures`, `IsHealthy` on the same `ProviderHealthScore` instance with no synchronization. This is a race condition that can corrupt health scores.

### 13. **`StickySessionStrategy.SessionKey` is mutable state on a strategy that's likely a singleton**
- `Core/ProviderSelection/StickySessionStrategy.cs:23` — `SessionKey` is a settable property. If the strategy is registered as a Singleton in DI (which it likely is), setting `SessionKey` before each `SelectProvider` call is not thread-safe. Concurrent requests will clobber each other's session keys, causing sticky sessions to route to the wrong provider.

### 14. **`WeightedStrategy` uses `System.Random` which is not thread-safe**
- `Core/ProviderSelection/WeightedStrategy.cs:16` — `_random = new Random()` is stored as an instance field. If the strategy is a singleton, concurrent calls to `SelectProvider` will corrupt the `Random` state. Should use `Random.Shared` (.NET 6+) or a thread-local `Random`.

### 15. **`CrossChainBridgeService.InitiateTrustedBridgeAsync` doesn't handle partial failure**
- `Services/CrossChainBridgeService.cs:218-260` — If `LockForBridgeAsync` succeeds but `MintWrappedAsync` fails, the source-chain asset is locked in the vault with no corresponding mint on the target chain and no compensating unlock. The bridge transaction is still saved with `Status = Completed`. This is a **fundamental consistency failure** in the trusted bridge flow.

### 16. **`CrossChainBridgeService.CompleteBridgeAsync` allows marking any-status bridge as completed**
- `Services/CrossChainBridgeService.cs:165-177` — The only guard is `if (Status == Completed) return Ok`. This means a bridge in `AwaitingVAA`, `Failed`, or `Redeeming` state can be forcibly marked `Completed` without any on-chain action. This is a state machine integrity violation.

### 17. **`BaseBlockchainProvider.IsRetryable` is too narrow**
- `Core/Blockchain/Base/BaseBlockchainProvider.cs:144-148` — Only retries on `HttpRequestException` with 5xx or 429. Network-level failures (DNS, connection refused, timeout) produce `HttpRequestException` with `StatusCode = null`, which IS handled. But `TaskCanceledException` from HTTP timeouts is not `HttpRequestException` and won't be retried — it will bubble up immediately.

### 18. **`TinymanDexAdapter` creates a new `HttpClient` per quote request**
- `Managers/Dex/TinymanDexAdapter.cs:124` — `using var client = new HttpClient { BaseAddress = ... }` inside `FetchTinymanPoolReservesAsync`. Creating and disposing `HttpClient` per request causes socket exhaustion under load. Should inject `IHttpClientFactory` or a typed client like `JupiterDexAdapter` does.

---

## Suggestions (consider)

### 19. **`ProviderContext.Activate()` mixes too many responsibilities**
- `Core/ProviderContext.cs:28-87` — The method handles explicit selection, dynamic selection via health monitor, dynamic selection via custom strategy, config default fallback, any-available fallback, failover list construction, and replication list construction. Consider extracting into a chain of responsibility or a `ProviderResolver` class.

### 20. **`OASISResultDebug.Enabled` as a static global is an anti-pattern**
- `Models/Responses/OASISResult.cs:12-15` — A static mutable boolean means any code anywhere can flip debug mode at runtime. This should be an `IOptionsMonitor<OASISDebugOptions>` injected via DI, or at minimum a property that logs a warning when changed after startup.

### 21. **Custom SHA-512/256 implementation in `TinymanV2PoolLocator` should be replaced**
- `Core/Blockchain/TinymanV2PoolLocator.cs:63-127` — This is a hand-rolled SHA-512/256 implementation (~100 lines of cryptographic code). .NET 9+ provides `SHA512_256.Create()` natively. For older .NET, BouncyCastle or `System.Security.Cryptography` with HMAC construction is preferable. Hand-rolled crypto is a correctness risk and harder to audit.

### 22. **`HealthRecordingProviderDecorator` should also override NFT extension methods**
- `Core/Decorators/HealthRecordingProviderDecorator.cs` — The decorator overrides all the base `ProviderDecorator` methods (Avatar, Wallet, Holon, etc.) with `TrackAsync`, but the NFT extension methods (SaveAvatarNFTAsync, LoadAvatarNFTAsync, etc.) from `IOASISStorageProviderNFTExtensions` are inherited from `ProviderDecorator` which just delegates to `Inner`. These NFT operations are not health-tracked, creating an inconsistent monitoring gap.

### 23. **`RoundRobinStrategy._index` will overflow eventually**
- `Core/ProviderSelection/RoundRobinStrategy.cs:15` — `_index` is an `int` that increments without bound. After ~2.1 billion selections it overflows to negative, causing `IndexOutOfRangeException` from the modulo on `healthy.Count`. Should use `Interlocked.Increment` on a `uint` or `long`, or reset to 0 periodically.

### 24. **`IBlockchainProvider` interface is very broad — 20+ methods**
- `Interfaces/IBlockchainProvider.cs` — This interface covers accounts, tokens, exchange, queries, contracts, chain info, and bridge operations all in one. Consider splitting into `IAccountModule`, `ITokenModule`, `IExchangeModule`, `IContractModule`, `IBridgeModule` — similar to the `IBlockchainProviderModule` pattern already in the codebase.

### 25. **`CrossChainBridgeService.GetSupportedRoutesAsync` generates O(n²) routes**
- `Services/CrossChainBridgeService.cs:183-220` — For every pair of enabled providers, it creates a route entry. With many chains, this produces a large response. More importantly, it doesn't check if the provider actually supports the specific asset type being bridged — it just adds all combinations.

### 26. **Consider making `ProviderContext` immutable after `Activate()`**
- `Core/ProviderContext.cs` — `CurrentProvider` and `AllActiveProviders` are publicly settable. This means any code with a reference to `ProviderContext` can swap the active provider without going through `Activate()`. These should be `init`-only or have private setters with the `Activate()` method being the sole mutator.

### 27. **`WormholeConfig` has hardcoded mainnet contract addresses**
- `Core/Blockchain/Wormhole/WormholeConfig.cs:27-44` — Mainnet bridge/validator addresses are hardcoded as defaults. If Wormhole upgrades contracts, the code must be recompiled. These should live purely in configuration and have no code-level defaults for mainnet.

---

## Summary

The OASIS WebAPI project implements a sophisticated multi-provider architecture with proper separation of concerns: strategy pattern for provider selection, decorator pattern for cross-cutting health tracking, adapter pattern for chain-specific DEX integrations, and a factory for blockchain provider resolution. The design is ambitious and largely well-structured.

However, the implementation has several **critical safety gaps**: the Wormhole bridge verification is a stub with no actual cryptographic proof checking, the trusted bridge flow has no compensating transactions on partial failure, the sequence derivation for VAAs is non-deterministic across restarts, and `BlockchainNetworkConfig.PrivateKey` is stored as plaintext. Additionally, the `IOASISStorageProvider` God Interface with 45+ methods creates a maintenance burden where every new domain entity ripples through providers, decorators, and tests. Addressing the security and concurrency issues should be the top priority before any production deployment.

### stderr

ℹ️ [project-memory] Memory loaded on session start {"total":85}
ℹ️ [image-gen] Background poller stopped
ℹ️ [image-gen] Cleaned up on session shutdown


---
