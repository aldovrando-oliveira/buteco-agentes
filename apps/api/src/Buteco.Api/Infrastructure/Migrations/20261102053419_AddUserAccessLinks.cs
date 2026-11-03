using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccessLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "access_link_expires_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "access_link_hash",
                table: "users",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "access_link_purpose",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "invited_by",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_access_link_hash",
                table: "users",
                column: "access_link_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_invited_by",
                table: "users",
                column: "invited_by");

            migrationBuilder.AddForeignKey(
                name: "FK_users_users_invited_by",
                table: "users",
                column: "invited_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_users_invited_by",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_access_link_hash",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_invited_by",
                table: "users");

            migrationBuilder.DropColumn(
                name: "access_link_expires_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "access_link_hash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "access_link_purpose",
                table: "users");

            migrationBuilder.DropColumn(
                name: "invited_by",
                table: "users");
        }
    }
}
