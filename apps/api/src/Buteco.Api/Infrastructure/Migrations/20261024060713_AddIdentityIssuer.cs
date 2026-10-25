using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityIssuer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_keys",
                columns: table => new
                {
                    kid = table.Column<string>(type: "text", nullable: false),
                    usage = table.Column<string>(type: "text", nullable: false),
                    algorithm = table.Column<string>(type: "text", nullable: false),
                    protected_material = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    signs_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    signs_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    validates_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_keys", x => x.kid);
                });

            migrationBuilder.CreateTable(
                name: "openiddict_applications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClientSecret = table.Column<string>(type: "text", nullable: true),
                    ClientType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConsentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    JsonWebKeySet = table.Column<string>(type: "text", nullable: true),
                    Permissions = table.Column<string>(type: "text", nullable: true),
                    PostLogoutRedirectUris = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedirectUris = table.Column<string>(type: "text", nullable: true),
                    Requirements = table.Column<string>(type: "text", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_openiddict_applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "openiddict_scopes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Descriptions = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_openiddict_scopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "openiddict_authorizations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Scopes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_openiddict_authorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_openiddict_authorizations_openiddict_applications_Applicati~",
                        column: x => x.ApplicationId,
                        principalTable: "openiddict_applications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "openiddict_tokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    AuthorizationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedemptionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_openiddict_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_openiddict_tokens_openiddict_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "openiddict_applications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_openiddict_tokens_openiddict_authorizations_AuthorizationId",
                        column: x => x.AuthorizationId,
                        principalTable: "openiddict_authorizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_identity_keys_usage_validates_until",
                table: "identity_keys",
                columns: new[] { "usage", "validates_until" });

            migrationBuilder.CreateIndex(
                name: "IX_openiddict_applications_ClientId",
                table: "openiddict_applications",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_openiddict_authorizations_ApplicationId_Status_Subject_Type",
                table: "openiddict_authorizations",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_openiddict_scopes_Name",
                table: "openiddict_scopes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_openiddict_tokens_ApplicationId_Status_Subject_Type",
                table: "openiddict_tokens",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_openiddict_tokens_AuthorizationId",
                table: "openiddict_tokens",
                column: "AuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_openiddict_tokens_ReferenceId",
                table: "openiddict_tokens",
                column: "ReferenceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_keys");

            migrationBuilder.DropTable(
                name: "openiddict_scopes");

            migrationBuilder.DropTable(
                name: "openiddict_tokens");

            migrationBuilder.DropTable(
                name: "openiddict_authorizations");

            migrationBuilder.DropTable(
                name: "openiddict_applications");
        }
    }
}
