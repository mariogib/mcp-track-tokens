using System.Security.Cryptography;
using System.Text;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Exceptions;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// Generates secure tracking API keys and stores SHA-256 hashes only.
/// </summary>
public sealed class ApiKeyService : IApiKeyService
{
    private readonly IApiKeyRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public ApiKeyService(IApiKeyRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<ApiKeyCreateResultDto> CreateAsync(
        CreateApiKeyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var plaintext = GenerateSecureKey();
        var hash = HashKey(plaintext);
        var entity = TrackingApiKey.Create(
            request.Name,
            hash,
            request.ExpiresAtUtc,
            request.AllowedEditors,
            request.AllowedMachineNames);

        await _repository.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ApiKeyCreateResultDto
        {
            Id = entity.Id,
            Name = entity.Name,
            ApiKey = plaintext,
            CreatedAtUtc = entity.CreatedAtUtc,
            ExpiresAtUtc = entity.ExpiresAtUtc,
            AllowedEditors = entity.AllowedEditors,
            AllowedMachineNames = entity.AllowedMachineNames
        };
    }

    /// <inheritdoc />
    public async Task<bool> VerifyAsync(string plaintextKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey))
        {
            return false;
        }

        var hash = HashKey(plaintextKey.Trim());
        var entity = await _repository.FindByHashAsync(hash, cancellationToken).ConfigureAwait(false);
        if (entity is null || !entity.IsValidAt(DateTimeOffset.UtcNow))
        {
            return false;
        }

        entity.RecordUse();
        await _repository.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(TrackingApiKey), id);

        if (entity.IsActive)
        {
            var active = await _repository.ListAsync(activeOnly: true, cancellationToken).ConfigureAwait(false);
            if (active.Count <= 1)
            {
                throw new InvalidOperationException(
                    "Cannot revoke the last active API key. Create another key first, or keep the configured bootstrap key active.");
            }
        }

        entity.IsActive = false;
        await _repository.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(TrackingApiKey), id);

        if (entity.IsActive)
        {
            throw new InvalidOperationException(
                "Cannot delete an active API key. Revoke it first, then delete.");
        }

        await _repository.DeleteAsync(entity, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TrackingApiKey>> ListAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
        => _repository.ListAsync(activeOnly, cancellationToken);

    /// <summary>
    /// Computes the SHA-256 hex digest used for storage and verification.
    /// </summary>
    public static string HashKey(string plaintextKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextKey);
        var bytes = Encoding.UTF8.GetBytes(plaintextKey.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GenerateSecureKey()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return "mtt_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
