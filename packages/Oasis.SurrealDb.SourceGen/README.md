# Oasis.SurrealDb.SourceGen

Roslyn `IIncrementalGenerator` that derives strongly-typed C# POCOs from
Mermaid ER schema sources for the `Oasis.SurrealDb.*` package suite.
Eliminates hand-maintained drift between the schema source-of-truth (the
`.mermaid` files under `Persistence/SurrealDb/Schemas/source/`) and the
application's domain layer.

This is the **4th** package in the suite, sitting alongside:

- `Oasis.SurrealDb.Client` — HTTP transport, parameterized query builder, JSON converters, `RecordId<T>` and `SurrealQuery<T>`.
- `Oasis.SurrealDb.Schema` — the Mermaid ER parser the source generator re-uses internally; no duplicate parser implementation.
- `Oasis.SurrealDb.Analyzer` — `SRDB0001` ban on string-interpolated SurrealQL.

## Consumer wiring (MSBuild)

Add a `ProjectReference` with analyzer-asset declarations plus the
`AdditionalFiles` glob pointing at the schema sources:

```xml
<ItemGroup>
  <ProjectReference
    Include="path\to\packages\Oasis.SurrealDb.SourceGen\Oasis.SurrealDb.SourceGen.csproj"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />

  <!-- The Schema package is a compile-time dependency of the generator
       (it owns the Mermaid parser). The Roslyn host must load it
       alongside the generator. ProjectReference + the analyzer Include
       target below pulls in the Schema DLL as an analyzer asset so the
       host can resolve `Oasis.SurrealDb.Schema, Version=0.1.0.0`. -->
  <ProjectReference
    Include="path\to\packages\Oasis.SurrealDb.Schema\Oasis.SurrealDb.Schema.csproj"
    ReferenceOutputAssembly="false"
    PrivateAssets="all" />
</ItemGroup>

<Target Name="AddSurrealDbSchemaAnalyzer"
        BeforeTargets="CoreCompile"
        DependsOnTargets="ResolveProjectReferences">
  <ItemGroup Condition="Exists('path\to\packages\Oasis.SurrealDb.Schema\bin\$(Configuration)\netstandard2.0\Oasis.SurrealDb.Schema.dll')">
    <Analyzer Include="path\to\packages\Oasis.SurrealDb.Schema\bin\$(Configuration)\netstandard2.0\Oasis.SurrealDb.Schema.dll" />
  </ItemGroup>
</Target>

<ItemGroup>
  <AdditionalFiles Include="Persistence\SurrealDb\Schemas\source\*.mermaid" />
</ItemGroup>

<PropertyGroup>
  <!-- Target namespace for generated POCOs.
       Default: <RootNamespace>.Generated.SurrealDb -->
  <OasisSurrealDbModelsNamespace>MyApp.Generated.SurrealDb</OasisSurrealDbModelsNamespace>
</PropertyGroup>
<ItemGroup>
  <CompilerVisibleProperty Include="OasisSurrealDbModelsNamespace" />
</ItemGroup>
```

For pass-through visibility of the generated source files into your
build artefacts (helpful for the pass-off `12-Generated-POCOs-match-schemas`
drift gate):

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)$(Configuration)\$(TargetFramework)\generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

## What you get

For each entity declared in each `.mermaid` source, the generator emits
one `<EntityPascalCase>.g.cs` containing:

- A `partial class <EntityPascalCase> : ISurrealRecord` in the configured
  namespace.
- Strongly-typed properties whose C# types come from the deterministic
  mapping table below.
- `[JsonPropertyName("<snake_case_column>")]` on every property.
- A `public const string SchemaNameConst = "<entity>";` plus the
  `ISurrealRecord.SchemaName` instance property pointing at it.
- Nested enums for `string ASSERT INSIDE [...]` closed-set fields,
  annotated with `JsonStringEnumConverter`.

### Deterministic type mapping

| Mermaid token       | C# type                                              |
|---------------------|------------------------------------------------------|
| `string`            | `string`                                             |
| `int`               | `long` (SurrealDB ints are 64-bit)                   |
| `decimal`           | `decimal`                                            |
| `datetime`          | `global::System.DateTimeOffset`                      |
| `duration`          | `global::System.TimeSpan`                            |
| `bool`              | `bool`                                               |
| `object`            | `global::System.Text.Json.JsonElement`               |
| `record<T>`         | `global::Oasis.SurrealDb.Client.RecordId<T>`         |
| `array<X>`          | `global::System.Collections.Generic.IReadOnlyList<X>`|
| `option<X>`         | `X?` (recursive nullable wrapper)                    |

Unknown tokens raise `NotSupportedException` (and a Roslyn `OSGSG002`
diagnostic) with the offending token in the message and the recognized
set; extend `CSharpTypeMapper.MapInner` to add support.

### Closed-set enums (`ASSERT INSIDE [...]`)

A `string` field with `%% @surreal.assert "$value INSIDE [\"X\", \"Y\"]"`
generates a nested enum named `<PropertyName>Kind` with members `X`, `Y`
and a `JsonStringEnumConverter` attribute on the property. The `Kind`
suffix avoids the C# "type 'X' already contains a definition for 'X'"
diagnostic that fires when the enum and the property share a name.

## Annotation directives

The source generator extends the Mermaid annotation DSL (which lives in
the `Oasis.SurrealDb.Schema` package) with three compound `csharp.*`
sub-directives:

| Directive                                       | Effect                                                          |
|-------------------------------------------------|-----------------------------------------------------------------|
| `%% @surreal.csharp.skip`                       | Generator omits this field from the POCO.                       |
| `%% @surreal.csharp.property name=<PascalName>` | Override the default snake-case to PascalCase translation.      |
| `%% @surreal.csharp.namespace <NS>`             | Per-entity namespace override (preferred over the MSBuild prop).|

Strict-namespacing rules apply: unknown `@surreal.*` directives still
fail the Mermaid parse.

## `SurrealQuery<T>` typed builder (companion in Oasis.SurrealDb.Client)

The source generator unblocks the typed query builder. Once the POCO is
generated, callers can write:

```csharp
using Oasis.SurrealDb.Client.Query;
using MyApp.Generated.SurrealDb;

var query = SurrealQuery<Wallet>.From()
    .Where(w => w.Status == Wallet.StatusKind.Active && w.AvatarId == avatarId)
    .OrderByDescending(w => w.CreatedAt)
    .Limit(20);

// Implicitly widens to the untyped SurrealQuery for the executor:
var rows = await executor.QueryAsync<Wallet>(query);
```

Output is byte-identical to the untyped equivalent:

```csharp
SurrealQuery.Of("SELECT * FROM wallet")
    .Where("status = $status", new Dictionary<string, object?> { { "status", "active" } })
    .Where("avatar_id = $avatar_id", new Dictionary<string, object?> { { "avatar_id", avatarId } })
    .OrderBy("created_at", OrderDirection.Desc)
    .Limit(20);
```

The expression visitor scope is intentionally bounded:

- `MemberExpression` on the lambda parameter -> column name resolved via `[JsonPropertyName]`.
- `BinaryExpression`: `==`, `!=`, `<`, `<=`, `>`, `>=`, `&&`, `||`, `!`.
- `MethodCallExpression`: `string.IsNullOrEmpty(x)` -> `(x = NONE OR x = "")` and `array.Contains(value)` / `value.Contains(member)` -> `value INSIDE array`.
- Closure-captured values resolve via `Expression.Lambda(node).Compile().DynamicInvoke()` at translation time.
- Enum operands are re-boxed via `Enum.ToObject` when the C# compiler folds them to their underlying integral type; `NormaliseValue` then lowercases the enum identifier so the produced SurrealQL matches the `JsonStringEnumConverter` convention used by the generator.

Anything outside this surface throws `NotSupportedException` with the
fallback recipe in the message: drop to the untyped `SurrealQuery.Of(...)`
escape hatch for that one query.

## `RecordId<T>` typed companion (Oasis.SurrealDb.Client)

```csharp
using Oasis.SurrealDb.Client;
using MyApp.Generated.SurrealDb;

// Construct from raw id; the schema name is taken from new T().SchemaName.
RecordId<Wallet> typedId = new RecordId<Wallet>("abc123");
Console.WriteLine(typedId);  // wallet:abc123

// Implicit widening for the executor (always safe):
RecordId untyped = typedId;

// Explicit narrowing fails at runtime if the table doesn't match:
var foreignId = new RecordId("idempotency_key_store", "k1");
try {
    var bad = (RecordId<Wallet>)foreignId;
} catch (InvalidCastException ex) {
    // Message includes both schema names.
}
```

## Diagnostics

| ID       | Severity | Meaning                                                                 |
|----------|----------|-------------------------------------------------------------------------|
| OSGSG001 | Error    | Mermaid schema parse error in an AdditionalText file (file:line:col).   |
| OSGSG002 | Error    | Unsupported SurrealDB type in a generated entity (offending token in message). |

Both are surfaced as compiler errors so misshapen schemas / unknown
types break the build cleanly rather than producing wrong code.

## Internal-only

Per the suite-wide publish-deferral decision locked in
`surrealdb-client-package` sub-wave 1.5a, the source-gen package ships
internal-feed only until at least 90 days after sub-wave 1.5b sign-off.
