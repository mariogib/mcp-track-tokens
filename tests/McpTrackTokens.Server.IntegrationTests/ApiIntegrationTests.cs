using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;

namespace McpTrackTokens.Server.IntegrationTests;

public sealed class ApiIntegrationTests : IClassFixture<TrackingWebApplicationFactory>
{
    private readonly TrackingWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ApiIntegrationTests(TrackingWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.ApiKey);
    }

    [Fact]
    public async Task Health_IsPublic()
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_IsPublic()
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync("/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WriteEndpoints_RequireApiKey()
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/v1/events", new IngestEventDto
        {
            EventType = "PromptSubmitted",
        Editor = "Cursor",
        TimestampUtc = DateTimeOffset.UtcNow
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReadEndpoints_RequireApiKey()
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync("/api/v1/projects");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidEvent_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/events", new IngestEventDto
        {
            EventType = "",
            Editor = "",
            TimestampUtc = default
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BatchIngestion_AcceptsEvents()
    {
        var batch = new BatchIngestRequestDto
        {
            Events =
            [
                CreateEvent("batch-1"),
                CreateEvent("batch-2")
            ]
        };

        var response = await _client.PostAsJsonAsync("/api/v1/events/batch", batch);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BatchIngestResultDto>(JsonOptions);
        result.Should().NotBeNull();
        result!.Accepted.Should().Be(2);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public async Task Idempotency_DeduplicatesExternalEventId()
    {
        var evt = CreateEvent("idempotent-1");
        var first = await _client.PostAsJsonAsync("/api/v1/events", evt);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var firstResult = await first.Content.ReadFromJsonAsync<IngestEventResultDto>(JsonOptions);

        var second = await _client.PostAsJsonAsync("/api/v1/events", evt);
        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var secondResult = await second.Content.ReadFromJsonAsync<IngestEventResultDto>(JsonOptions);

        secondResult.Should().NotBeNull();
        secondResult!.WasDuplicate.Should().BeTrue();
        secondResult.EventId.Should().Be(firstResult!.EventId);
    }

    [Fact]
    public async Task ProjectReports_ReturnData()
    {
        var project = await RegisterProjectAsync("Report Project");

        await _client.PostAsJsonAsync("/api/v1/events", CreateEvent($"report-evt-{Guid.NewGuid():N}", project.Id));

        var activity = await _client.GetAsync($"/api/v1/projects/{project.Id}/activity");
        activity.StatusCode.Should().Be(HttpStatusCode.OK);

        var cost = await _client.GetAsync($"/api/v1/projects/{project.Id}/cost");
        cost.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await _client.GetAsync("/api/v1/reports/summary");
        summary.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UsageImport_AcceptsSampleCsv()
    {
        var samplePath = FindSample("cursor-usage-sample.csv");
        File.Exists(samplePath).Should().BeTrue();

        var response = await _client.PostAsJsonAsync("/api/v1/imports/cursor", new ImportCursorUsageRequestDto
        {
            FilePath = samplePath,
            DryRun = false,
            Force = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("importedCount");
    }

    [Fact]
    public async Task Reconciliation_RunsSuccessfully()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/reconciliation/run", new ReconciliationRequestDto
        {
            FromUtc = DateTimeOffset.UtcNow.AddDays(-7),
            ToUtc = DateTimeOffset.UtcNow,
            DryRun = true
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sessions_StartAndListActive()
    {
        var start = await _client.PostAsJsonAsync("/api/v1/sessions/start", new SessionStartDto
        {
            Editor = "Cursor",
            WorkspacePath = Directory.GetCurrentDirectory(),
            ExternalSessionId = $"ext-{Guid.NewGuid():N}"
        });
        start.StatusCode.Should().Be(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());

        var active = await _client.GetAsync("/api/v1/sessions/active");
        active.StatusCode.Should().Be(HttpStatusCode.OK, await active.Content.ReadAsStringAsync());
        var body = await active.Content.ReadAsStringAsync();
        body.Should().Contain("Cursor");
    }

    [Fact]
    public async Task Projects_UpdateAndDelete_ViaHttp()
    {
        var created = await RegisterProjectAsync($"Edit Me {Guid.NewGuid():N}");

        var update = await _client.PutAsJsonAsync($"/api/v1/projects/{created.Id}", new UpdateProjectRequest
        {
            Name = "Edited Name",
            Slug = $"edited-{Guid.NewGuid():N}"[..20].TrimEnd('-'),
            ClientName = "Edited Client",
            BillingCode = "EDIT-1",
            Currency = "USD",
            IsActive = true
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var updated = await update.Content.ReadFromJsonAsync<ProjectDetailDto>(JsonOptions);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Edited Name");
        updated.ClientName.Should().Be("Edited Client");

        var delete = await _client.DeleteAsync($"/api/v1/projects/{created.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await _client.GetFromJsonAsync<List<ProjectDto>>("/api/v1/projects", JsonOptions);
        list.Should().NotBeNull();
        list!.Any(p => p.Id == created.Id).Should().BeFalse();
    }

    private async Task<ProjectDetailDto> RegisterProjectAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var detection = scope.ServiceProvider.GetRequiredService<IProjectDetectionService>();
        return await detection.RegisterAsync(new CreateProjectRequest { Name = name });
    }

    private static IngestEventDto CreateEvent(string externalId, Guid? projectId = null) => new()
    {
        SchemaVersion = "1.0",
        ExternalEventId = externalId,
        EventType = "PromptSubmitted",
        Editor = "Cursor",
        TimestampUtc = DateTimeOffset.UtcNow,
        WorkspacePath = Directory.GetCurrentDirectory(),
        ProjectId = projectId,
        PromptLength = 12
    };

    private static string FindSample(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "samples", "imports", fileName),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "imports", fileName))
        };
        return candidates.First(File.Exists);
    }
}
