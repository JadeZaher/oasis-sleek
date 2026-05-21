using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oasis.SurrealDb.Client.Json;

/// <summary>
/// Serialises <see cref="RecordId"/> as the SurrealDB <c>table:id</c> string
/// form, the wire shape produced by SurrealDB itself for record-id columns.
/// Reads tolerate both the string form and the object form
/// <c>{ "tb": "wallet", "id": "abc" }</c> that the CBOR-over-WS protocol
/// sometimes emits when the response is parsed back into JSON.
/// </summary>
public sealed class RecordIdJsonConverter : JsonConverter<RecordId>
{
    public override RecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (s is null)
            {
                throw new JsonException("RecordId cannot be deserialized from null.");
            }
            return RecordId.Parse(s);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            string? tb = null;
            string? id = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                var prop = reader.GetString();
                reader.Read();
                switch (prop)
                {
                    case "tb": case "table": tb = reader.GetString(); break;
                    case "id":               id = reader.GetString(); break;
                    default: reader.Skip(); break;
                }
            }
            if (string.IsNullOrEmpty(tb) || string.IsNullOrEmpty(id))
            {
                throw new JsonException("RecordId object form requires both 'tb' and 'id'.");
            }
            return new RecordId(tb!, id!);
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for RecordId.");
    }

    public override void Write(Utf8JsonWriter writer, RecordId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
