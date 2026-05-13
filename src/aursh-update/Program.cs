using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace AurShell.UpdateTool;

/// <summary>
/// Standalone aursh-update binary. Lives separately from the main aursh
/// executable specifically so that it can be sudo'd:
///
///     sudo aursh-update              # pull + install
///     sudo aursh-update set <path>   # remember repo path for future updates
///     sudo aursh-update check        # report whether the local clone is behind
///
/// The main aursh shell's `aursh-update` builtin shells out to THIS binary
/// (no inline update logic), which lets the user run sudo against just the
/// updater without elevating the whole interactive shell.
/// </summary>
internal static class Program
{
    private const string ExpectedRemote = "https://github.com/patrickcortez/AurSh";

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
                return DoUpdate();

            string sub = args[0].ToLowerInvariant();
            return sub switch
            {
                "set"     => SetRepo(args),
                "check"   => CheckUpdate(),
                "where"   => PrintRepoPath(),
                "-h" or "--help" or "help" => PrintHelp(),
                _ => Fail($"unknown subcommand '{sub}'. Try 'aursh-update --help'."),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh-update: {ex.Message}");
            return 1;
        }
    }

    private static int PrintHelp()
    {
        Console.WriteLine("aursh-update — standalone AurShell updater\n");
        Console.WriteLine("Usage:");
        Console.WriteLine("  aursh-update                       Pull latest and reinstall.");
        Console.WriteLine("  aursh-update set <path-to-repo>    Remember the AurSh git checkout to update from.");
        Console.WriteLine("  aursh-update check                 Report how many commits behind the local clone is.");
        Console.WriteLine("  aursh-update where                 Print the currently configured repo path.");
        Console.WriteLine("");
        Console.WriteLine("Run under sudo (or as Administrator on Windows) if the install");
        Console.WriteLine("location requires elevated privileges:");
        Console.WriteLine("");
        Console.WriteLine("  sudo aursh-update");
        return 0;
    }

    private static int PrintRepoPath()
    {
        string? repo = GetUpdateRepoPath();
        if (string.IsNullOrEmpty(repo))
        {
            Console.Error.WriteLine("aursh-update: no repository configured. Run 'aursh-update set <path>' first.");
            return 1;
        }
        Console.WriteLine(repo);
        return 0;
    }

    private static int SetRepo(string[] args)
    {
        if (args.Length < 2)
            return Fail("aursh-update set <path-to-repo>");

        string path = Path.GetFullPath(args[1]);
        if (!Directory.Exists(path))
            return Fail($"directory '{path}' not found.");

        if (!ValidateRepo(path))
            return 1;

        SetUpdateRepoPath(path);
        Console.WriteLine($"Update repository set to: {path}");
        return 0;
    }

    private static int CheckUpdate()
    {
        string? repoPath = GetUpdateRepoPath();
        if (string.IsNullOrEmpty(repoPath))
            return Fail("no repository set. Use 'aursh-update set <path>'.");

        if (!Directory.Exists(repoPath))
            return Fail($"repository directory '{repoPath}' not found.");

        RunGit(repoPath, "fetch");
        string behind = RunGitOutput(repoPath, "rev-list HEAD..origin/main --count");
        if (string.IsNullOrEmpty(behind))
            behind = RunGitOutput(repoPath, "rev-list HEAD..origin/master --count");

        if (!int.TryParse(behind, out int count))
            return Fail("failed to check remote status.");

        Console.WriteLine(count == 0
            ? "AurShell is up to date."
            : $"AurShell is {count} commit(s) behind.");
        return 0;
    }

    private static int DoUpdate()
    {
        string? sourceDir = ResolveSourceDir();
        if (string.IsNullOrEmpty(sourceDir))
            return Fail("no repository set. Use 'aursh-update set <path>'.");

        if (!Directory.Exists(sourceDir))
            return Fail($"repository directory '{sourceDir}' not found.");

        Console.WriteLine($"Updating AurShell from {sourceDir}...");

        int pullCode = RunGit(sourceDir, "pull origin main");
        if (pullCode != 0)
        {
            // Fall back to master if main isn't the default branch.
            pullCode = RunGit(sourceDir, "pull origin master");
            if (pullCode != 0)
                return Fail("git pull failed.");
        }

        Console.WriteLine("Installing AurShell...");
        bool useMake = File.Exists(Path.Combine(sourceDir, "Makefile"));
        string installExe = useMake ? "make" : "dotnet";
        string installArgs = useMake
            ? "install"
            : $"build \"{Path.Combine(sourceDir, "src", "AurShell.csproj")}\" -c Release";

        int installCode = RunForeground(sourceDir, installExe, installArgs);
        if (installCode != 0)
            return Fail("install failed.");

        Console.WriteLine("Update complete.");
        return 0;
    }

    private static string? ResolveSourceDir()
    {
        string? configured = GetUpdateRepoPath();
        if (!string.IsNullOrEmpty(configured)) return configured;

        // Fallback: walk up from the binary's location looking for a .git dir.
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            string? parent = Directory.GetParent(dir)?.FullName;
            if (parent == dir || parent == null) break;
            dir = parent;
        }
        return null;
    }

    private static bool ValidateRepo(string path)
    {
        if (!Directory.Exists(Path.Combine(path, ".git")))
        {
            Console.Error.WriteLine($"aursh-update: '{path}' is not a git repository.");
            return false;
        }

        string remotes = RunGitOutput(path, "remote -v");
        foreach (string line in remotes.Split('\n'))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            int tabIdx = trimmed.IndexOf('\t');
            if (tabIdx < 0) continue;
            string urlPart = trimmed.Substring(tabIdx + 1).Trim();
            int spaceIdx = urlPart.IndexOf(' ');
            if (spaceIdx >= 0) urlPart = urlPart.Substring(0, spaceIdx);
            string normalized = urlPart.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? urlPart[..^4]
                : urlPart;
            if (normalized.Equals(ExpectedRemote, StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(ExpectedRemote + ".git", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        Console.Error.WriteLine($"aursh-update: repository does not have the expected remote ({ExpectedRemote}).");
        return false;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"aursh-update: {message}");
        return 1;
    }

    // ───────────────────────────── git / process helpers ─────────────────────────

    private static int RunGit(string workingDir, string args)
    {
        return RunForeground(workingDir, "git", args);
    }

    private static int RunForeground(string workingDir, string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return 127;
            proc.WaitForExit();
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh-update: {exe}: {ex.Message}");
            return 127;
        }
    }

    private static string RunGitOutput(string workingDir, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return "";
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            return proc.ExitCode == 0 ? output : "";
        }
        catch
        {
            return "";
        }
    }

    // ───────────────────────────── config storage ─────────────────────────
    //
    // Stored in <ConfigDirectory>/update_configs.txt as a comma-separated
    // list of key=value pairs. Today only `path` is used; the schema is
    // intentionally extensible (e.g. `branch=`, `remote=`) so future
    // versions can add fields without changing the file location.
    //
    // Example file contents:
    //     path=/home/cortez/Repos/AurSh,
    //
    // The trailing comma is part of the wire format — every key=value
    // pair is followed by a comma so the parser can tolerate a trailing
    // line break or whitespace.

    private const string ConfigFileName = "update_configs.txt";
    private const string LegacyConfigFileName = "update-repo";

    private static string UpdateConfigPath => Path.Combine(ConfigDirectory, ConfigFileName);
    private static string LegacyUpdateRepoPath => Path.Combine(ConfigDirectory, LegacyConfigFileName);

    private static string? GetUpdateRepoPath()
    {
        // Preferred: parse the structured update_configs.txt.
        if (File.Exists(UpdateConfigPath))
        {
            string? p = ReadConfigField("path");
            if (!string.IsNullOrEmpty(p)) return p;
        }

        // Backward-compat: older versions stored the raw path in a file
        // called `update-repo`. If that's all the user has, return it so
        // existing installs keep working until they next run `set`.
        if (File.Exists(LegacyUpdateRepoPath))
        {
            string content = File.ReadAllText(LegacyUpdateRepoPath).Trim();
            if (!string.IsNullOrEmpty(content)) return content;
        }

        return null;
    }

    private static void SetUpdateRepoPath(string path)
    {
        Directory.CreateDirectory(ConfigDirectory);
        WriteConfigField("path", path);
    }

    /// <summary>
    /// Read a single key from update_configs.txt. Returns null if the file
    /// doesn't exist or the key is absent. Trailing commas and whitespace
    /// around either side of `=` are tolerated.
    /// </summary>
    private static string? ReadConfigField(string key)
    {
        if (!File.Exists(UpdateConfigPath)) return null;
        string content;
        try { content = File.ReadAllText(UpdateConfigPath); }
        catch { return null; }

        foreach (string raw in content.Split(','))
        {
            string entry = raw.Trim();
            if (string.IsNullOrEmpty(entry)) continue;
            int eq = entry.IndexOf('=');
            if (eq <= 0) continue;
            string k = entry.Substring(0, eq).Trim();
            string v = entry.Substring(eq + 1).Trim();
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrEmpty(v) ? null : v;
        }
        return null;
    }

    /// <summary>
    /// Write/overwrite a key in update_configs.txt while preserving every
    /// other key/value already in the file. The final serialized form is
    /// `key1=value1,key2=value2,` (trailing comma intentional).
    /// </summary>
    private static void WriteConfigField(string key, string value)
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(UpdateConfigPath))
        {
            try
            {
                foreach (string raw in File.ReadAllText(UpdateConfigPath).Split(','))
                {
                    string e = raw.Trim();
                    if (string.IsNullOrEmpty(e)) continue;
                    int eq = e.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = e.Substring(0, eq).Trim();
                    string v = e.Substring(eq + 1).Trim();
                    if (!string.IsNullOrEmpty(k)) entries[k] = v;
                }
            }
            catch { /* fall through; we'll just overwrite the unreadable file */ }
        }

        entries[key] = value;

        var sb = new System.Text.StringBuilder();
        foreach (var kv in entries)
        {
            sb.Append(kv.Key);
            sb.Append('=');
            sb.Append(kv.Value);
            sb.Append(',');
        }
        File.WriteAllText(UpdateConfigPath, sb.ToString());
    }

    /// <summary>
    /// Mirror of AurShell.Utils.Platform.ConfigDirectory so that aursh-update
    /// reads/writes the same config file the main shell does.
    /// </summary>
    private static string ConfigDirectory
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                string? ad = Environment.GetEnvironmentVariable("APPDATA");
                if (!string.IsNullOrEmpty(ad)) return Path.Combine(ad, "aurshell");
            }
            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrEmpty(xdg)) return Path.Combine(xdg, "aurshell");
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "aurshell");
        }
    }
}
