using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Application.Validators;
using McpTrackTokens.Domain.Enums;
using DomainValidationException = McpTrackTokens.Domain.Exceptions.ValidationException;
using NSubstitute;

namespace McpTrackTokens.Application.Tests;

public sealed class ExportServiceTests
{
    private readonly IReportService _reports = Substitute.For<IReportService>();
    private readonly IReportExporter _exporter = Substitute.For<IReportExporter>();
    private readonly string _exportRoot;

    public ExportServiceTests()
    {
        _exportRoot = Path.Combine(Path.GetTempPath(), "mcp-track-tokens-export-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_exportRoot);
        _reports.GetDailyActivityAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(new DailyActivityReport
            {
                FromUtc = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                ToUtc = DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
                Rows = []
            });
        _exporter.Render(Arg.Any<object>(), Arg.Any<ExportFormat>())
            .Returns(Encoding.UTF8.GetBytes("{}"));
    }

    private ExportService CreateSut()
        => new(
            _reports,
            _exporter,
            new ExportRequestDtoValidator(),
            Microsoft.Extensions.Options.Options.Create(new TrackingOptions
            {
                ExportPath = _exportRoot
            }));

    [Fact]
    public async Task BuildFileAsync_returns_in_memory_payload()
    {
        var sut = CreateSut();
        var request = new ExportRequestDto
        {
            ReportType = "daily",
            Format = ExportFormat.Json,
            FromUtc = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            ToUtc = DateTimeOffset.Parse("2026-07-31T00:00:00Z")
        };

        var file = await sut.BuildFileAsync(request);

        file.FileName.Should().EndWith(".json");
        file.ContentType.Should().Contain("application/json");
        file.Content.Should().NotBeEmpty();
        file.ByteCount.Should().Be(file.Content.LongLength);
    }

    [Fact]
    public async Task ExportAsync_rejects_path_traversal_in_output_directory()
    {
        var sut = CreateSut();
        var request = new ExportRequestDto
        {
            ReportType = "daily",
            Format = ExportFormat.Json,
            FromUtc = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            ToUtc = DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
            OutputDirectory = Path.Combine(_exportRoot, "..", "outside")
        };

        var act = async () => await sut.ExportAsync(request);

        await act.Should().ThrowAsync<DomainValidationException>()
            .WithMessage("*Path traversal*");
    }

    [Fact]
    public async Task ExportAsync_rejects_directory_outside_approved_roots()
    {
        var sut = CreateSut();
        var outside = Path.Combine(Path.GetTempPath(), "mcp-track-tokens-not-approved", Guid.NewGuid().ToString("N"));
        var request = new ExportRequestDto
        {
            ReportType = "daily",
            Format = ExportFormat.Json,
            FromUtc = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            ToUtc = DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
            OutputDirectory = outside
        };

        var act = async () => await sut.ExportAsync(request);

        await act.Should().ThrowAsync<DomainValidationException>()
            .WithMessage("*approved export path*");
    }
}
