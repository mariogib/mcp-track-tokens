using Microsoft.EntityFrameworkCore;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;
using McpTrackTokens.Infrastructure.Persistence;
using DomainProjectRepository = McpTrackTokens.Domain.Entities.ProjectRepository;

namespace McpTrackTokens.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IProjectRepository"/>.
/// </summary>
public sealed class ProjectRepository : IProjectRepository
{
    private readonly TrackingDbContext _db;

    public ProjectRepository(TrackingDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Project?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
        => _db.Projects
            .FirstOrDefaultAsync(p => p.Slug == slug.Trim().ToLowerInvariant(), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Project>> ListAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = _db.Projects.AsNoTracking().AsQueryable();
        if (activeOnly)
        {
            query = query.Where(p => p.IsActive);
        }

        return await query.OrderBy(p => p.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Project>> ListByClientAsync(string clientName, CancellationToken cancellationToken = default)
    {
        var normalized = clientName.Trim();
        return await _db.Projects.AsNoTracking()
            .Where(p => p.ClientName != null && p.ClientName == normalized)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Project?> FindByNormalizedPathAsync(string normalizedPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        var path = normalizedPath.Trim();
        var repo = await _db.ProjectRepositories.AsNoTracking()
            .Where(r => r.IsActive && r.NormalizedPath == path)
            .Select(r => r.ProjectId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (repo != Guid.Empty)
        {
            return await GetByIdAsync(repo, cancellationToken).ConfigureAwait(false);
        }

        var candidates = await _db.Projects.AsNoTracking()
            .Where(p => p.PrimaryRepositoryPath != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidates.FirstOrDefault(p =>
            string.Equals(
                McpTrackTokens.Domain.ValueObjects.NormalizedPath.Normalize(p.PrimaryRepositoryPath),
                path,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<Project?> FindByNormalizedRemoteUrlAsync(
        string normalizedRemoteUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedRemoteUrl))
        {
            return null;
        }

        var remote = normalizedRemoteUrl.Trim();
        var repo = await _db.ProjectRepositories.AsNoTracking()
            .Where(r => r.IsActive && r.NormalizedRemoteUrl == remote)
            .Select(r => r.ProjectId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (repo != Guid.Empty)
        {
            return await GetByIdAsync(repo, cancellationToken).ConfigureAwait(false);
        }

        return await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PrimaryRemoteUrl != null && p.PrimaryRemoteUrl == remote, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Project?> FindByAliasAsync(
        string normalizedAlias,
        AliasType? aliasType = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedAlias))
        {
            return null;
        }

        var alias = normalizedAlias.Trim();
        var query = _db.ProjectAliases.AsNoTracking().Where(a => a.NormalizedAlias == alias);
        if (aliasType is not null)
        {
            query = query.Where(a => a.AliasType == aliasType.Value);
        }

        var projectId = await query.Select(a => a.ProjectId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return projectId == Guid.Empty ? null : await GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        await _db.Projects.AddAsync(project, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        _db.Projects.Update(project);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task AddRepositoryAsync(DomainProjectRepository repository, CancellationToken cancellationToken = default)
    {
        await _db.ProjectRepositories.AddAsync(repository, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAliasAsync(ProjectAlias alias, CancellationToken cancellationToken = default)
    {
        await _db.ProjectAliases.AddAsync(alias, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DomainProjectRepository>> GetRepositoriesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
        => await _db.ProjectRepositories.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.LocalPath)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectAlias>> GetAliasesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
        => await _db.ProjectAliases.AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.Alias)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<bool> SlugExistsAsync(
        string slug,
        Guid? excludingProjectId = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var query = _db.Projects.AsNoTracking().Where(p => p.Slug == normalized);
        if (excludingProjectId is Guid id)
        {
            query = query.Where(p => p.Id != id);
        }

        return query.AnyAsync(cancellationToken);
    }
}
