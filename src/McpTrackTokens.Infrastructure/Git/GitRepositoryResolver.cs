using System.Diagnostics;
using System.Text;
using McpTrackTokens.Application.Interfaces;

namespace McpTrackTokens.Infrastructure.Git;

/// <summary>
/// Resolves Git repository metadata from a workspace, file, or repository path.
/// Never throws fatally — returns partial information when resolution is incomplete.
/// </summary>
public sealed class GitRepositoryResolver : IGitRepositoryResolver
{
    private readonly IPathNormalizer _paths;

    public GitRepositoryResolver(IPathNormalizer paths)
    {
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<GitRepositoryInfo> ResolveAsync(string? path, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Empty(warnings, "No path supplied for Git resolution.");
            }

            string? candidate;
            try
            {
                candidate = Path.GetFullPath(path.Trim());
            }
            catch (Exception ex)
            {
                warnings.Add($"Unable to expand path '{path}': {ex.Message}");
                return Empty(warnings);
            }

            // Resolution order: supplied path -> (treat as workspace/active file) -> search upward for .git
            var gitDir = FindGitDirectory(candidate, warnings);
            string? rootPath = null;

            if (gitDir is not null)
            {
                rootPath = Directory.GetParent(gitDir)?.FullName;
                if (string.Equals(Path.GetFileName(gitDir), ".git", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(gitDir))
                {
                    // Worktree / gitfile: still try rev-parse for the true toplevel.
                    rootPath = Path.GetDirectoryName(gitDir);
                }
            }

            // Prefer git rev-parse --show-toplevel when available.
            var workingDirectory = Directory.Exists(candidate)
                ? candidate
                : Path.GetDirectoryName(candidate);

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                var toplevel = await RunGitAsync(
                        workingDirectory,
                        "rev-parse --show-toplevel",
                        cancellationToken,
                        warnings)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(toplevel))
                {
                    rootPath = toplevel.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(rootPath) && gitDir is not null)
            {
                var parent = Directory.GetParent(gitDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                rootPath = parent?.FullName;
            }

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                // Folder-name fallback: treat nearest existing directory as a non-git root hint.
                var folder = Directory.Exists(candidate)
                    ? candidate
                    : Path.GetDirectoryName(candidate);

                warnings.Add("No Git repository found; returning folder path only.");
                var folderName = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFileName(folder.TrimEnd('\\', '/'));
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    warnings.Add($"Folder name candidate: {folderName}");
                }

                return new GitRepositoryInfo(
                    folder,
                    folder is null ? null : _paths.Normalize(folder),
                    null,
                    null,
                    null,
                    IsGitRepository: false);
            }

            string? remoteUrl = null;
            string? branch = null;

            var remote = await RunGitAsync(rootPath, "config --get remote.origin.url", cancellationToken, warnings)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(remote))
            {
                remoteUrl = remote.Trim();
            }
            else
            {
                warnings.Add("Git remote origin URL was not available.");
            }

            var branchName = await RunGitAsync(
                    rootPath,
                    "rev-parse --abbrev-ref HEAD",
                    cancellationToken,
                    warnings)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(branchName) &&
                !string.Equals(branchName.Trim(), "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                branch = branchName.Trim();
            }
            else
            {
                warnings.Add("Git branch could not be resolved.");
            }

            // Alias / folder-name signals are surfaced via warnings for callers that need them.
            var name = Path.GetFileName(rootPath.TrimEnd('\\', '/'));
            if (!string.IsNullOrWhiteSpace(name))
            {
                warnings.Add($"Repository folder name: {name}");
            }

            return new GitRepositoryInfo(
                rootPath,
                _paths.Normalize(rootPath),
                remoteUrl,
                string.IsNullOrWhiteSpace(remoteUrl) ? null : _paths.NormalizeRemoteUrl(remoteUrl),
                branch,
                IsGitRepository: true);
        }
        catch (Exception ex)
        {
            warnings.Add($"Git resolution failed: {ex.Message}");
            return Empty(warnings);
        }
    }

    private static GitRepositoryInfo Empty(List<string> warnings, string? extraWarning = null)
    {
        if (!string.IsNullOrWhiteSpace(extraWarning))
        {
            warnings.Add(extraWarning);
        }

        // Warnings are intentionally not part of GitRepositoryInfo; they are diagnostic only.
        _ = warnings;
        return new GitRepositoryInfo(null, null, null, null, null, IsGitRepository: false);
    }

    private static string? FindGitDirectory(string startPath, List<string> warnings)
    {
        try
        {
            var current = Directory.Exists(startPath)
                ? new DirectoryInfo(startPath)
                : new DirectoryInfo(Path.GetDirectoryName(startPath) ?? startPath);

            while (current is not null)
            {
                var gitPath = Path.Combine(current.FullName, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                {
                    return gitPath;
                }

                current = current.Parent;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Upward .git search failed: {ex.Message}");
        }

        return null;
    }

    private static async Task<string?> RunGitAsync(
        string workingDirectory,
        string arguments,
        CancellationToken cancellationToken,
        List<string> warnings)
    {
        try
        {
            if (!Directory.Exists(workingDirectory))
            {
                return null;
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            if (!process.Start())
            {
                warnings.Add($"Failed to start git ({arguments}).");
                return null;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    warnings.Add($"git {arguments}: {stderr.Trim()}");
                }

                return null;
            }

            return string.IsNullOrWhiteSpace(stdout) ? null : stdout.Trim();
        }
        catch (Exception ex)
        {
            warnings.Add($"git {arguments} failed: {ex.Message}");
            return null;
        }
    }
}
