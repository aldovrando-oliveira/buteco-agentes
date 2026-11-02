using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "two_factor_enabled_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "two_factor_last_step",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "two_factor_pending_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "two_factor_pending_secret",
                table: "users",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "two_factor_secret",
                table: "users",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_recovery_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_recovery_codes", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_recovery_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_recovery_codes_user_id_code_hash",
                table: "user_recovery_codes",
                columns: new[] { "user_id", "code_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_recovery_codes");

            migrationBuilder.DropColumn(
                name: "two_factor_enabled_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_last_step",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_pending_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_pending_secret",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_secret",
                table: "users");
        }
    }
}
