// SPDX-License-Identifier: UNLICENSED
// Hand-authored SurrealDB POCO -- C#-first authoring surface.
//
// Replaces the previous Mermaid-sourced generator output for the `wallet`
// table. Carries both the .surql-emit metadata ([SurrealTable], [Column],
// [Assert], [Index], etc.) AND the runtime JSON shape consumers depend
// on ([JsonPropertyName], ISurrealRecord.SchemaName). Lives in
// OASIS.WebAPI.Generated.SurrealDb to preserve source-compat with the 30+
// files that import that namespace.

#nullable enable

using System;
using System.Text.Json.Serialization;
using Oasis.SurrealDb.Client;
using Oasis.SurrealDb.Client.Schema;

namespace OASIS.WebAPI.Persistence.SurrealDb.Models
{
    [SurrealTable("wallet",
        Aggregate = "Wallet (Models/Wallet.cs)",
        Guardrail = "G6 SCHEMAFULL")]
    [SurrealNote("No balance field -- chain is source of truth (data-engine-decision).")]
    [SurrealNote("B3 review: wallet_avatar_chain_address UNIQUE is on (avatar_id, chain_type, address) -- all three NOT NULL; SurrealDB NULL-collision concern does not apply.")]
    [Slice("wallet_nft")]
    [Index("wallet_avatar_chain_address", Fields = new[] { "avatar_id", "chain_type", "address" }, Unique = true)]
    [Index("wallet_avatar_chain", Fields = new[] { "avatar_id", "chain_type" })]
    public partial class Wallet : ISurrealRecord
    {
        public const string SchemaNameConst = "wallet";
        public string SchemaName => SchemaNameConst;

        public enum WalletTypeKind
        {
            External,
            Platform,
        }

        [Id, Column(Order = 1, Type = "string")]
        public string Id { get; set; } = string.Empty;

        [Column(Order = 2)]
        [References(typeof(Avatar))]
        public string AvatarId { get; set; } = string.Empty;

        [Column(Order = 3, Type = "string")]
        [Required(NotEmpty = true)]
        public string ChainType { get; set; } = string.Empty;

        [Column(Order = 4, Type = "string")]
        [Required(NotEmpty = true)]
        public string Address { get; set; } = string.Empty;

        [Column(Order = 5, Type = "option<string>")]
        public string? PublicKey { get; set; }

        [Column(Order = 6, Type = "option<string>")]
        public string? Label { get; set; }

        [Column(Order = 7, Type = "bool")]
        [Default("false")]
        public bool IsDefault { get; set; }

        [Column(Order = 8, Type = "string")]
        [Inside("External", "Platform")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public WalletTypeKind WalletType { get; set; }

        [Column(Order = 9, Type = "option<string>")]
        public string? EncryptedPrivateKey { get; set; }

        [Column(Order = 10, Type = "option<string>")]
        public string? EncryptedSeedPhrase { get; set; }

        [Column(Order = 11, Type = "datetime")]
        [ReadOnly]
        public DateTimeOffset CreatedDate { get; set; }
    }
}
