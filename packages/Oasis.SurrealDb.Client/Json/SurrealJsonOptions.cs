using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oasis.SurrealDb.Client.Json;

/// <summary>
/// Centralised <see cref="JsonSerializerOptions"/> for every SurrealDB
/// payload the client emits or consumes.
/// <para>
/// Closes the code-review C6 design root: <see cref="JsonStringEnumConverter"/>
/// is registered by default so enums round-trip as strings against
/// SCHEMAFULL <c>TYPE string ASSERT $value INSIDE [...]</c>. The previous
/// SDK-mediated default emitted enums as ints, which silently violated
/// every assertion and caused inserts to fail at runtime.
/// </para>
/// <para>
/// Custom converters:
/// <list type="bullet">
///   <item><see cref="RecordIdJsonConverter"/> — <c>table:id</c> string form
///         per <see href="https://surrealdb.com/docs/surrealql/datamodel/ids">SurrealDB IDs</see>.</item>
///   <item><see cref="SurrealDateTimeJsonConverter"/> — ISO-8601 with explicit
///         <c>Z</c>, matching SurrealDB's <c>datetime</c> shape.</item>
///   <item><see cref="SurrealTimeSpanJsonConverter"/> — SurrealDB-style
///         duration literals (<c>1h30m</c>, <c>500ms</c>).</item>
///   <item><see cref="SurrealDecimalJsonConverter"/> — always string on the
///         wire to avoid double-precision drift.</item>
/// </list>
/// </para>
/// </summary>
public static class SurrealJsonOptions
{
    /// <summary>
    /// The pre-configured options the client uses for every request / response.
    /// Safe to share across threads; <see cref="JsonSerializerOptions"/> is
    /// immutable once first used.
    /// </summary>
    public static JsonSerializerOptions Default { get; } = BuildDefault();

    private static JsonSerializerOptions BuildDefault()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy  = JsonNamingPolicy.CamelCase,
            // The HTTP /sql contract uses snake-case property keys on
            // statement slots, but the wave-1 schemas use lowerCamelCase for
            // field names. We accept either on read via
            // PropertyNameCaseInsensitive=true and emit camelCase on write —
            // the camel form matches the wave-1 .mermaid sources.
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented               = false,
        };

        // C6 fix — strings, not ints, for enums.
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        opts.Converters.Add(new RecordIdJsonConverter());
        opts.Converters.Add(new SurrealDateTimeJsonConverter());
        opts.Converters.Add(new SurrealTimeSpanJsonConverter());
        opts.Converters.Add(new SurrealDecimalJsonConverter());

        return opts;
    }
}
