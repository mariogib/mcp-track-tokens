using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace McpTrackTokens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTimesheetCategories : Migration
    {
        private static readonly Guid WorkId = new("a1b2c3d4-e5f6-4789-a012-111111111101");
        private static readonly Guid MeetingsId = new("a1b2c3d4-e5f6-4789-a012-111111111102");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimesheetCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimesheetCategories", x => x.Id);
                });

            var seedAt = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);
            migrationBuilder.InsertData(
                table: "TimesheetCategories",
                columns: new[] { "Id", "Name", "SortOrder", "IsActive", "UpdatedAtUtc", "CreatedAtUtc" },
                values: new object[,]
                {
                    { WorkId, "Work", 0, true, seedAt, seedAt },
                    { MeetingsId, "Meetings", 1, true, seedAt, seedAt }
                });

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "TimesheetEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: WorkId);

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetEntries_CategoryId",
                table: "TimesheetEntries",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetCategories_IsActive",
                table: "TimesheetCategories",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetCategories_Name",
                table: "TimesheetCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetCategories_SortOrder",
                table: "TimesheetCategories",
                column: "SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_TimesheetEntries_TimesheetCategories_CategoryId",
                table: "TimesheetEntries",
                column: "CategoryId",
                principalTable: "TimesheetCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimesheetEntries_TimesheetCategories_CategoryId",
                table: "TimesheetEntries");

            migrationBuilder.DropTable(
                name: "TimesheetCategories");

            migrationBuilder.DropIndex(
                name: "IX_TimesheetEntries_CategoryId",
                table: "TimesheetEntries");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "TimesheetEntries");
        }
    }
}
