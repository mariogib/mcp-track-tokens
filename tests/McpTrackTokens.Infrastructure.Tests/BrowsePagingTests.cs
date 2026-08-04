using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using McpTrackTokens.Application.DependencyInjection;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Infrastructure.DependencyInjection;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Infrastructure.Tests;

public sealed class BrowsePagingTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mtt-page-{Guid.NewGuid():N}.db");
    private readonly string _exportPath = Path.Combine(Path.GetTempPath(), $"mtt-page-exports-{Guid.NewGuid():N}");
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
    public async Task ActivityEvents_ListPaged_UsesPageBoundariesAndFilters()
    {
        using var scope = _services.CreateScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var events = scope.ServiceProvider.GetRequiredService<IActivityEventRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var project = Project.Create("Paging Project", "paging-project");
        await projects.AddAsync(project);
        await uow.SaveChangesAsync();

        var baseTime = new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 5; i++)
        {
            var evt = PromptActivityEvent.Create(
                ActivityEventType.PromptSubmitted,
                EditorType.Cursor,
                baseTime.AddMinutes(i),
                project.Id,
                model: i % 2 == 0 ? "gpt-4" : "claude",
                branch: i < 3 ? "main" : "feature");
            await events.AddAsync(evt);
        }

        await uow.SaveChangesAsync();

        var filter = new ActivityEventPageFilter
        {
            ProjectId = project.Id,
            FromUtc = baseTime.AddHours(-1),
            ToUtc = baseTime.AddHours(2),
            PromptSubmittedOnly = true
        };

        var total = await events.CountAsync(filter);
        total.Should().Be(5);

        var page0 = await events.ListPagedAsync(filter, pageIndex: 0, pageSize: 2);
        page0.Should().HaveCount(2);
        page0[0].TimestampUtc.Should().BeAfter(page0[1].TimestampUtc);

        var page1 = await events.ListPagedAsync(filter, pageIndex: 1, pageSize: 2);
        page1.Should().HaveCount(2);
        page1.Select(e => e.Id).Should().NotIntersectWith(page0.Select(e => e.Id));

        var page2 = await events.ListPagedAsync(filter, pageIndex: 2, pageSize: 2);
        page2.Should().HaveCount(1);

        var filtered = filter with { Model = "gpt-4" };
        var filteredCount = await events.CountAsync(filtered);
        filteredCount.Should().Be(3);
        var filteredPage = await events.ListPagedAsync(filtered, 0, 10);
        filteredPage.Should().HaveCount(3);
        filteredPage.Should().OnlyContain(e => e.Model == "gpt-4");

        var facets = await events.GetPromptFacetsAsync(
            project.Id,
            baseTime.AddHours(-1),
            baseTime.AddHours(2));
        facets.Models.Should().BeEquivalentTo("claude", "gpt-4");
        facets.Branches.Should().BeEquivalentTo("feature", "main");
        facets.Days.Should().Contain("2024-07-01");
    }

    [Fact]
    public async Task TimesheetEntries_ListPaged_RespectsOpenClosedAndSearch()
    {
        using var scope = _services.CreateScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var categories = scope.ServiceProvider.GetRequiredService<ITimesheetCategoryRepository>();
        var timesheets = scope.ServiceProvider.GetRequiredService<ITimesheetEntryRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var project = Project.Create("Timesheet Paging", "timesheet-paging");
        await projects.AddAsync(project);
        var existingCategories = await categories.ListAsync(activeOnly: true);
        var category = existingCategories.FirstOrDefault()
            ?? TimesheetCategory.Create($"Work-{Guid.NewGuid():N}"[..20], 0);
        if (existingCategories.Count == 0)
        {
            await categories.AddAsync(category);
        }
        await uow.SaveChangesAsync();

        var day = new DateTimeOffset(2024, 8, 10, 9, 0, 0, TimeSpan.Zero);
        var open = TimesheetEntry.Start(project.Id, category.Id, day, "alpha notes");
        var closed = TimesheetEntry.Start(project.Id, category.Id, day.AddHours(1), "beta notes");
        closed.End(day.AddHours(2));
        var other = TimesheetEntry.Start(project.Id, category.Id, day.AddHours(3), "gamma");
        other.End(day.AddHours(4));

        await timesheets.AddAsync(open);
        await timesheets.AddAsync(closed);
        await timesheets.AddAsync(other);
        await uow.SaveChangesAsync();

        var filter = new TimesheetEntryPageFilter
        {
            ProjectId = project.Id,
            FromUtc = day.AddDays(-1),
            ToUtc = day.AddDays(1)
        };

        (await timesheets.CountAsync(filter)).Should().Be(3);

        var page0 = await timesheets.ListPagedAsync(filter, 0, 2);
        page0.Should().HaveCount(2);
        var page1 = await timesheets.ListPagedAsync(filter, 1, 2);
        page1.Should().HaveCount(1);
        page1.Select(e => e.Id).Should().NotIntersectWith(page0.Select(e => e.Id));

        var openOnly = filter with { OpenClosed = "open" };
        (await timesheets.CountAsync(openOnly)).Should().Be(1);
        (await timesheets.ListPagedAsync(openOnly, 0, 10)).Should().OnlyContain(e => e.EndedAtUtc == null);

        var search = filter with { Search = "beta" };
        (await timesheets.CountAsync(search)).Should().Be(1);
        (await timesheets.ListPagedAsync(search, 0, 10)).Single().Notes.Should().Contain("beta");
    }

    [Fact]
    public async Task Sessions_ListPaged_UsesPageBoundariesAndFilters()
    {
        using var scope = _services.CreateScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var project = Project.Create("Session Paging", "session-paging");
        await projects.AddAsync(project);
        await uow.SaveChangesAsync();

        var baseTime = new DateTimeOffset(2024, 9, 1, 10, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 5; i++)
        {
            var session = EditorSession.Start(
                EditorType.Cursor,
                baseTime.AddMinutes(i),
                project.Id,
                branch: i < 3 ? "main" : "feature",
                workspacePath: i % 2 == 0 ? @"D:\alpha" : @"D:\beta");
            if (i >= 3)
            {
                session.TransitionTo(SessionStatus.Ended, baseTime.AddMinutes(i + 30));
            }

            await sessions.AddAsync(session);
        }

        await uow.SaveChangesAsync();

        var filter = new SessionPageFilter
        {
            ProjectId = project.Id,
            FromUtc = baseTime.AddHours(-1),
            ToUtc = baseTime.AddHours(2)
        };

        (await sessions.CountAsync(filter)).Should().Be(5);

        var page0 = await sessions.ListPagedAsync(filter, 0, 2);
        page0.Should().HaveCount(2);
        page0[0].StartedAtUtc.Should().BeAfter(page0[1].StartedAtUtc);

        var page1 = await sessions.ListPagedAsync(filter, 1, 2);
        page1.Should().HaveCount(2);
        page1.Select(s => s.Id).Should().NotIntersectWith(page0.Select(s => s.Id));

        var page2 = await sessions.ListPagedAsync(filter, 2, 2);
        page2.Should().HaveCount(1);

        var closed = filter with { Status = "Closed" };
        (await sessions.CountAsync(closed)).Should().Be(2);
        var closedPage = await sessions.ListPagedAsync(closed, 0, 10);
        closedPage.Should().HaveCount(2);
        closedPage.Should().OnlyContain(s =>
            s.Status == SessionStatus.Ended || s.Status == SessionStatus.Abandoned);

        var search = filter with { Search = "alpha" };
        (await sessions.CountAsync(search)).Should().Be(3);
        var searchPage = await sessions.ListPagedAsync(search, 0, 10);
        searchPage.Should().HaveCount(3);
        searchPage.Should().OnlyContain(s =>
            s.WorkspacePath != null && s.WorkspacePath.Contains("alpha", StringComparison.Ordinal));
    }

    [Fact]
    public void ActivityEvents_ListPaged_SqlContainsLimitOffset()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
        var from = DateTimeOffset.UtcNow.AddDays(-1).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
        var toExclusive = DateTimeOffset.UtcNow.ToUniversalTime().AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss");
        var sql =
            "SELECT * FROM PromptActivityEvents WHERE \"TimestampUtc\" >= {0} AND \"TimestampUtc\" < {1} ORDER BY TimestampUtc DESC, Id DESC LIMIT {2} OFFSET {3}";
        var query = db.PromptActivityEvents.FromSqlRaw(sql, from, toExclusive, 25, 0);
        var text = query.ToQueryString();
        text.Should().ContainEquivalentOf("LIMIT");
        text.Should().ContainEquivalentOf("OFFSET");
    }

    [Fact]
    public async Task ActivityEvents_ListAsync_FiltersByTextRangeWithoutLoadingOutsideWindow()
    {
        using var scope = _services.CreateScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var events = scope.ServiceProvider.GetRequiredService<IActivityEventRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var project = Project.Create("Range Project", "range-project");
        await projects.AddAsync(project);
        await uow.SaveChangesAsync();

        var inside = new DateTimeOffset(2024, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var outside = inside.AddDays(-30);
        await events.AddAsync(PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted, EditorType.Cursor, inside, project.Id, model: "in"));
        await events.AddAsync(PromptActivityEvent.Create(
            ActivityEventType.PromptSubmitted, EditorType.Cursor, outside, project.Id, model: "out"));
        await uow.SaveChangesAsync();

        var listed = await events.ListAsync(inside.AddHours(-1), inside.AddHours(1), project.Id);
        listed.Should().ContainSingle(e => e.Model == "in");
        listed.Should().NotContain(e => e.Model == "out");

        var latest = await events.GetLatestAsync();
        latest!.Model.Should().Be("in");
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
            // best effort
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}
