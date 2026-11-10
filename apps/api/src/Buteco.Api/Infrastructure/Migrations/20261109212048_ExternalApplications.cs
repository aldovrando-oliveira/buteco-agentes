using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExternalApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    normalized_name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_application_secrets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    suffix = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_application_secrets", x => x.id);
                    table.ForeignKey(
                        name: "FK_external_application_secrets_external_applications_applicat~",
                        column: x => x.application_id,
                        principalTable: "external_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_external_application_secrets_application_id",
                table: "external_application_secrets",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "IX_external_application_secrets_hash",
                table: "external_application_secrets",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_applications_client_id",
                table: "external_applications",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_applications_normalized_name",
                table: "external_applications",
                column: "normalized_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_application_secrets");

            migrationBuilder.DropTable(
                name: "external_applications");
        }
    }
}
