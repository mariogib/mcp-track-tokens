using System.Text;

namespace McpTrackTokens.Setup.Integrations;

/// <summary>
/// Per-user post-install helper invoked by the MSI (impersonated).
/// Always writes local-host / HTTP MCP example config for the tray-deployed
/// API + MCP + dashboard stack. Optionally installs Cursor hooks scaffold.
/// Does not silently rewrite editor settings.
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

            WriteHostDeployNotes(options.InstallDir);
            WriteHttpMcpExample(options.InstallDir);

            if (!options.KeepDatabase)
            {
                PurgeUserData();
            }

            if (options.InstallHooks)
            {
                InstallHooks(options.InstallDir);
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

    private static void WriteHostDeployNotes(string installDir)
    {
        var text =
            """
            MCP Track Tokens — Windows host deploy
            ======================================

            The tray host (mcp-track-tokens-tray.exe) runs the full local stack:

              • HTTP API          http://127.0.0.1:5187/api/v1
              • HTTP MCP server   http://127.0.0.1:5187/mcp
              • Web dashboard     http://127.0.0.1:5187/  (wwwroot)
              • Desktop shell     Desktop\mcp-track-tokens-desktop.exe
              • SQLite database   %USERPROFILE%\.mcp-track-tokens\mcp-track-tokens.db

            Upgrades replace Program Files only. The SQLite database is kept by default
            (Setup option “Upgrade / keep existing SQLite database”). Uncheck that option
            only when you want a clean database. Uninstall leaves the database in place.

            Start "MCP Track Tokens Host" from the Start Menu (or enable Start with Windows).
            Open the dashboard from the tray menu or the Desktop shortcut.

            Point Cursor MCP at the HTTP endpoint (see mcp.http.example.json next to this
            file under LocalAppData\MCP Track Tokens, and under integrations\ in the
            install folder). Do not also run a separate stdio MCP against a different DB.
            """;

        var installNote = Path.Combine(installDir, "WINDOWS-HOST.txt");
        try
        {
            File.WriteAllText(installNote, text.Replace("\n", Environment.NewLine), Encoding.UTF8);
        }
        catch
        {
            // best-effort
        }

        TryWriteNote("WINDOWS-HOST.txt", text);
        Console.WriteLine($"Wrote host deploy notes for {installDir}");
    }

    private static void WriteHttpMcpExample(string installDir)
    {
        const string defaultKey = "OverTheMoon";
        var json =
            $$"""
            {
              "mcpServers": {
                "mcp-track-tokens": {
                  "url": "http://127.0.0.1:5187/mcp",
                  "headers": {
                    "Authorization": "Bearer {{defaultKey}}"
                  }
                }
              }
            }
            """;

        // Prefer bundled sample from the MSI integrations payload when present.
        var bundled = Path.Combine(installDir, "integrations", "mcp.http.json");
        var payload = File.Exists(bundled)
            ? File.ReadAllText(bundled)
            : json;

        var cursorDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cursor");
        Directory.CreateDirectory(cursorDir);
        var cursorExample = Path.Combine(cursorDir, "mcp-track-tokens.mcp.http.example.json");
        File.WriteAllText(cursorExample, payload.Trim() + Environment.NewLine, Encoding.UTF8);

        TryWriteNote("mcp.http.example.json", payload.Trim() + Environment.NewLine);
        Console.WriteLine($"Wrote HTTP MCP example to {cursorExample}");
        TryWriteNote(
            "mcp-http-config.txt",
            $"HTTP MCP example written to:{Environment.NewLine}{cursorExample}{Environment.NewLine}{Environment.NewLine}" +
            "Merge into your Cursor mcp.json manually. Start the tray host first so " +
            "http://127.0.0.1:5187/mcp is available. Default API key matches the tray host (OverTheMoon) " +
            "unless you changed MCP_TRACK_TOKENS_API_KEY.");
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
              "hooks": {
                "beforeSubmitPrompt": [
                  { "command": "./mcp-track-tokens-hooks/dist/prompt-submitted.js", "timeout": 5 }
                ],
                "sessionStart": [
                  { "command": "./mcp-track-tokens-hooks/dist/session-started.js", "timeout": 5 }
                ],
                "sessionEnd": [
                  { "command": "./mcp-track-tokens-hooks/dist/session-ended.js", "timeout": 5 }
                ],
                "subagentStart": [
                  { "command": "./mcp-track-tokens-hooks/dist/agent-started.js", "timeout": 5 }
                ],
                "subagentStop": [
                  { "command": "./mcp-track-tokens-hooks/dist/agent-completed.js", "timeout": 5 }
                ],
                "stop": [
                  { "command": "./mcp-track-tokens-hooks/dist/agent-completed.js", "timeout": 5 }
                ]
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

    private static void PurgeUserData()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".mcp-track-tokens");

        if (!Directory.Exists(dataDir))
        {
            Console.WriteLine($"No user data folder to purge at {dataDir}");
            return;
        }

        try
        {
            Directory.Delete(dataDir, recursive: true);
            Console.WriteLine($"Purged user data folder {dataDir}");
            TryWriteNote(
                "database-purged.txt",
                $"Deleted {dataDir} because Setup option “Upgrade / keep existing SQLite database” was unchecked.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to purge {dataDir}: {ex.Message}");
            TryWriteNote("database-purge-error.txt", ex.ToString());
        }
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
        public bool KeepDatabase { get; init; } = true;

        public static Options Parse(string[] args)
        {
            var installDir = "";
            var hooks = false;
            var keepDatabase = true;

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
                        // Legacy MSI argument; ignored (extension packaging removed).
                        _ = ReadFlag(args, ref i, defaultValue: false);
                        break;
                    case "--keep-database":
                        // Present without "1" (unchecked MSI checkbox) means purge.
                        keepDatabase = ReadFlag(args, ref i, defaultValue: false);
                        break;
                }
            }

            return new Options
            {
                InstallDir = installDir,
                InstallHooks = hooks,
                KeepDatabase = keepDatabase
            };
        }

        private static bool ReadFlag(string[] args, ref int index, bool defaultValue)
        {
            if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
            {
                return defaultValue;
            }

            var value = args[++index];
            // Unchecked WiX checkbox leaves an empty token after the switch.
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value is "1" or "true" or "yes" or "TRUE" or "Yes";
        }
    }
}
