using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OASIS.WebAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddSagaOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SagaSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SagaName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StepName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StepIdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsCompensation = table.Column<bool>(type: "boolean", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Output = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    DeadLettered = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SagaSteps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SagaSteps_CorrelationKey",
                table: "SagaSteps",
                column: "CorrelationKey");

            migrationBuilder.CreateIndex(
                name: "IX_SagaSteps_DeadLettered",
                table: "SagaSteps",
                column: "DeadLettered");

            migrationBuilder.CreateIndex(
                name: "IX_SagaSteps_SagaName",
                table: "SagaSteps",
                column: "SagaName");

            migrationBuilder.CreateIndex(
                name: "IX_SagaSteps_Status_NextRunAt",
                table: "SagaSteps",
                columns: new[] { "Status", "NextRunAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SagaSteps");
        }
    }
}
