# SurrealDB Entity Convention

**Status:** Adopted 2026-05-22 (`oasis-sleek` `api-safety-hardening` branch).
**Scope:** All SurrealDB-backed entities (`Persistence/SurrealDb/Schemas/source/*.mermaid`).
**Applies to:** New entities going forward + existing entities as they cut over inside `surrealdb-migration` wave-2.

---

## 1. Schema is the source of truth

Every SurrealDB-backed entity lives as a `.mermaid` file in
`Persistence/SurrealDb/Schemas/source/`. The Roslyn source generator
([packages/Oasis.SurrealDb.SourceGen](../../packages/Oasis.SurrealDb.SourceGen))
emits a partial POCO into `OASIS.WebAPI.Generated.SurrealDb.<Entity>`
at build time from each `.mermaid`.

**No hand-written entity classes** for persisted aggregates. If you need
a property the generator does not emit, you have one of three legitimate
patterns (see §3); none of them involves a parallel hand-written class.

---

## 2. Why this convention

1. **One canonical type per aggregate.** Eliminates the
   `OASIS.WebAPI.Models.Quest.Quest` vs
   `OASIS.WebAPI.Generated.SurrealDb.Quest` ambiguity tax. Every
   cross-aggregate file that ever needed both paid this tax with `using
   *Def = ...` aliases; the convention eliminates it for new work.
2. **Schema drift becomes a compile error**, not a runtime SurrealQL
   parse failure. Adding a column to the `.mermaid` regenerates the
   POCO; renaming or removing one breaks every caller immediately.
3. **The shape mismatches** between SurrealDB storage form and
   caller-friendly C# types (Guid('N') hex string ⇄ `Guid`, `JsonElement`
   ⇄ `IDictionary<string,string>`, `DateTimeOffset` ⇄ `DateTime`) all
   have a single place to live -- partial-class accessors (§3.1) -- so
   the conversion logic doesn't scatter across managers.

---

## 3. The three legitimate patterns for additional members

### 3.1 Partial-class accessors -- **the default**

Generated POCOs are emitted as `partial class`. Add a sibling file that
declares the same partial in **the same namespace**
(`OASIS.WebAPI.Generated.SurrealDb`) regardless of file location:

```csharp
// File: Models/DappComposition/DappSeriesExtensions.cs
namespace OASIS.WebAPI.Generated.SurrealDb;

public partial class DappSeries
{
    /// <summary>Caller-friendly Guid view of the storage-side Id (Guid('N') hex).</summary>
    public Guid IdGuid
    {
        get => Guid.ParseExact(Id, "N");
        set => Id = value.ToString("N");
    }

    /// <summary>Caller-friendly Dictionary view of the storage-side SharedConfig JsonElement.</summary>
    public IDictionary<string, string> SharedConfigDict
    {
        get => SharedConfig.ToStringDictionary();
        set => SharedConfig = value.ToJsonElement();
    }

    public bool OwnedBy(Guid avatarId) => AvatarId == avatarId.ToString("N");
}
```

**Use partial accessors for:**

- Guid ⇄ string("N") conversions on `id` / `*_id` fields.
- `IDictionary<string,string>` views over `object` fields stored as
  `JsonElement`.
- `DateTime` views over `DateTimeOffset` fields when the caller pays
  for the conversion (be careful: lossy if local-time semantics differ).
- Domain predicates (`OwnedBy(...)`, `IsActive`, `IsTerminal`,
  `BelongsToSeries(...)`).
- Static factories that produce a populated POCO from
  caller-friendly inputs.

**Avoid:**

- Properties that would change the persistence shape (add those as new
  `.mermaid` fields; the generator handles it).
- Heavy domain logic (that belongs in the manager).
- Anything that touches another aggregate (composite projections are
  request/response DTOs -- see §3.3).

### 3.2 In-memory transients -- separate types under `Models/<Aggregate>/`

Types that are never persisted (execution context, per-node config
unions, in-flight state machines) live as plain C# classes in
`Models/<Aggregate>/<Type>.cs` under their own namespace and reference
the generated POCO by type:

```csharp
namespace OASIS.WebAPI.Models.Quest;

using OASIS.WebAPI.Generated.SurrealDb;

public sealed record QuestNodeExecutionContext(
    QuestRun Run,
    QuestNode Node,
    IReadOnlyDictionary<Guid, QuestNodeExecution> UpstreamOutputs);
```

These are pure runtime DTOs / value objects; they have no
corresponding `.mermaid` schema.

### 3.3 Request / response DTOs -- separate types under `Models/Requests/`

Caller-facing inputs and outputs (CreateModel, UpdateModel, response
projections, composite views spanning aggregates) live as plain C#
classes in `Models/Requests/<Aggregate>Requests.cs`. They are
intentionally **not** the same type as the storage POCO -- the API
surface should not leak the storage shape's enum nesting or JsonElement
fields:

```csharp
namespace OASIS.WebAPI.Models.Requests;

public class DappSeriesUpdateModel
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string>? SharedConfig { get; set; }
}
```

---

## 4. What this looks like in practice

| Question | Answer |
|---|---|
| "Where do I add a new persisted column?" | `.mermaid` file. Rebuild. The generator emits it on the POCO. |
| "Where do I add `Guid IdGuid { get; set; }`?" | Partial-class extension file in `Generated.SurrealDb` namespace. |
| "Where do I add a `QuestStartedNotification` DTO?" | `Models/Requests/QuestRequests.cs`. References `Generated.SurrealDb.Quest` by type if it needs to. |
| "Where do I write `ComposeAsync(...)` validation logic?" | The manager (`Managers/DappCompositionManager.cs`). Never on the entity. |
| "What about `partial class Quest` for a *computed* `IsActive` from latest QuestRun?" | Manager-level method, not a partial. The entity doesn't get to query a different aggregate. |

---

## 5. Migration phasing

| Cohort | Status | Owner |
|---|---|---|
| New entities (`DappSeries`, `DappSeriesQuest`) | ✅ Source-gen-only from day one (this commit) | this convention |
| 10 currently source-gen-only POCOs (`Holon`, `ApiKey`, `Avatar`, etc.) | ✅ Already conform | -- |
| 4 hand-written legacy (`Wallet`, `BlockchainOperation`, `ConsumedVaaRecord`, `IdempotencyRecord`) | ⏳ Cut over inside `surrealdb-migration` wave-2 | `surrealdb-migration` track |
| 8 hand-written Quest aggregate (`Quest`, `QuestNode`, `QuestEdge`, `QuestDependency`, `QuestRun`, `QuestNodeExecution`, `QuestTemplate`, `QuestNodeTemplate`) | ⏳ Cut over after `surrealdb-migration` wave-2 Surreal stores ship -- partial-class extensions ready, swap is incremental | follow-up after wave-2 |

Partial-class extensions are **additive** -- they do not break code that
still consumes the hand-written types. This is what makes incremental
cutover safe even with multiple PRs in flight against the same
aggregate.

---

## 6. Anti-patterns

1. **A hand-written class with the same name as a generated POCO.**
   Causes ambiguous-reference errors at every consumer; forces `using
   *Def = ...` aliases everywhere; double the maintenance.
2. **A partial extension that calls another aggregate's manager.** The
   entity should not know about service-layer composition. Put that
   logic in the calling manager.
3. **A partial extension that contains business rules.** State-machine
   guards (`Status != Draft` rejection), validation, side effects -- all
   belong in the manager, not the entity.
4. **Generator emission suffixes** (e.g. `QuestEntity`, `QuestRecord`).
   Tempting because it avoids the collision, but rejected: it is a
   breaking change for the 14 already-emitted POCOs and addresses only
   the symptom (collision) rather than the cause (two parallel
   definitions of the same aggregate).

---

## 7. References

- Source generator: [packages/Oasis.SurrealDb.SourceGen](../../packages/Oasis.SurrealDb.SourceGen)
- Schema sources: [Persistence/SurrealDb/Schemas/source/*.mermaid](source)
- Example partial extensions: `Models/DappComposition/DappSeriesExtensions.cs`,
  `Models/DappComposition/DappSeriesQuestExtensions.cs` (this commit).
- Example schema with extensive annotations:
  [Persistence/SurrealDb/Schemas/source/130_quest_template.mermaid](source/130_quest_template.mermaid),
  [210_dapp_series.mermaid](source/210_dapp_series.mermaid).
- Originating discussion: 2026-05-22 / 2026-05-23 dapp-composition slice.
