using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskRejections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_rejections",
                columns: table => new
                {
                    TaskId = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    RejectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_rejections", x => x.TaskId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_rejections_AgentId",
                table: "task_rejections",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_task_rejections_RejectedAt",
                table: "task_rejections",
                column: "RejectedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_rejections");
        }
    }
}
