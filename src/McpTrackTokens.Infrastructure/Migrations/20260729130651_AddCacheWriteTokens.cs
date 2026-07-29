using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace McpTrackTokens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCacheWriteTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CacheWriteTokens",
                table: "ExternalUsageRecords",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheWriteTokens",
                table: "ExternalUsageRecords");
        }
    }
}
