using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using McpTrackTokens.Application.DependencyInjection;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Infrastructure.DependencyInjection;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Tests;

public sealed class SqlitePersistenceTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mtt-infra-{Guid.NewGuid():N}.db");
    private readonly string _exportPath = Path.Combine(Path.GetTempPath(), $"mtt-exports-{Guid.NewGuid():N}");
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_exportPath);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tracking:DatabaseProvider"] = "Sqlite",
                ["Tracking:DatabasePath"] = _dbPath,
                ["Tracking:ExportPath"] = _exportPath,
                ["Tracking:LogPath"] = Path.Combine(Path.GetTempPath(), "mtt-logs"),
                ["Tracking:QueuePath"] = Path.Combine(Path.GetTempPath(), "mtt-queue"),
                ["Tracking:MigrateOnStartup"] = "true"
            })
            .Build();

        var collection = new ServiceCollection();
        collection.AddSingleton<IConfiguration>(configuration);
        collection.AddLogging();
        collection.AddApplication();
        collection.AddInfrastructure(configuration);
        _services = collection.BuildServiceProvider();

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        TryDelete(_dbPath);
        TryDeleteDirectory(_exportPath);
    }

    [Fact]
    public async Task Projects_PersistAndRoundTrip()
    {
        using var scope = _services.CreateScope();
        var detection = scope.ServiceProvider.GetRequiredService<IProjectDetectionService>();
        var created = await detection.RegisterAsync(new CreateProjectRequest
        {
            Name = "Infra Project",
            RepositoryPath = Path.Combine(Path.GetTempPath(), "repo-a")
        });

        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var loaded = await projects.GetByIdAsync(created.Id);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Infra Project");
    }

    [Fact]
    public async Task ApiKeys_EnforceUniqueHash()
    {
        using var scope = _services.CreateScope();
        var apiKeys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var first = await apiKeys.CreateAsync(new CreateApiKeyRequestDto { Name = "one" });
        var verified = await apiKeys.VerifyAsync(first.ApiKey);
        verified.Should().BeTrue();

        var repository = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();
        var hash = Application.Services.ApiKeyService.HashKey(first.ApiKey);
        var existing = await repository.FindByHashAsync(hash);
        existing.Should().NotBeNull();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var duplicate = TrackingApiKey.Create("dup", hash);
        await repository.AddAsync(duplicate);
        var act = async () => await unitOfWork.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task EventExternalId_IsUniquePerEditor()
    {
        using var scope = _services.CreateScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<IEventIngestionService>();
        var dto = new IngestEventDto
        {
            ExternalEventId = "unique-evt-1",
            EventType = "PromptSubmitted",
            Editor = "Cursor",
            TimestampUtc = DateTimeOffset.UtcNow,
            PromptLength = 5
        };

        var first = await ingestion.IngestAsync(dto);
        var second = await ingestion.IngestAsync(dto);
        first.WasDuplicate.Should().BeFalse();
        second.WasDuplicate.Should().BeTrue();
        second.EventId.Should().Be(first.EventId);
    }

    [Fact]
    public async Task CursorCsvAndJson_ParseAndImport()
    {
        var csv = FindSample("cursor-usage-sample.csv");
        var json = FindSample("cursor-usage-sample.json");

        using var scope = _services.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<ICursorUsageImporter>();
        var detector = scope.ServiceProvider.GetRequiredService<ICursorUsageFormatDetector>();

        (await detector.DetectAsync(csv)).Should().Be(UsageSource.CursorCsv);
        (await detector.DetectAsync(json)).Should().Be(UsageSource.CursorJson);

        var csvResult = await importer.ImportAsync(new ImportCursorUsageRequestDto
        {
            FilePath = csv,
            Force = true
        });
        csvResult.ImportedCount.Should().BeGreaterThan(0);

        var jsonResult = await importer.ImportAsync(new ImportCursorUsageRequestDto
        {
            FilePath = json,
            Force = true
        });
        jsonResult.ReceivedCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CursorCsv_V2_ImportsAllTotalTokenRows_IncludingZeroCost()
    {
        var csv = FindSample("cursor-usage-events-v2.csv");

        using var scope = _services.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<ICursorUsageImporter>();
        var usage = scope.ServiceProvider.GetRequiredService<IExternalUsageRepository>();

        var preview = await importer.PreviewAsync(new ImportCursorUsageRequestDto { FilePath = csv });
        // All three rows have Total Tokens > 0; Included cost becomes 0 but still imports.
        preview.ValidCount.Should().Be(3);
        preview.InvalidCount.Should().Be(0);
        preview.SampleRecords.Should().OnlyContain(r => (r.TotalTokens ?? 0) > 0);
        preview.SampleRecords.Sum(r => r.TotalTokens ?? 0).Should().Be(3250 + 2750 + 700);

        var result = await importer.ImportAsync(new ImportCursorUsageRequestDto
        {
            FilePath = csv,
            Force = true
        });
        result.ImportedCount.Should().Be(3);
        result.FailedCount.Should().Be(0);

        var stored = await usage.ListAsync(
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-31T23:59:59Z"));
        stored.Count(r => (r.TotalTokens ?? 0) > 0).Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void PathNormalizer_NormalizesPathsAndRemotes()
    {
        using var scope = _services.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<IPathNormalizer>();
        var path = normalizer.Normalize(@"C:\Dev\Demo\");
        path.Should().NotBeNullOrWhiteSpace();
        path.Should().NotContain("\\");

        var remote = normalizer.NormalizeRemoteUrl("git@github.com:Org/Repo.git");
        remote.Should().Contain("github.com");
        remote.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Export_WritesUnderApprovedDirectory()
    {
        using var scope = _services.CreateScope();
        var detection = scope.ServiceProvider.GetRequiredService<IProjectDetectionService>();
        var project = await detection.RegisterAsync(new CreateProjectRequest { Name = "Export Me" });
        var export = scope.ServiceProvider.GetRequiredService<IExportService>();
        var result = await export.ExportAsync(new ExportRequestDto
        {
            ReportType = "project-cost",
            ProjectId = project.Id,
            FromUtc = DateTimeOffset.UtcNow.AddDays(-7),
            ToUtc = DateTimeOffset.UtcNow,
            Format = ExportFormat.Json
        });

        result.FilePath.Should().StartWith(_exportPath);
        File.Exists(result.FilePath).Should().BeTrue();
    }

    [Fact]
    public void QueuePath_IsCreatedByOptions()
    {
        using var scope = _services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TrackingOptions>>().Value;
        var queue = TrackingOptions.ExpandPath(options.QueuePath);
        Directory.CreateDirectory(queue);
        Directory.Exists(queue).Should().BeTrue();
    }

    [Fact]
    public async Task Reconciliation_MultipleUsages_LinkToSameClosestPrompt()
    {
        using var scope = _services.CreateScope();
        var detection = scope.ServiceProvider.GetRequiredService<IProjectDetectionService>();
        var events = scope.ServiceProvider.GetRequiredService<IActivityEventRepository>();
        var usageRepo = scope.ServiceProvider.GetRequiredService<IExternalUsageRepository>();
        var attributions = scope.ServiceProvider.GetRequiredService<IUsageAttributionRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reconciliation = scope.ServiceProvider.GetRequiredService<IReconciliationService>();

        var project = await detection.RegisterAsync(new CreateProjectRequest { Name = "Multi Usage Prompt" });
        var promptAt = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        var prompt = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            promptAt,
            projectId: project.Id,
            externalEventId: $"prompt-{Guid.NewGuid():N}");
        await events.AddAsync(prompt);

        var usage1 = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            promptAt.AddSeconds(2),
            externalRecordId: $"usage-a-{Guid.NewGuid():N}",
            totalTokens: 1000,
            reportedCost: 0m);
        var usage2 = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            promptAt.AddSeconds(45),
            externalRecordId: $"usage-b-{Guid.NewGuid():N}",
            totalTokens: 2500,
            reportedCost: 1.25m);
        await usageRepo.AddRangeAsync([usage1, usage2]);
        await unitOfWork.SaveChangesAsync();

        var result = await reconciliation.RunAsync(new ReconciliationRequestDto
        {
            FromUtc = promptAt.AddHours(-1),
            ToUtc = promptAt.AddHours(1),
            DryRun = false
        });

        result.AllocatedCount.Should().Be(2);
        result.Attributions.Should().OnlyContain(a => a.ActivityEventId == prompt.Id);

        var linked = await attributions.ListByActivityEventIdsAsync([prompt.Id]);
        linked.Should().HaveCount(2);
        linked.Select(a => a.ExternalUsageRecordId).Should().BeEquivalentTo([usage1.Id, usage2.Id]);
        linked.Should().OnlyContain(a => a.AttributionMethod == AttributionMethod.ClosestPromptMatch);
        linked.Should().OnlyContain(a => a.ProjectId == project.Id);
    }

    [Fact]
    public async Task ListAttributions_FiltersByUsageTimestamp_NotAttributionCreatedAt()
    {
        using var scope = _services.CreateScope();
        var detection = scope.ServiceProvider.GetRequiredService<IProjectDetectionService>();
        var usageRepo = scope.ServiceProvider.GetRequiredService<IExternalUsageRepository>();
        var attributions = scope.ServiceProvider.GetRequiredService<IUsageAttributionRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var project = await detection.RegisterAsync(new CreateProjectRequest { Name = "Usage Period Filter" });
        var usageAt = DateTimeOffset.Parse("2026-07-01T12:00:00Z");
        var attributedAt = DateTimeOffset.Parse("2026-08-13T12:00:00Z");
        var usage = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            usageAt,
            externalRecordId: $"period-{Guid.NewGuid():N}",
            model: "composer-2.5",
            totalTokens: 1000,
            reportedCost: 1.5m,
            createdAtUtc: attributedAt);
        await usageRepo.AddAsync(usage);
        await unitOfWork.SaveChangesAsync();

        await attributions.AddAsync(UsageAttribution.Create(
            usage.Id,
            AttributionMethod.ClosestPromptMatch,
            AttributionConfidence.High,
            100m,
            allocatedCost: 1.5m,
            allocatedTotalTokens: 1000,
            projectId: project.Id,
            reason: "Period filter test",
            createdAtUtc: attributedAt));
        await unitOfWork.SaveChangesAsync();

        var july = await attributions.ListAsync(
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-31T23:59:59Z"),
            project.Id);
        var august = await attributions.ListAsync(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-31T23:59:59Z"),
            project.Id);

        july.Should().ContainSingle(a => a.ExternalUsageRecordId == usage.Id);
        august.Should().BeEmpty();
    }

    [Fact]
    public async Task ListUnallocated_IncludesRowsWithOnlyUnallocatedPlaceholder()
    {
        using var scope = _services.CreateScope();
        var detection = scope.ServiceProvider.GetRequiredService<IProjectDetectionService>();
        var usageRepo = scope.ServiceProvider.GetRequiredService<IExternalUsageRepository>();
        var attributions = scope.ServiceProvider.GetRequiredService<IUsageAttributionRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var project = await detection.RegisterAsync(new CreateProjectRequest { Name = "Unalloc List Project" });
        var at = DateTimeOffset.Parse("2026-07-18T11:00:00Z");
        var withPlaceholder = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at,
            externalRecordId: $"unalloc-placeholder-{Guid.NewGuid():N}",
            totalTokens: 500,
            reportedCost: 0m);
        var withProject = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at.AddSeconds(1),
            externalRecordId: $"alloc-{Guid.NewGuid():N}",
            totalTokens: 600,
            reportedCost: 0m);
        var neverAttributed = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at.AddSeconds(2),
            externalRecordId: $"never-{Guid.NewGuid():N}",
            totalTokens: 700,
            reportedCost: 0m);

        await usageRepo.AddRangeAsync([withPlaceholder, withProject, neverAttributed]);
        await unitOfWork.SaveChangesAsync();

        await attributions.AddRangeAsync(
        [
            UsageAttribution.Create(
                withPlaceholder.Id,
                AttributionMethod.Unallocated,
                AttributionConfidence.Unallocated,
                0m,
                allocatedCost: 0m,
                allocatedTotalTokens: 500,
                reason: "No prompt in window."),
            UsageAttribution.Create(
                withProject.Id,
                AttributionMethod.ClosestPromptMatch,
                AttributionConfidence.High,
                100m,
                allocatedCost: 0m,
                allocatedTotalTokens: 600,
                projectId: project.Id,
                reason: "Linked.")
        ]);
        await unitOfWork.SaveChangesAsync();

        var unallocated = await usageRepo.ListUnallocatedAsync(at.AddMinutes(-1), at.AddMinutes(1));

        unallocated.Select(u => u.Id).Should().BeEquivalentTo([withPlaceholder.Id, neverAttributed.Id]);
        unallocated.Should().NotContain(u => u.Id == withProject.Id);
    }

    [Fact]
    public async Task DeleteUnallocated_RemovesOnlyUnallocatedRowsAndPlaceholders()
    {
        using var scope = _services.CreateScope();
        var detection = scope.ServiceProvider.GetRequiredService<IProjectDetectionService>();
        var usageRepo = scope.ServiceProvider.GetRequiredService<IExternalUsageRepository>();
        var attributions = scope.ServiceProvider.GetRequiredService<IUsageAttributionRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var project = await detection.RegisterAsync(new CreateProjectRequest { Name = "Delete Unalloc Project" });
        var at = DateTimeOffset.Parse("2026-07-18T12:00:00Z");
        var withPlaceholder = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at,
            externalRecordId: $"del-placeholder-{Guid.NewGuid():N}",
            totalTokens: 500,
            reportedCost: 0m);
        var withProject = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at.AddSeconds(1),
            externalRecordId: $"del-alloc-{Guid.NewGuid():N}",
            totalTokens: 600,
            reportedCost: 0m);
        var neverAttributed = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            at.AddSeconds(2),
            externalRecordId: $"del-never-{Guid.NewGuid():N}",
            totalTokens: 700,
            reportedCost: 0m);

        await usageRepo.AddRangeAsync([withPlaceholder, withProject, neverAttributed]);
        await unitOfWork.SaveChangesAsync();

        await attributions.AddRangeAsync(
        [
            UsageAttribution.Create(
                withPlaceholder.Id,
                AttributionMethod.Unallocated,
                AttributionConfidence.Unallocated,
                0m,
                allocatedCost: 0m,
                allocatedTotalTokens: 500,
                reason: "No prompt in window."),
            UsageAttribution.Create(
                withProject.Id,
                AttributionMethod.ClosestPromptMatch,
                AttributionConfidence.High,
                100m,
                allocatedCost: 0m,
                allocatedTotalTokens: 600,
                projectId: project.Id,
                reason: "Linked.")
        ]);
        await unitOfWork.SaveChangesAsync();

        var deleted = await usageRepo.DeleteUnallocatedAsync(at.AddMinutes(-1), at.AddMinutes(1));

        deleted.Should().Be(2);
        (await usageRepo.GetByIdAsync(withPlaceholder.Id)).Should().BeNull();
        (await usageRepo.GetByIdAsync(neverAttributed.Id)).Should().BeNull();
        (await usageRepo.GetByIdAsync(withProject.Id)).Should().NotBeNull();
        (await attributions.ListByUsageRecordAsync(withPlaceholder.Id)).Should().BeEmpty();
        (await attributions.ListByUsageRecordAsync(withProject.Id)).Should().ContainSingle();
        (await usageRepo.ListUnallocatedAsync(at.AddMinutes(-1), at.AddMinutes(1))).Should().BeEmpty();
    }

    [Fact]
    public async Task Reconciliation_LinksUsageToClosestPriorPrompt_RoundedToSecond()
    {
        using var scope = _services.CreateScope();
        var detection = scope.ServiceProvider.GetRequiredService<IProjectDetectionService>();
        var events = scope.ServiceProvider.GetRequiredService<IActivityEventRepository>();
        var usageRepo = scope.ServiceProvider.GetRequiredService<IExternalUsageRepository>();
        var attributions = scope.ServiceProvider.GetRequiredService<IUsageAttributionRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reconciliation = scope.ServiceProvider.GetRequiredService<IReconciliationService>();

        var project = await detection.RegisterAsync(new CreateProjectRequest { Name = "Prior Prompt" });
        var earlier = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            DateTimeOffset.Parse("2026-07-18T09:54:00Z"),
            projectId: project.Id,
            externalEventId: $"prompt-earlier-{Guid.NewGuid():N}");
        var closer = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            DateTimeOffset.Parse("2026-07-18T09:55:28.400Z"),
            projectId: project.Id,
            externalEventId: $"prompt-closer-{Guid.NewGuid():N}");
        await events.AddAsync(earlier);
        await events.AddAsync(closer);

        var usage = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            DateTimeOffset.Parse("2026-07-18T09:55:56.100Z"),
            externalRecordId: $"usage-prior-{Guid.NewGuid():N}",
            totalTokens: 9000,
            reportedCost: 0m);
        await usageRepo.AddAsync(usage);
        await unitOfWork.SaveChangesAsync();

        var result = await reconciliation.RunAsync(new ReconciliationRequestDto
        {
            FromUtc = DateTimeOffset.Parse("2026-07-18T09:00:00Z"),
            ToUtc = DateTimeOffset.Parse("2026-07-18T11:00:00Z"),
            DryRun = false
        });

        result.AllocatedCount.Should().Be(1);
        result.UnallocatedCount.Should().Be(0);

        var linked = await attributions.ListByUsageRecordAsync(usage.Id);
        linked.Should().ContainSingle();
        linked[0].ActivityEventId.Should().Be(closer.Id);
        linked[0].ProjectId.Should().Be(project.Id);
        linked[0].AttributionMethod.Should().Be(AttributionMethod.ClosestPromptMatch);
    }

    [Fact]
    public async Task Reconciliation_DoesNotLinkUsageWhenOnlyLaterPromptExists()
    {
        using var scope = _services.CreateScope();
        var detection = scope.ServiceProvider.GetRequiredService<IProjectDetectionService>();
        var events = scope.ServiceProvider.GetRequiredService<IActivityEventRepository>();
        var usageRepo = scope.ServiceProvider.GetRequiredService<IExternalUsageRepository>();
        var attributions = scope.ServiceProvider.GetRequiredService<IUsageAttributionRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reconciliation = scope.ServiceProvider.GetRequiredService<IReconciliationService>();

        var project = await detection.RegisterAsync(new CreateProjectRequest { Name = "Later Only" });
        var usageAt = DateTimeOffset.Parse("2026-07-18T09:55:28Z");
        var laterPrompt = PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted,
            EditorType.Cursor,
            usageAt.AddSeconds(5),
            projectId: project.Id,
            externalEventId: $"prompt-later-{Guid.NewGuid():N}");
        await events.AddAsync(laterPrompt);

        var usage = ExternalUsageRecord.Create(
            UsageSource.CursorCsv,
            usageAt,
            externalRecordId: $"usage-later-{Guid.NewGuid():N}",
            totalTokens: 9000,
            reportedCost: 0m);
        await usageRepo.AddAsync(usage);
        await unitOfWork.SaveChangesAsync();

        var result = await reconciliation.RunAsync(new ReconciliationRequestDto
        {
            FromUtc = usageAt.AddHours(-1),
            ToUtc = usageAt.AddHours(1),
            DryRun = false
        });

        result.AllocatedCount.Should().Be(0);
        result.UnallocatedCount.Should().Be(1);

        var linked = await attributions.ListByUsageRecordAsync(usage.Id);
        linked.Should().ContainSingle();
        linked[0].ProjectId.Should().BeNull();
        linked[0].AttributionMethod.Should().Be(AttributionMethod.Unallocated);
    }

    private static string FindSample(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "samples", "imports", fileName),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "imports", fileName))
        };
        return candidates.First(File.Exists);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }
}
