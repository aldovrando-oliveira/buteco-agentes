using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Inbox.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "contacts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: true),
                    DeliveryStatus = table.Column<string>(type: "text", nullable: true),
                    DeliveryFailureReason = table.Column<string>(type: "text", nullable: true),
                    PendingDispatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchStatus = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_messages_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_messages_SessionId_ExternalId",
                table: "messages",
                columns: new[] { "SessionId", "ExternalId" },
                unique: true,
                filter: "\"Direction\" = 'Inbound' AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_messages_SessionId_OccurredAt",
                table: "messages",
                columns: new[] { "SessionId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "contacts");
        }
    }
}
