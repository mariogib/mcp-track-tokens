using System.Diagnostics;
using System.Text;

namespace McpTrackTokens.Setup.Integrations;

/// <summary>
/// Per-user post-install helper invoked by the MSI (impersonated).
/// Installs Cursor hooks scaffold and/or the VS Code/Cursor VSIX.
/// Does not rewrite editor settings.json / hooks.json merges beyond the
/// same scaffold the CLI install-cursor-hooks command writes.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            if (string.IsNullOrWhiteSpace(options.InstallDir) || !Directory.Exists(options.InstallDir))
            {
                Console.Error.WriteLine("Missing or invalid --install-dir.");
                return 0;
            }

            if (options.InstallHooks)
            {
                InstallHooks(options.InstallDir);
            }

            if (options.InstallExtension)
            {
                InstallExtension(options.InstallDir);
            }

            return 0;
        }
        catch (Exception ex)
        {
            // Never fail the MSI over optional editor integrations.
            Console.Error.WriteLine(ex.Message);
            TryWriteNote("integrations-error.txt", ex.ToString());
            return 0;
        }
    }

    private static void InstallHooks(string installDir)
    {
        var source = Path.Combine(installDir, "integrations", "cursor-hooks");
        if (!Directory.Exists(source))
        {
            Console.Error.WriteLine($"Hooks payload missing: {source}");
            return;
        }

        var cursorDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cursor");
        var hooksTarget = Path.Combine(cursorDir, "mcp-track-tokens-hooks");
        Directory.CreateDirectory(hooksTarget);
        CopyDirectory(source, hooksTarget);

        var exampleConfigPath = Path.Combine(cursorDir, "mcp-track-tokens-hooks.example.json");
        File.WriteAllText(exampleConfigPath, """
            {
              "version": 1,
              "serverUrl": "http://127.0.0.1:5187",
              "apiKeyEnv": "MCP_TRACK_TOKENS_API_KEY",
              "hooks": {
                "promptSubmitted": "./mcp-track-tokens-hooks/dist/prompt-submitted.js",
                "agentStarted": "./mcp-track-tokens-hooks/dist/agent-started.js",
                "agentCompleted": "./mcp-track-tokens-hooks/dist/agent-completed.js",
                "sessionStarted": "./mcp-track-tokens-hooks/dist/session-started.js",
                "sessionEnded": "./mcp-track-tokens-hooks/dist/session-ended.js"
              }
            }
            """);

        Console.WriteLine($"Installed Cursor hooks scaffold to {hooksTarget}");
        Console.WriteLine($"Wrote example config to {exampleConfigPath}");
        TryWriteNote(
            "cursor-hooks-installed.txt",
            $"Hooks installed to:{Environment.NewLine}{hooksTarget}{Environment.NewLine}{Environment.NewLine}" +
            $"Merge {exampleConfigPath} into your Cursor hooks configuration manually.");
    }

    private static void InstallExtension(string installDir)
    {
        var integrationsDir = Path.Combine(installDir, "integrations");
        var vsix = Directory.Exists(integrationsDir)
            ? Directory.GetFiles(integrationsDir, "*.vsix").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;

        if (vsix is null)
        {
            Console.Error.WriteLine("VSIX payload missing under integrations\\.");
            return;
        }

        var editor = FindEditorCli();
        if (editor is null)
        {
            var message =
                $"VSIX is available at:{Environment.NewLine}{vsix}{Environment.NewLine}{Environment.NewLine}" +
                "Install manually (Cursor or VS Code):{Environment.NewLine}" +
                $"  cursor --install-extension \"{vsix}\"{Environment.NewLine}" +
                $"  code --install-extension \"{vsix}\"";
            Console.WriteLine(message);
            TryWriteNote("extension-manual-install.txt", message);
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = editor,
            Arguments = $"--install-extension \"{vsix}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            Console.Error.WriteLine($"Failed to start {editor}");
            return;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);
        Console.WriteLine(stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            Console.Error.WriteLine(stderr);
        }

        Console.WriteLine($"Installed VSIX via {Path.GetFileName(editor)}");
        TryWriteNote(
            "extension-installed.txt",
            $"Installed {vsix}{Environment.NewLine}via {editor}{Environment.NewLine}" +
            "Extension settings were not modified.");
    }

    private static string? FindEditorCli()
    {
        foreach (var name in new[] { "cursor.cmd", "cursor.exe", "cursor", "code.cmd", "code.exe", "code" })
        {
            var onPath = FindOnPath(name);
            if (onPath is not null)
            {
                return onPath;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(localAppData, "Programs", "cursor", "resources", "app", "bin", "cursor.cmd"),
            Path.Combine(localAppData, "Programs", "cursor", "resources", "app", "bin", "cursor"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "bin", "code.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "bin", "code.cmd"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // ignore malformed PATH segments
            }
        }

        return null;
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void TryWriteNote(string fileName, string contents)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MCP Track Tokens");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, fileName), contents, Encoding.UTF8);
        }
        catch
        {
            // best-effort only
        }
    }

    private sealed class Options
    {
        public string InstallDir { get; init; } = "";
        public bool InstallHooks { get; init; }
        public bool InstallExtension { get; init; }

        public static Options Parse(string[] args)
        {
            var installDir = "";
            var hooks = false;
            var extension = false;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--install-dir" when i + 1 < args.Length:
                        installDir = args[++i].Trim('"');
                        break;
                    case "--hooks":
                        // MSI passes "--hooks 1" or "--hooks" / "--hooks " when unchecked.
                        hooks = ReadFlag(args, ref i, defaultValue: false);
                        break;
                    case "--extension":
                        extension = ReadFlag(args, ref i, defaultValue: false);
                        break;
                }
            }

            return new Options
            {
                InstallDir = installDir,
                InstallHooks = hooks,
                InstallExtension = extension
            };
        }

        private static bool ReadFlag(string[] args, ref int index, bool defaultValue)
        {
            if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
            {
                return defaultValue;
            }

            var value = args[++index];
            return value is "1" or "true" or "yes" or "TRUE" or "Yes";
        }
    }
}
