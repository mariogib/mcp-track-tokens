using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace McpTrackTokens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleProjectRepository : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep the oldest mapping per project before enforcing a unique ProjectId.
            migrationBuilder.Sql(
                """
                DELETE FROM "ProjectRepositories"
                WHERE "Id" NOT IN (
                    SELECT "Id" FROM (
                        SELECT "Id",
                               ROW_NUMBER() OVER (
                                   PARTITION BY "ProjectId"
                                   ORDER BY "CreatedAtUtc", "Id"
                               ) AS rn
                        FROM "ProjectRepositories"
                    )
                    WHERE rn = 1
                );
                """);

            migrationBuilder.DropIndex(
                name: "IX_ProjectRepositories_ProjectId",
                table: "ProjectRepositories");

            migrationBuilder.DropIndex(
                name: "IX_ProjectRepositories_ProjectId_NormalizedPath",
                table: "ProjectRepositories");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRepositories_ProjectId",
                table: "ProjectRepositories",
                column: "ProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectRepositories_ProjectId",
                table: "ProjectRepositories");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRepositories_ProjectId",
                table: "ProjectRepositories",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRepositories_ProjectId_NormalizedPath",
                table: "ProjectRepositories",
                columns: new[] { "ProjectId", "NormalizedPath" },
                unique: true);
        }
    }
}
