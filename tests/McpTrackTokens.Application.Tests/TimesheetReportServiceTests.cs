using FluentAssertions;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Domain.Entities;
using NSubstitute;

namespace McpTrackTokens.Application.Tests;

public sealed class TimesheetReportServiceTests
{
    private readonly IProjectRepository _projects = Substitute.For<IProjectRepository>();
    private readonly ITimesheetEntryRepository _timesheets = Substitute.For<ITimesheetEntryRepository>();
    private readonly ITimesheetCategoryRepository _categories = Substitute.For<ITimesheetCategoryRepository>();

    private TimesheetReportService CreateSut()
        => new(_projects, _timesheets, _categories);

    [Fact]
    public async Task Overall_includes_client_projects_with_zero_timesheet_time()
    {
        var withTime = Project.Create("MCP Track Tokens", "mcp-track-tokens", clientName: "LunarQ");
        var withoutTime = Project.Create("lunarq-WhatsApp-Editor", "lunarq-whatsapp-editor", clientName: "LunarQ");
        var work = TimesheetCategory.Create("Work", sortOrder: 0, id: TimesheetCategory.WorkId);
        var from = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-07-31T23:59:59Z");
        var entry = TimesheetEntry.Start(
            withTime.Id,
            work.Id,
            DateTimeOffset.Parse("2026-07-10T10:00:00Z"));
        entry.End(DateTimeOffset.Parse("2026-07-10T12:00:00Z"));

        _projects.ListAsync(false, Arg.Any<CancellationToken>()).Returns([withTime, withoutTime]);
        _timesheets.ListAsync(null, from, to, Arg.Any<CancellationToken>()).Returns([entry]);
        _categories.ListAsync(false, Arg.Any<CancellationToken>()).Returns([work]);

        var report = await CreateSut().GetOverallReportAsync(from, to);

        report.ByProject.Should().HaveCount(2);
        report.ByProject.Select(r => r.ProjectName).Should().Contain(["MCP Track Tokens", "lunarq-WhatsApp-Editor"]);
        report.ByProject.Single(r => r.ProjectId == withoutTime.Id).DurationSeconds.Should().Be(0);
        report.ByClient.Should().ContainSingle(r => r.ClientName == "LunarQ");
        report.ByClient[0].ProjectCount.Should().Be(2);
    }
}
