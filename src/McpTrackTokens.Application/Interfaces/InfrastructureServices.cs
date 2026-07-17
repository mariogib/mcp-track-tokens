using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Enums;

namespace McpTrackTokens.Application.Interfaces;

/// <summary>
/// Resolved Git repository metadata.
/// </summary>
/// <param name="RootPath">Absolute repository root path.</param>
/// <param name="NormalizedRootPath">Normalized root path.</param>
/// <param name="RemoteUrl">Primary remote URL when available.</param>
/// <param name="NormalizedRemoteUrl">Normalized remote URL.</param>
/// <param name="Branch">Current branch name.</param>
/// <param name="IsGitRepository">Whether a Git repository was found.</param>
public sealed record GitRepositoryInfo(
    string? RootPath,
    string? NormalizedRootPath,
    string? RemoteUrl,
    string? NormalizedRemoteUrl,
    string? Branch,
    bool IsGitRepository);

/// <summary>
/// Resolves Git repository metadata from a workspace or file path.
/// </summary>
public interface IGitRepositoryResolver
{
    Task<GitRepositoryInfo> ResolveAsync(string? path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Imports Cursor usage files into normalized records.
/// </summary>
public interface ICursorUsageImporter
{
    Task<ImportPreviewDto> PreviewAsync(
        ImportCursorUsageRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ImportResultDto> ImportAsync(
        ImportCursorUsageRequestDto request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Detects Cursor usage file formats.
/// </summary>
public interface ICursorUsageFormatDetector
{
    Task<UsageSource> DetectAsync(string filePath, CancellationToken cancellationToken = default);

    Task<UsageSource> DetectAsync(Stream content, string? fileName = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Maps raw Cursor usage columns to normalized fields.
/// </summary>
public interface ICursorUsageColumnMapper
{
    IReadOnlyDictionary<string, string> MapColumns(
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<string, string>? overrides = null);
}

/// <summary>
/// Normalizes provider-specific usage rows into domain entities.
/// </summary>
public interface IExternalUsageNormalizer
{
    Task<IReadOnlyList<ExternalUsageRecord>> NormalizeAsync(
        UsageSource source,
        IReadOnlyList<NormalizedUsageRecordDto> records,
        Guid? importBatchId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes report payloads to disk in supported formats.
/// </summary>
public interface IReportExporter
{
    Task<ExportResultDto> ExportAsync(
        object report,
        ExportFormat format,
        string filePath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Encrypts and decrypts optional prompt/response content at rest.
/// </summary>
public interface IContentEncryptionService
{
    Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default);

    Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default);

    bool IsConfigured { get; }
}

/// <summary>
/// Normalizes filesystem paths for comparison and storage.
/// </summary>
public interface IPathNormalizer
{
    string Normalize(string? path);

    string NormalizeRemoteUrl(string? remoteUrl);
}

/// <summary>
/// Computes content hashes for import deduplication.
/// </summary>
public interface IFileHashService
{
    Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default);

    Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default);
}
