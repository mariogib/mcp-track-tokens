using FluentAssertions;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Application.Validators;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.ValueObjects;
using NSubstitute;

namespace McpTrackTokens.Application.Tests;

public sealed class ProjectDetectionServiceTests
{
    private readonly IProjectRepository _projects = Substitute.For<IProjectRepository>();
    private readonly IGitRepositoryResolver _git = Substitute.For<IGitRepositoryResolver>();
    private readonly IPathNormalizer _paths = Substitute.For<IPathNormalizer>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private ProjectDetectionService CreateSut(bool autoCreate = false)
    {
        _paths.Normalize(Arg.Any<string?>()).Returns(ci => NormalizedPath.Normalize(ci.Arg<string?>()));
        _paths.NormalizeRemoteUrl(Arg.Any<string?>()).Returns(ci => NormalizedRemoteUrl.Normalize(ci.Arg<string?>()));

        return new ProjectDetectionService(
            _projects,
            _git,
            _paths,
            _unitOfWork,
            new CreateProjectRequestValidator(),
            new UpdateProjectRequestValidator(),
            Microsoft.Extensions.Options.Options.Create(new TrackingOptions
            {
                AutoCreateProjects = autoCreate,
                DefaultCurrency = "USD"
            }));
    }

    [Fact]
    public async Task DetectAsync_finds_project_by_normalized_path()
    {
        var project = Project.Create("Demo", "demo");
        _git.ResolveAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GitRepositoryInfo(null, null, null, null, null, false));
        _projects.FindByNormalizedPathAsync("C:/work/demo", Arg.Any<CancellationToken>())
            .Returns(project);

        var sut = CreateSut();
        var result = await sut.DetectAsync(@"C:\work\demo", @"C:\work\demo", null);

        result.Should().BeSameAs(project);
    }

    [Fact]
    public async Task DetectAsync_finds_project_by_remote_url()
    {
        var project = Project.Create("Remote", "remote");
        const string remote = "git@github.com:owner/repo.git";
        var normalizedRemote = NormalizedRemoteUrl.Normalize(remote);

        _git.ResolveAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GitRepositoryInfo(null, null, null, null, null, false));
        _projects.FindByNormalizedPathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Project?)null);
        _projects.FindByAliasAsync(Arg.Any<string>(), Arg.Any<AliasType?>(), Arg.Any<CancellationToken>())
            .Returns((Project?)null);
        _projects.FindByNormalizedRemoteUrlAsync(normalizedRemote, Arg.Any<CancellationToken>())
            .Returns(project);

        var sut = CreateSut();
        var result = await sut.DetectAsync(workspacePath: null, repositoryPath: null, remoteUrl: remote);

        result.Should().BeSameAs(project);
    }

    [Fact]
    public async Task DetectAsync_returns_null_when_missing_and_auto_create_disabled()
    {
        _git.ResolveAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GitRepositoryInfo(null, null, null, null, null, false));
        _projects.FindByNormalizedPathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Project?)null);
        _projects.FindByAliasAsync(Arg.Any<string>(), Arg.Any<AliasType?>(), Arg.Any<CancellationToken>())
            .Returns((Project?)null);
        _projects.FindByNormalizedRemoteUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Project?)null);

        var sut = CreateSut(autoCreate: false);
        var result = await sut.DetectAsync(@"C:\unknown", @"C:\unknown", null);

        result.Should().BeNull();
        await _projects.DidNotReceive().AddAsync(Arg.Any<Project>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_creates_project_with_slug()
    {
        _projects.SlugExistsAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _projects.GetRepositoriesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _projects.GetAliasesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var sut = CreateSut();
        var detail = await sut.RegisterAsync(new CreateProjectRequest
        {
            Name = "New Project",
            RepositoryPath = @"D:\code\new-project"
        });

        detail.Name.Should().Be("New Project");
        detail.Slug.Should().Be("new-project");
        detail.Currency.Should().Be("USD");
        await _projects.Received(1).AddAsync(Arg.Any<Project>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_updates_project_fields()
    {
        var existing = Project.Create("Old Name", "old-name", "USD", "Old Client");
        _projects.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);
        _projects.SlugExistsAsync("renamed", existing.Id, Arg.Any<CancellationToken>()).Returns(false);
        _projects.GetRepositoriesAsync(existing.Id, Arg.Any<CancellationToken>()).Returns([]);
        _projects.GetAliasesAsync(existing.Id, Arg.Any<CancellationToken>()).Returns([]);

        var sut = CreateSut();
        var detail = await sut.UpdateAsync(existing.Id, new UpdateProjectRequest
        {
            Name = "Renamed",
            Slug = "renamed",
            ClientName = "New Client",
            BillingCode = "B-1",
            Currency = "EUR",
            IsActive = true
        });

        detail.Name.Should().Be("Renamed");
        detail.Slug.Should().Be("renamed");
        detail.ClientName.Should().Be("New Client");
        detail.Currency.Should().Be("EUR");
        await _projects.Received().UpdateAsync(existing, Arg.Any<CancellationToken>());
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_deactivates_project()
    {
        var existing = Project.Create("To Delete", "to-delete");
        _projects.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var sut = CreateSut();
        await sut.DeleteAsync(existing.Id);

        existing.IsActive.Should().BeFalse();
        await _projects.Received().UpdateAsync(existing, Arg.Any<CancellationToken>());
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
