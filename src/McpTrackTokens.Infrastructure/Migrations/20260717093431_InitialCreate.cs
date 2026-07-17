using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace McpTrackTokens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CostAllocationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostAllocationRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    FileHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReceivedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ErrorSummary = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ClientName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    BillingCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    PrimaryRepositoryPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    PrimaryRemoteUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false, defaultValue: new byte[0]),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackingApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LastUsedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowedEditors = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    AllowedMachineNames = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackingApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalUsageRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExternalRecordId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PeriodStartUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PeriodEndUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UserIdentifier = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    InputTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    OutputTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    CachedInputTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    ReasoningTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    ReportedCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    RequestCount = table.Column<int>(type: "INTEGER", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalUsageRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalUsageRecords_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EditorSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Editor = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EditorVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MachineName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    WorkspacePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    RepositoryPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    RemoteUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ExternalSessionId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastActivityAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false, defaultValue: new byte[0]),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditorSessions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProjectAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    NormalizedAlias = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    AliasType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectAliases_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectRepositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    NormalizedPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    RemoteUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    NormalizedRemoteUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    DefaultBranch = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectRepositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectRepositories_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActivityWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EditorSessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DurationSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    InactivityThresholdMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CalculationVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityWindows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityWindows_EditorSessions_EditorSessionId",
                        column: x => x.EditorSessionId,
                        principalTable: "EditorSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ActivityWindows_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PromptActivityEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EditorSessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Editor = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExternalEventId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ExternalConversationId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ExternalRequestId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    WorkspacePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    RepositoryPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    RemoteUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PromptLength = table.Column<int>(type: "INTEGER", nullable: true),
                    PromptHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PromptContentStored = table.Column<bool>(type: "INTEGER", nullable: false),
                    PromptContentEncrypted = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseCompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AttributionMethod = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AttributionConfidence = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptActivityEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptActivityEvents_EditorSessions_EditorSessionId",
                        column: x => x.EditorSessionId,
                        principalTable: "EditorSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PromptActivityEvents_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UsageAttributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalUsageRecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EditorSessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActivityEventId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AllocatedCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AllocatedInputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    AllocatedOutputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    AllocatedTotalTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    AllocationPercentage = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AttributionMethod = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Confidence = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageAttributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsageAttributions_EditorSessions_EditorSessionId",
                        column: x => x.EditorSessionId,
                        principalTable: "EditorSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UsageAttributions_ExternalUsageRecords_ExternalUsageRecordId",
                        column: x => x.ExternalUsageRecordId,
                        principalTable: "ExternalUsageRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UsageAttributions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UsageAttributions_PromptActivityEvents_ActivityEventId",
                        column: x => x.ActivityEventId,
                        principalTable: "PromptActivityEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityWindows_CreatedAtUtc",
                table: "ActivityWindows",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityWindows_EditorSessionId",
                table: "ActivityWindows",
                column: "EditorSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityWindows_EndedAtUtc",
                table: "ActivityWindows",
                column: "EndedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityWindows_ProjectId",
                table: "ActivityWindows",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityWindows_ProjectId_StartedAtUtc_EndedAtUtc",
                table: "ActivityWindows",
                columns: new[] { "ProjectId", "StartedAtUtc", "EndedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityWindows_StartedAtUtc",
                table: "ActivityWindows",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CostAllocationRules_CreatedAtUtc",
                table: "CostAllocationRules",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CostAllocationRules_IsEnabled",
                table: "CostAllocationRules",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_CostAllocationRules_Priority",
                table: "CostAllocationRules",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_CostAllocationRules_UpdatedAtUtc",
                table: "CostAllocationRules",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_Editor_ExternalSessionId",
                table: "EditorSessions",
                columns: new[] { "Editor", "ExternalSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_EndedAtUtc",
                table: "EditorSessions",
                column: "EndedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_ExternalSessionId",
                table: "EditorSessions",
                column: "ExternalSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_LastActivityAtUtc",
                table: "EditorSessions",
                column: "LastActivityAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_ProjectId",
                table: "EditorSessions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_RepositoryPath",
                table: "EditorSessions",
                column: "RepositoryPath");

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_StartedAtUtc",
                table: "EditorSessions",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EditorSessions_Status",
                table: "EditorSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_CreatedAtUtc",
                table: "ExternalUsageRecords",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_ExternalRecordId",
                table: "ExternalUsageRecords",
                column: "ExternalRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_ImportBatchId",
                table: "ExternalUsageRecords",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_ImportedAtUtc",
                table: "ExternalUsageRecords",
                column: "ImportedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_Source",
                table: "ExternalUsageRecords",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_Source_ExternalRecordId",
                table: "ExternalUsageRecords",
                columns: new[] { "Source", "ExternalRecordId" },
                unique: true,
                filter: "\"ExternalRecordId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_TimestampUtc",
                table: "ExternalUsageRecords",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_CompletedAtUtc",
                table: "ImportBatches",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_CreatedAtUtc",
                table: "ImportBatches",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_FileHash",
                table: "ImportBatches",
                column: "FileHash",
                unique: true,
                filter: "\"FileHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_Source",
                table: "ImportBatches",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_StartedAtUtc",
                table: "ImportBatches",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_Status",
                table: "ImportBatches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAliases_CreatedAtUtc",
                table: "ProjectAliases",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAliases_NormalizedAlias",
                table: "ProjectAliases",
                column: "NormalizedAlias");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAliases_NormalizedAlias_AliasType",
                table: "ProjectAliases",
                columns: new[] { "NormalizedAlias", "AliasType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAliases_ProjectId",
                table: "ProjectAliases",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRepositories_CreatedAtUtc",
                table: "ProjectRepositories",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRepositories_NormalizedPath",
                table: "ProjectRepositories",
                column: "NormalizedPath");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRepositories_NormalizedRemoteUrl",
                table: "ProjectRepositories",
                column: "NormalizedRemoteUrl");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRepositories_ProjectId",
                table: "ProjectRepositories",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRepositories_ProjectId_NormalizedPath",
                table: "ProjectRepositories",
                columns: new[] { "ProjectId", "NormalizedPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ClientName",
                table: "Projects",
                column: "ClientName");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CreatedAtUtc",
                table: "Projects",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_IsActive",
                table: "Projects",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_PrimaryRemoteUrl",
                table: "Projects",
                column: "PrimaryRemoteUrl");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_PrimaryRepositoryPath",
                table: "Projects",
                column: "PrimaryRepositoryPath");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Slug",
                table: "Projects",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_UpdatedAtUtc",
                table: "Projects",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_CreatedAtUtc",
                table: "PromptActivityEvents",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_Editor_ExternalEventId",
                table: "PromptActivityEvents",
                columns: new[] { "Editor", "ExternalEventId" },
                unique: true,
                filter: "\"ExternalEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_EditorSessionId",
                table: "PromptActivityEvents",
                column: "EditorSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_ExternalConversationId",
                table: "PromptActivityEvents",
                column: "ExternalConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_ExternalEventId",
                table: "PromptActivityEvents",
                column: "ExternalEventId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_ExternalRequestId",
                table: "PromptActivityEvents",
                column: "ExternalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_ProjectId",
                table: "PromptActivityEvents",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_RepositoryPath",
                table: "PromptActivityEvents",
                column: "RepositoryPath");

            migrationBuilder.CreateIndex(
                name: "IX_PromptActivityEvents_TimestampUtc",
                table: "PromptActivityEvents",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingApiKeys_CreatedAtUtc",
                table: "TrackingApiKeys",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingApiKeys_ExpiresAtUtc",
                table: "TrackingApiKeys",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingApiKeys_IsActive",
                table: "TrackingApiKeys",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingApiKeys_KeyHash",
                table: "TrackingApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackingApiKeys_LastUsedAtUtc",
                table: "TrackingApiKeys",
                column: "LastUsedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UsageAttributions_ActivityEventId",
                table: "UsageAttributions",
                column: "ActivityEventId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageAttributions_CreatedAtUtc",
                table: "UsageAttributions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UsageAttributions_EditorSessionId",
                table: "UsageAttributions",
                column: "EditorSessionId");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityWindows");

            migrationBuilder.DropTable(
                name: "CostAllocationRules");

            migrationBuilder.DropTable(
                name: "ProjectAliases");

            migrationBuilder.DropTable(
                name: "ProjectRepositories");

            migrationBuilder.DropTable(
                name: "TrackingApiKeys");

            migrationBuilder.DropTable(
                name: "UsageAttributions");

            migrationBuilder.DropTable(
                name: "ExternalUsageRecords");

            migrationBuilder.DropTable(
                name: "PromptActivityEvents");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "EditorSessions");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
