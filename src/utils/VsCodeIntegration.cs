using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AurShell.Utils;

/// <summary>
/// Detects VS Code and Antigravity IDE installations and auto-configures
/// their terminal profile settings to include AurShell as a terminal option
/// with the JetBrainsMono Nerd Font as the terminal font.
///
/// Runs once per install — a sentinel file at ~/.aursh/.vscode-configured
/// prevents repeated writes. The user can delete that file to re-trigger
/// the configuration or set AURSH_NO_VSCODE_CONFIG=1 to suppress it entirely.
/// </summary>
public static class VsCodeIntegration
{
    private static readonly string SentinelFileName = ".vscode-configured";

    /// <summary>
    /// Entry point called from Shell initialization. Checks whether VS Code
    /// or Antigravity IDE settings directories exist and injects the AurShell
    /// terminal profile if not already present. No-ops silently if the editors
    /// are not installed or if configuration was already applied.
    /// </summary>
    public static void EnsureProfileConfigured()
    {
        string? suppress = System.Environment.GetEnvironmentVariable("AURSH_NO_VSCODE_CONFIG");
        if (suppress == "1" || string.Equals(suppress, "true", System.StringComparison.OrdinalIgnoreCase))
            return;

        string aurshDir = Path.Combine(Platform.HomeDirectory, ".aursh");
        string sentinelPath = Path.Combine(aurshDir, SentinelFileName);
        if (File.Exists(sentinelPath))
            return;

        string? aurshExe = FindAurshExecutable();
        if (string.IsNullOrEmpty(aurshExe))
            return;

        bool anyConfigured = false;

        foreach (string settingsPath in EnumerateSettingsPaths())
        {
            if (TryConfigureSettings(settingsPath, aurshExe))
                anyConfigured = true;
        }

        if (anyConfigured)
        {
            try
            {
                Directory.CreateDirectory(aurshDir);
                File.WriteAllText(sentinelPath, $"Configured at {System.DateTime.UtcNow:O}\n");
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Finds the aursh executable path. Checks the current process path first,
    /// then looks in standard install locations and PATH.
    /// </summary>
    private static string? FindAurshExecutable()
    {
        // Current process location
        string? currentExe = System.Environment.ProcessPath;
        if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
            return currentExe;

        // Standard install path on Windows
        if (Platform.CurrentOS == OperatingSystemType.Windows)
        {
            string programFiles = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
                "AurShell", "aursh.exe");
            if (File.Exists(programFiles))
                return programFiles;
        }

        // PATH lookup
        string exeName = Platform.CurrentOS == OperatingSystemType.Windows ? "aursh.exe" : "aursh";
        string? onPath = Platform.FindExecutableInPath(exeName);
        if (!string.IsNullOrEmpty(onPath))
            return onPath;

        return null;
    }

    /// <summary>
    /// Yields all known VS Code and Antigravity IDE settings.json paths
    /// for the current platform.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<string> EnumerateSettingsPaths()
    {
        string[] editorDirs;

        switch (Platform.CurrentOS)
        {
            case OperatingSystemType.Windows:
                {
                    string? appData = System.Environment.GetEnvironmentVariable("APPDATA");
                    if (!string.IsNullOrEmpty(appData))
                    {
                        editorDirs = new[]
                        {
                        Path.Combine(appData, "Code", "User"),
                        Path.Combine(appData, "Code - Insiders", "User"),
                        Path.Combine(appData, "VSCodium", "User"),
                        Path.Combine(appData, "Antigravity", "User"),
                    };
                        foreach (string dir in editorDirs)
                        {
                            string settingsPath = Path.Combine(dir, "settings.json");
                            if (Directory.Exists(dir))
                                yield return settingsPath;
                        }
                    }
                    break;
                }
            case OperatingSystemType.MacOS:
                {
                    string lib = Path.Combine(Platform.HomeDirectory, "Library", "Application Support");
                    editorDirs = new[]
                    {
                    Path.Combine(lib, "Code", "User"),
                    Path.Combine(lib, "Code - Insiders", "User"),
                    Path.Combine(lib, "VSCodium", "User"),
                    Path.Combine(lib, "Antigravity", "User"),
                };
                    foreach (string dir in editorDirs)
                    {
                        string settingsPath = Path.Combine(dir, "settings.json");
                        if (Directory.Exists(dir))
                            yield return settingsPath;
                    }
                    break;
                }
            default:
                {
                    string configBase = System.Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                                        ?? Path.Combine(Platform.HomeDirectory, ".config");
                    editorDirs = new[]
                    {
                    Path.Combine(configBase, "Code", "User"),
                    Path.Combine(configBase, "Code - Insiders", "User"),
                    Path.Combine(configBase, "VSCodium", "User"),
                    Path.Combine(configBase, "Antigravity", "User"),
                };
                    foreach (string dir in editorDirs)
                    {
                        string settingsPath = Path.Combine(dir, "settings.json");
                        if (Directory.Exists(dir))
                            yield return settingsPath;
                    }
                    break;
                }
        }
    }

    /// <summary>
    /// Reads the settings.json at the given path, injects the AurShell terminal
    /// profile and font family if not already present, and writes it back.
    /// Returns true if any changes were made.
    /// </summary>
    private static bool TryConfigureSettings(string settingsPath, string aurshExe)
    {
        try
        {
            JsonNode? root;
            if (File.Exists(settingsPath))
            {
                string existingJson = File.ReadAllText(settingsPath);
                if (string.IsNullOrWhiteSpace(existingJson))
                    root = new JsonObject();
                else
                    root = JsonNode.Parse(existingJson, documentOptions: new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });
            }
            else
            {
                root = new JsonObject();
            }

            if (root is not JsonObject settings)
                return false;

            bool modified = false;

            // Determine the OS-specific profile key
            string profilesKey = Platform.CurrentOS switch
            {
                OperatingSystemType.Windows => "terminal.integrated.profiles.windows",
                OperatingSystemType.MacOS => "terminal.integrated.profiles.osx",
                _ => "terminal.integrated.profiles.linux"
            };

            // Ensure the profiles object exists
            if (!settings.ContainsKey(profilesKey) || settings[profilesKey] is not JsonObject)
                settings[profilesKey] = new JsonObject();

            var profiles = settings[profilesKey]!.AsObject();

            // Add AurShell profile if not already present
            if (!profiles.ContainsKey("AurShell"))
            {
                string escapedPath = aurshExe.Replace("\\", "\\\\");
                var profileNode = new JsonObject
                {
                    ["path"] = aurshExe,
                    ["icon"] = "flame",
                    ["color"] = "terminal.ansiBlue"
                };
                profiles.Add("AurShell", profileNode);
                modified = true;
            }

            // Set terminal font family if not already configured
            string fontKey = "terminal.integrated.fontFamily";
            if (!settings.ContainsKey(fontKey))
            {
                settings[fontKey] = "JetBrainsMonoNL Nerd Font";
                modified = true;
            }

            if (modified)
            {
                string? parentDir = Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrEmpty(parentDir))
                    Directory.CreateDirectory(parentDir);

                var writeOptions = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string outputJson = settings.ToJsonString(writeOptions);
                File.WriteAllText(settingsPath, outputJson);
            }

            return modified;
        }
        catch
        {
            return false;
        }
    }
}
