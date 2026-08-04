using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace McpTrackTokens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeHighVolumeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UsageAttributions_CreatedAtUtc",
                table: "UsageAttributions");

            migrationBuilder.DropIndex(
                name: "IX_UsageAttributions_ExternalUsageRecordId",
                table: "UsageAttributions");

            migrationBuilder.DropIndex(
                name: "IX_UsageAttributions_ProjectId",
                table: "UsageAttributions");

            migrationBuilder.DropIndex(
                name: "IX_UsageAttributions_ReviewedAtUtc",
                table: "UsageAttributions");

            migrationBuilder.DropIndex(
                name: "IX_TrackingApiKeys_CreatedAtUtc",
                table: "TrackingApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_TrackingApiKeys_ExpiresAtUtc",
                table: "TrackingApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_TrackingApiKeys_LastUsedAtUtc",
                table: "TrackingApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_TimesheetEntries_ProjectId",
                table: "TimesheetEntries");

            migrationBuilder.DropIndex(
                name: "IX_PromptActivityEvents_CreatedAtUtc",
                table: "PromptActivityEvents");

            migrationBuilder.DropIndex(
                name: "IX_PromptActivityEvents_EditorSessionId",
                table: "PromptActivityEvents");

            migrationBuilder.DropIndex(
                name: "IX_PromptActivityEvents_ExternalEventId",
                table: "PromptActivityEvents");

            migrationBuilder.DropIndex(
                name: "IX_PromptActivityEvents_ProjectId",
                table: "PromptActivityEvents");

            migrationBuilder.DropIndex(
                name: "IX_PromptActivityEvents_RepositoryPath",
                table: "PromptActivityEvents");

            migrationBuilder.DropIndex(
                name: "IX_ExternalUsageRecords_CreatedAtUtc",
                table: "ExternalUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_ExternalUsageRecords_ExternalRecordId",
                table: "ExternalUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_ExternalUsageRecords_ImportedAtUtc",
                table: "ExternalUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_ExternalUsageRecords_Source",
                table: "ExternalUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_EditorSessions_ExternalSessionId",
                table: "EditorSessions");

            migrationBuilder.DropIndex(
                name: "IX_EditorSessions_ProjectId",
                table: "EditorSessions");

            migrationBuilder.DropIndex(
                name: "IX_EditorSessions_RepositoryPath",
                table: "EditorSessions");

            migrationBuilder.DropIndex(
                name: "IX_EditorSessions_Status",
                table: "EditorSessions");

            migrationBuilder.DropIndex(
                name: "IX_ActivityWindows_CreatedAtUtc",
                table: "ActivityWindows");

            migrationBuilder.DropIndex(
                name: "IX_ActivityWindows_ProjectId",
                table: "ActivityWindows");

            migrationBuilder.CreateIndex(
                name: "IX_UsageAttributions_ExternalUsageRecordId_ProjectId",
                table: "UsageAttributions",
                columns: new[] { "ExternalUsageRecordId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageAttributions_ProjectId_CreatedAtUtc",
                table: "UsageAttributions",
                columns: new[] { "ProjectId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetEntries_ProjectId_StartedAtUtc",
                table: "TimesheetEntries",
                columns: new[] { "ProjectId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_EditorSessionId_EventType_TimestampUtc",
                table: "PromptActivityEvents",
                columns: new[] { "EditorSessionId", "EventType", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_ProjectId_EventType_TimestampUtc",
                table: "PromptActivityEvents",
                columns: new[] { "ProjectId", "EventType", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_ProjectId_TimestampUtc",
                table: "PromptActivityEvents",
                columns: new[] { "ProjectId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_Source_TimestampUtc",
                table: "ExternalUsageRecords",
                columns: new[] { "Source", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_ProjectId_StartedAtUtc",
                table: "EditorSessions",
                columns: new[] { "ProjectId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_Status_LastActivityAtUtc",
                table: "EditorSessions",
                columns: new[] { "Status", "LastActivityAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UsageAttributions_ExternalUsageRecordId_ProjectId",
                table: "UsageAttributions");

            migrationBuilder.DropIndex(
                name: "IX_UsageAttributions_ProjectId_CreatedAtUtc",
                table: "UsageAttributions");

            migrationBuilder.DropIndex(
                name: "IX_TimesheetEntries_ProjectId_StartedAtUtc",
                table: "TimesheetEntries");

            migrationBuilder.DropIndex(
                name: "IX_PromptActivityEvents_EditorSessionId_EventType_TimestampUtc",
                table: "PromptActivityEvents");

            migrationBuilder.DropIndex(
                name: "IX_PromptActivityEvents_ProjectId_EventType_TimestampUtc",
                table: "PromptActivityEvents");

            migrationBuilder.DropIndex(
                name: "IX_PromptActivityEvents_ProjectId_TimestampUtc",
                table: "PromptActivityEvents");

            migrationBuilder.DropIndex(
                name: "IX_ExternalUsageRecords_Source_TimestampUtc",
                table: "ExternalUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_EditorSessions_ProjectId_StartedAtUtc",
                table: "EditorSessions");

            migrationBuilder.DropIndex(
                name: "IX_EditorSessions_Status_LastActivityAtUtc",
                table: "EditorSessions");

            migrationBuilder.CreateIndex(
                name: "IX_UsageAttributions_CreatedAtUtc",
                table: "UsageAttributions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UsageAttributions_ExternalUsageRecordId",
                table: "UsageAttributions",
                column: "ExternalUsageRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageAttributions_ProjectId",
                table: "UsageAttributions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageAttributions_ReviewedAtUtc",
                table: "UsageAttributions",
                column: "ReviewedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingApiKeys_CreatedAtUtc",
                table: "TrackingApiKeys",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingApiKeys_ExpiresAtUtc",
                table: "TrackingApiKeys",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingApiKeys_LastUsedAtUtc",
                table: "TrackingApiKeys",
                column: "LastUsedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetEntries_ProjectId",
                table: "TimesheetEntries",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_CreatedAtUtc",
                table: "PromptActivityEvents",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_EditorSessionId",
                table: "PromptActivityEvents",
                column: "EditorSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_ExternalEventId",
                table: "PromptActivityEvents",
                column: "ExternalEventId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_ProjectId",
                table: "PromptActivityEvents",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_RepositoryPath",
                table: "PromptActivityEvents",
                column: "RepositoryPath");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_CreatedAtUtc",
                table: "ExternalUsageRecords",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_ExternalRecordId",
                table: "ExternalUsageRecords",
                column: "ExternalRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_ImportedAtUtc",
                table: "ExternalUsageRecords",
                column: "ImportedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_Source",
                table: "ExternalUsageRecords",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_ExternalSessionId",
                table: "EditorSessions",
                column: "ExternalSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_ProjectId",
                table: "EditorSessions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_RepositoryPath",
                table: "EditorSessions",
                column: "RepositoryPath");

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_Status",
                table: "EditorSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityWindows_CreatedAtUtc",
                table: "ActivityWindows",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityWindows_ProjectId",
                table: "ActivityWindows",
                column: "ProjectId");
        }
    }
}
