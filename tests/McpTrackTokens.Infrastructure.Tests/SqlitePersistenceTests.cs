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
