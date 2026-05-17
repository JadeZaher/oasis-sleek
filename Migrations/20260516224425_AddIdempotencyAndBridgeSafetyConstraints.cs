using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OASIS.WebAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyAndBridgeSafetyConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BridgeTransactions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AvatarId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceChain = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetChain = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceTokenId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TargetTokenId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SourceAddress = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TargetAddress = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    LockTxHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MintTxHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProofData = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WormholeEmitterChainId = table.Column<int>(type: "integer", nullable: true),
                    WormholeEmitterAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    WormholeSequence = table.Column<long>(type: "bigint", nullable: true),
                    VaaBytes = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    VaaSignatureCount = table.Column<int>(type: "integer", nullable: true),
                    RedemptionTxHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BridgeTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsumedVaas",
                columns: table => new
                {
                    Digest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EmitterChainId = table.Column<int>(type: "integer", nullable: false),
                    EmitterAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    BridgeTransactionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumedVaas", x => x.Digest);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OperationType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ResultPayload = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BridgeTransactions_AvatarId",
                table: "BridgeTransactions",
                column: "AvatarId");

            migrationBuilder.CreateIndex(
                name: "IX_BridgeTransactions_IdempotencyKey",
                table: "BridgeTransactions",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_BridgeTransactions_LockTxHash",
                table: "BridgeTransactions",
                column: "LockTxHash",
                unique: true,
                filter: "\"LockTxHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BridgeTransactions_SourceChain_TargetChain",
                table: "BridgeTransactions",
                columns: new[] { "SourceChain", "TargetChain" });

            migrationBuilder.CreateIndex(
                name: "IX_BridgeTransactions_Status",
                table: "BridgeTransactions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BridgeTransactions_WormholeEmitterChainId_WormholeEmitterAd~",
                table: "BridgeTransactions",
                columns: new[] { "WormholeEmitterChainId", "WormholeEmitterAddress", "WormholeSequence" },
                unique: true,
                filter: "\"WormholeEmitterChainId\" IS NOT NULL AND \"WormholeEmitterAddress\" IS NOT NULL AND \"WormholeSequence\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConsumedVaas_Digest",
                table: "ConsumedVaas",
                column: "Digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsumedVaas_EmitterChainId_EmitterAddress_Sequence",
                table: "ConsumedVaas",
                columns: new[] { "EmitterChainId", "EmitterAddress", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_Key",
                table: "IdempotencyRecords",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_OperationType",
                table: "IdempotencyRecords",
                column: "OperationType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BridgeTransactions");

            migrationBuilder.DropTable(
                name: "ConsumedVaas");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");
        }
    }
}
