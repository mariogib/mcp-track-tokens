using FluentValidation;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Domain.Exceptions;
using McpTrackTokens.Domain.Validation;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Detects projects by path, remote URL, and aliases; optionally auto-creates them.
/// </summary>
public sealed class ProjectDetectionService : IProjectDetectionService
{
    private readonly IProjectRepository _projects;
    private readonly IGitRepositoryResolver _git;
    private readonly IPathNormalizer _paths;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreateProjectRequest> _createValidator;
    private readonly IValidator<UpdateProjectRequest> _updateValidator;
    private readonly TrackingOptions _options;

    public ProjectDetectionService(
        IProjectRepository projects,
        IGitRepositoryResolver git,
        IPathNormalizer paths,
        IUnitOfWork unitOfWork,
        IValidator<CreateProjectRequest> createValidator,
        IValidator<UpdateProjectRequest> updateValidator,
        IOptions<TrackingOptions> options)
    {
        _projects = projects;
        _git = git;
        _paths = paths;
        _unitOfWork = unitOfWork;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<Project?> DetectAsync(
        string? workspacePath,
        string? repositoryPath,
        string? remoteUrl,
        string? activeFilePath = null,
        bool? createIfMissing = null,
        CancellationToken cancellationToken = default)
    {
        // Match registered paths before Git resolution. Hook payloads often carry Windows
        // paths that do not exist inside a Linux container; Git GetFullPath can mangle them.
        foreach (var candidate in DistinctNonEmpty(repositoryPath, workspacePath, activeFilePath))
        {
            var byDirectPath = await _projects
                .FindByNormalizedPathAsync(_paths.Normalize(candidate), cancellationToken)
                .ConfigureAwait(false);
            if (byDirectPath is not null)
            {
                return byDirectPath;
            }
        }

        var probePath = FirstNonEmpty(repositoryPath, workspacePath, activeFilePath);
        GitRepositoryInfo? gitInfo = null;
        if (!string.IsNullOrWhiteSpace(probePath))
        {
            gitInfo = await _git.ResolveAsync(probePath, cancellationToken).ConfigureAwait(false);
        }

        var effectiveRepoPath = FirstNonEmpty(repositoryPath, workspacePath, gitInfo?.RootPath);
        var effectiveRemote = FirstNonEmpty(remoteUrl, gitInfo?.RemoteUrl);

        if (!string.IsNullOrWhiteSpace(effectiveRepoPath))
        {
            var byPath = await _projects
                .FindByNormalizedPathAsync(_paths.Normalize(effectiveRepoPath), cancellationToken)
                .ConfigureAwait(false);
            if (byPath is not null)
            {
                return byPath;
            }

            var folderName = Path.GetFileName(_paths.Normalize(effectiveRepoPath).TrimEnd('/'));
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                var byAlias = await _projects
                    .FindByAliasAsync(folderName.ToLowerInvariant(), AliasType.RepositoryName, cancellationToken)
                    .ConfigureAwait(false);
                if (byAlias is not null)
                {
                    return byAlias;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(effectiveRemote))
        {
            var byRemote = await _projects
                .FindByNormalizedRemoteUrlAsync(_paths.NormalizeRemoteUrl(effectiveRemote), cancellationToken)
                .ConfigureAwait(false);
            if (byRemote is not null)
            {
                return byRemote;
            }

            var byRemoteAlias = await _projects
                .FindByAliasAsync(_paths.NormalizeRemoteUrl(effectiveRemote), AliasType.RemoteUrl, cancellationToken)
                .ConfigureAwait(false);
            if (byRemoteAlias is not null)
            {
                return byRemoteAlias;
            }
        }

        if (!string.IsNullOrWhiteSpace(workspacePath))
        {
            var byWorkspace = await _projects
                .FindByAliasAsync(_paths.Normalize(workspacePath), AliasType.WorkspaceName, cancellationToken)
                .ConfigureAwait(false);
            if (byWorkspace is not null)
            {
                return byWorkspace;
            }
        }

        var shouldCreate = createIfMissing ?? _options.AutoCreateProjects;
        if (!shouldCreate || string.IsNullOrWhiteSpace(effectiveRepoPath))
        {
            return null;
        }

        var name = Path.GetFileName(_paths.Normalize(effectiveRepoPath).TrimEnd('/'));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "auto-project";
        }

        var created = await RegisterAsync(
                new CreateProjectRequest
                {
                    Name = name,
                    RepositoryPath = effectiveRepoPath,
                    RemoteUrl = effectiveRemote
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await _projects.GetByIdAsync(created.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProjectDetailDto> RegisterAsync(
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);

        var currency = string.IsNullOrWhiteSpace(request.Currency)
            ? _options.DefaultCurrency
            : request.Currency;
        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? ProjectValidator.Slugify(request.Name)
            : request.Slug.Trim().ToLowerInvariant();

        if (await _projects.SlugExistsAsync(slug, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            var suffix = 2;
            var candidate = $"{slug}-{suffix}";
            while (await _projects.SlugExistsAsync(candidate, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                suffix++;
                candidate = $"{slug}-{suffix}";
            }

            slug = candidate;
        }

        var project = Project.Create(
            request.Name,
            slug,
            currency,
            request.ClientName,
            request.BillingCode,
            request.RepositoryPath,
            request.RemoteUrl);

        await _projects.AddAsync(project, cancellationToken).ConfigureAwait(false);
        await _projects
            .SetRepositoryAsync(project.Id, request.RepositoryPath, request.RemoteUrl, cancellationToken)
            .ConfigureAwait(false);

        if (request.Aliases is not null)
        {
            foreach (var alias in request.Aliases.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                await _projects
                    .AddAliasAsync(ProjectAlias.Create(project.Id, alias, AliasType.Manual), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await ToDetailDtoAsync(project, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProjectDetailDto> UpdateAsync(
        Guid projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);

        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Project), projectId);

        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? project.Slug
            : request.Slug.Trim().ToLowerInvariant();

        if (await _projects.SlugExistsAsync(slug, projectId, cancellationToken).ConfigureAwait(false))
        {
            throw new DuplicateEntityException(nameof(Project), slug);
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? project.Name : request.Name;
        var currency = string.IsNullOrWhiteSpace(request.Currency)
            ? project.Currency
            : request.Currency;

        var repositoryPath = request.RepositoryPath ?? project.PrimaryRepositoryPath;
        var remoteUrl = request.RemoteUrl ?? project.PrimaryRemoteUrl;

        project.UpdateDetails(
            name,
            slug,
            currency,
            request.ClientName,
            request.BillingCode,
            repositoryPath,
            remoteUrl,
            request.IsActive ?? project.IsActive);

        await _projects.UpdateAsync(project, cancellationToken).ConfigureAwait(false);
        await _projects
            .SetRepositoryAsync(project.Id, project.PrimaryRepositoryPath, project.PrimaryRemoteUrl, cancellationToken)
            .ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await ToDetailDtoAsync(project, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Project), projectId);

        project.Deactivate();
        await _projects.UpdateAsync(project, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProjectDetailDto> ToDetailDtoAsync(Project project, CancellationToken cancellationToken)
    {
        var repositories = await _projects.GetRepositoriesAsync(project.Id, cancellationToken).ConfigureAwait(false);
        var aliases = await _projects.GetAliasesAsync(project.Id, cancellationToken).ConfigureAwait(false);

        return new ProjectDetailDto
        {
            Id = project.Id,
            Name = project.Name,
            Slug = project.Slug,
            ClientName = project.ClientName,
            BillingCode = project.BillingCode,
            Currency = project.Currency,
            PrimaryRepositoryPath = project.PrimaryRepositoryPath,
            PrimaryRemoteUrl = project.PrimaryRemoteUrl,
            IsActive = project.IsActive,
            CreatedAtUtc = project.CreatedAtUtc,
            UpdatedAtUtc = project.UpdatedAtUtc,
            Repositories = repositories.Select(MapRepository).ToList(),
            Aliases = aliases.Select(MapAlias).ToList()
        };
    }

    private static ProjectRepositoryDto MapRepository(ProjectRepository repository) => new()
    {
        Id = repository.Id,
        ProjectId = repository.ProjectId,
        LocalPath = repository.LocalPath,
        NormalizedPath = repository.NormalizedPath,
        RemoteUrl = repository.RemoteUrl,
        NormalizedRemoteUrl = repository.NormalizedRemoteUrl,
        DefaultBranch = repository.DefaultBranch,
        IsActive = repository.IsActive
    };

    private static ProjectAliasDto MapAlias(ProjectAlias alias) => new()
    {
        Id = alias.Id,
        ProjectId = alias.ProjectId,
        Alias = alias.Alias,
        NormalizedAlias = alias.NormalizedAlias,
        AliasType = alias.AliasType.ToString()
    };

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static IEnumerable<string> DistinctNonEmpty(params string?[] values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (seen.Add(trimmed))
            {
                yield return trimmed;
            }
        }
    }
}
