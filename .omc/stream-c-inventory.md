# Stream C: DbContext Access Inventory
**Target Services**: ReconciliationService.cs + CrossChainBridgeService.cs  
**Goal**: Map all OASISDbContext accesses to IBridgeStore + IIdempotencyStore  
**Status**: Complete mapping — NEW METHODS required before refactor

## ReconciliationService.cs

| Line | Query Shape | IBridgeStore Method | Status |
|------|-------------|---------------------|--------|
| 86-92 | Select non-terminal bridge IDs, batch, order by age | GetNonTerminalBridgeIdsAsync | COVERED |
| 123-125 | AnyAsync bridge exists by id | MISSING | ADD ExistsByIdAsync |
| 147-149 | FirstOrDefaultAsync bridge by id | GetBridgeAsync | COVERED |
| 215-223 | WHERE id+status ExecuteUpdateAsync: Status->next, CompletedAt=UtcNow conditional | TryTransitionBridgeStatusAsync with SetCompletedAtUtcNow | COVERED |
| 254-260 | WHERE id+status ExecuteUpdateAsync: Status->Failed, ErrorMessage, CompletedAt=UtcNow | TryTransitionBridgeStatusAsync | COVERED |
| 354-360 | Select non-terminal operation IDs, batch, order by age | GetNonTerminalOperationIdsAsync | COVERED |
| 388-390 | FirstOrDefaultAsync operation by id | GetOperationAsync | COVERED |
| 435-440 | WHERE id+status ExecuteUpdateAsync: Status->Completed, CompletedDate=UtcNow | TryTransitionOperationStatusAsync | COVERED |
| 465-470 | WHERE id+status ExecuteUpdateAsync: Status->Failed, CompletedDate=UtcNow | TryTransitionOperationStatusAsync | COVERED |
| 612,634,642 | IIdempotencyStore calls (GetAsync, CompleteAsync, FailAsync) | Already injected separately | COVERED |

## CrossChainBridgeService.cs

| Line | Query Shape | IBridgeStore Method | Status |
|------|-------------|---------------------|--------|
| 85,129,404,422,594 | FindAsync (tracked load) by PK | GetBridgeAsync + consider tracked overload | REQUIRES CARE |
| 107,116,411-413 | Tracked entity mutation + SaveChangesAsync | MISSING SaveBridgeAsync OR use conditional update | ADD OR REFACTOR |
| 174,198,327,447,509,530 | Entry(tx).ReloadAsync after ExecuteUpdateAsync | MISSING ReloadBridgeAsync | ADD |
| 189-193 | WHERE id+status ExecuteUpdateAsync: VAAReady->Redeeming, IdempotencyKey | TryTransitionBridgeStatusAsync | COVERED |
| 214-222 | ConsumedVaas.Add + SaveChangesAsync with constraint catch | TryInsertConsumedVaaAsync | COVERED |
| 244,282 | FailRedeemAsync (ExecuteUpdateAsync Redeeming->Failed) | TryTransitionBridgeStatusAsync | COVERED |
| 248-249,366 | AsNoTracking FirstOrDefaultAsync bridge by id for idem settlement | GetBridgeAsync | COVERED |
| 300-306 | WHERE id+status ExecuteUpdateAsync: Redeeming->Completed, 3 fields, CompletedAt | TryTransitionBridgeStatusAsync | COVERED |
| 463-467 | WHERE id+status ExecuteUpdateAsync: Completed->Reversing, IdempotencyKey | TryTransitionBridgeStatusAsync | COVERED |
| 502-507 | WHERE id+status ExecuteUpdateAsync: Reversing->Refunded, 2 fields, CompletedAt | TryTransitionBridgeStatusAsync | COVERED |
| 524-528 | WHERE id+status ExecuteUpdateAsync: Reversing->Failed, ErrorMessage | TryTransitionBridgeStatusAsync | COVERED |
| 541-544 | OrderByDescending(CreatedAt) GetBridgeHistoryAsync | GetBridgeHistoryAsync uses ASC in EfBridgeStore | MISMATCH |

## MISSING METHODS — ADD TO IBridgeStore

```csharp
// 1. Existence check (ReconciliationService line 123-125)
Task<bool> ExistsByIdAsync(string id, CancellationToken ct = default);

// 2. Reload after conditional update (CrossChainBridgeService lines 174, 198, 327, 447, 509, 530)
Task<BridgeTransactionResult?> ReloadBridgeAsync(string id, CancellationToken ct = default);

// 3. Save tracked entity mutations (CrossChainBridgeService lines 107, 116, 411-413)
//    ALTERNATIVE: Refactor service to use conditional updates instead
Task SaveBridgeAsync(BridgeTransactionResult tx, CancellationToken ct = default);

// 4. Fix sort order (CrossChainBridgeService line 541-544)
//    Current EfBridgeStore returns ASC; service expects DESC
//    Add parameter: Task<IReadOnlyList<BridgeTransactionResult>> 
//                   GetBridgeHistoryAsync(Guid avatarId, bool descending = false, CancellationToken ct = default);
```

## Design Recommendations

**Option A (RECOMMENDED)**: Eliminate tracked entities
- Replace all FindAsync + mutation patterns with conditional updates
- Delete SaveBridgeAsync and ReloadBridgeAsync from scope
- Matches ReconciliationService's proven pattern
- Reduces EF change-tracker complexity

**Option B (Hybrid)**: Support tracked entities
- Add SaveBridgeAsync, ReloadBridgeAsync, and tracked GetBridgeAsync overload
- EF impl: straightforward (SaveChangesAsync, Entry.ReloadAsync)
- Surreal impl: throw NotSupportedException
- Requires service to handle or fallback to conditional updates

**Minimum additions if Option B**:
1. ExistsByIdAsync
2. ReloadBridgeAsync
3. SaveBridgeAsync
4. Fix GetBridgeHistoryAsync sort order

## Behavior Contracts to Preserve

- All ExecuteUpdateAsync calls return affected-row count VERBATIM (0=race lost, 1=won)
- IBridgeStore methods NEVER assert==1, retry, or RMW
- Optimistic concurrency via WHERE clause predicates
- IIdempotencyStore remains decoupled and independent

