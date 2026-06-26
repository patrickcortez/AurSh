using System;
using System.Collections.Generic;
using System.IO;
using AurShell.Core;
using AurShell.Parser;
using AurShell.Utils;

namespace AurShell.Commands;

public static class AurshUpdateCommand
{
    private static string? FindAurshUpdateExecutable()
    {
        string exeName = Platform.CurrentOS == OperatingSystemType.Windows
            ? "aursh-update.exe"
            : "aursh-update";

        // 1. Look next to the running aursh binary first (install-time layout).
        string baseDir = AppContext.BaseDirectory;
        string adjacent = Path.Combine(baseDir, exeName);
        if (File.Exists(adjacent))
            return adjacent;

        // 2. Walk up looking for a bin/ directory (developer layout: repo/bin/).
        string? dir = baseDir;
        for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, "bin", exeName);
            if (File.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        // 3. Fall back to PATH.
        string? onPath = Utils.Platform.FindExecutableInPath(
            Platform.CurrentOS == OperatingSystemType.Windows ? "aursh-update.exe" : "aursh-update");
        if (!string.IsNullOrEmpty(onPath))
            return onPath;

        return null;
    }

    // Stored in <ConfigDirectory>/update_configs.txt as one `key=value,`
    // pair per line. The schema is shared with the standalone aursh-update
    // binary so both readers agree on the same file.
    //
    // Recognized keys: `path` (repo checkout location), `branch` (which
    // branch check/update/pull should track). The legacy `update-repo`
    // flat-text file is still read for backward compatibility.
    private static string UpdateConfigPath => Path.Combine(Platform.ConfigDirectory, "update_configs.txt");
    private static string LegacyUpdateRepoPath => Path.Combine(Platform.ConfigDirectory, "update-repo");

    private static string? GetUpdateRepoPath()
    {
        if (File.Exists(UpdateConfigPath))
        {
            string? p = ReadUpdateConfigField("path");
            if (!string.IsNullOrEmpty(p)) return p;
        }
        if (File.Exists(LegacyUpdateRepoPath))
        {
            string content = File.ReadAllText(LegacyUpdateRepoPath).Trim();
            if (!string.IsNullOrEmpty(content)) return content;
        }
        return null;
    }

    private static void SetUpdateRepoPath(string path)
    {
        Directory.CreateDirectory(Platform.ConfigDirectory);
        WriteUpdateConfigField("path", path);
    }

    private static string? GetUpdateBranch()
    {
        if (!File.Exists(UpdateConfigPath)) return null;
        return ReadUpdateConfigField("branch");
    }

    private static void SetUpdateBranch(string branch)
    {
        Directory.CreateDirectory(Platform.ConfigDirectory);
        WriteUpdateConfigField("branch", branch);
    }

    /// <summary>
    /// Resolves the branch to use for git operations in the configured repo.
    /// Prefers <c>branch=</c> from update_configs.txt; falls back to the
    /// repo's currently checked-out HEAD; finally to <c>main</c>.
    /// </summary>
    private static string ResolveUpdateBranch(string repoPath)
    {
        string? stored = GetUpdateBranch();
        if (!string.IsNullOrEmpty(stored)) return stored!;

        string head = RunGitOutput(repoPath, "rev-parse --abbrev-ref HEAD").Trim();
        if (!string.IsNullOrEmpty(head) && head != "HEAD") return head;

        return "main";
    }

    private static string? ReadUpdateConfigField(string key)
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

    private static void WriteUpdateConfigField(string key, string value)
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
            catch { /* overwrite unreadable file */ }
        }

        entries[key] = value;

        // Write one `key=value,` per line. `path` always appears first,
        // followed by `branch` directly below it; future keys append in
        // insertion order so the file stays diffable.
        var ordered = new List<KeyValuePair<string, string>>();
        if (entries.TryGetValue("path", out string? p)) ordered.Add(new("path", p));
        if (entries.TryGetValue("branch", out string? b)) ordered.Add(new("branch", b));
        foreach (var kv in entries)
        {
            if (kv.Key.Equals("path", StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Key.Equals("branch", StringComparison.OrdinalIgnoreCase)) continue;
            ordered.Add(new(kv.Key, kv.Value));
        }

        var sb = new System.Text.StringBuilder();
        foreach (var kv in ordered)
        {
            sb.Append(kv.Key);
            sb.Append('=');
            sb.Append(kv.Value);
            sb.Append(',');
            sb.Append('\n');
        }
        File.WriteAllText(UpdateConfigPath, sb.ToString());
    }

    private static bool ValidateRepo(string path)
    {
        string gitDir = Path.Combine(path, ".git");
        if (!Directory.Exists(gitDir))
        {
            Console.Error.WriteLine($"aursh: aursh-update: '{path}' is not a git repository.");
            return false;
        }

        var psi = new System.Diagnostics.ProcessStartInfo("git", "remote -v")
        {
            WorkingDirectory = path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return false;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0) return false;

            string expected = "https://github.com/patrickcortez/AurSh.git";
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                int urlStart = trimmed.IndexOf('\t');
                if (urlStart < 0) continue;
                string urlPart = trimmed.Substring(urlStart + 1).Trim();
                int spaceIdx = urlPart.IndexOf(' ');
                if (spaceIdx >= 0) urlPart = urlPart.Substring(0, spaceIdx);
                string normalized = urlPart.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                    ? urlPart.Substring(0, urlPart.Length - 4)
                    : urlPart;
                if (normalized.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("https://github.com/patrickcortez/AurSh", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch { }

        Console.Error.WriteLine("aursh: aursh-update: repository does not have the expected remote (https://github.com/patrickcortez/AurSh.git).");
        return false;
    }

    private static string RunGitOutput(string workingDir, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return "";
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            if (proc.ExitCode != 0) return "";
            return output;
        }
        catch { return ""; }
    }

    /// <summary>
    /// Run git with stdout/stderr inherited from the current process so the
    /// user sees progress / conflict messages directly. Returns the exit
    /// code (or 127 if the binary could not be launched).
    /// </summary>
    private static int RunGitForeground(string workingDir, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return 127;
            proc.WaitForExit();
            return proc.ExitCode;
        }
        catch
        {
            return 127;
        }
    }

    public static int Execute(SimpleCommandNode cmd)
    {
        string? updaterPath = FindAurshUpdateExecutable();
        if (!string.IsNullOrEmpty(updaterPath))
        {
            var psi = new System.Diagnostics.ProcessStartInfo(updaterPath)
            {
                UseShellExecute = false,
                CreateNoWindow = false,

                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in cmd.Args)
                psi.ArgumentList.Add(arg);

            try
            {
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return 127;

                var outTask = System.Threading.Tasks.Task.Run(() =>
                {
                    string? line;
                    while ((line = proc.StandardOutput.ReadLine()) != null)
                        Console.WriteLine(line);
                });
                var errTask = System.Threading.Tasks.Task.Run(() =>
                {
                    string? line;
                    while ((line = proc.StandardError.ReadLine()) != null)
                        Console.Error.WriteLine(line);
                });

                proc.WaitForExit();
                try { outTask.Wait(System.TimeSpan.FromSeconds(2)); } catch { }
                try { errTask.Wait(System.TimeSpan.FromSeconds(2)); } catch { }
                return proc.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"aursh: aursh-update: {ex.Message}");
                return 127;
            }
        }

        if (cmd.Args.Count == 0)
            return DoUpdate();

        string sub = cmd.Args[0].ToLowerInvariant();

        if (sub == "set")
        {
            if (cmd.Args.Count < 2)
            {
                Console.Error.WriteLine("aursh: aursh-update set <path-to-repo>");
                return 1;
            }
            string path = Path.GetFullPath(cmd.Args[1]);
            if (!Directory.Exists(path))
            {
                Console.Error.WriteLine($"aursh: aursh-update: directory '{path}' not found.");
                return 1;
            }
            if (!ValidateRepo(path))
                return 1;
            SetUpdateRepoPath(path);
            Console.WriteLine($"Update repository set to: {path}");
            return 0;
        }

        if (sub == "change")
        {
            if (cmd.Args.Count < 2)
            {
                Console.Error.WriteLine("aursh: aursh-update change <branch-name>");
                return 1;
            }
            string branch = cmd.Args[1].Trim();
            if (string.IsNullOrEmpty(branch))
            {
                Console.Error.WriteLine("aursh: aursh-update: branch name is empty.");
                return 1;
            }
            if (branch.IndexOfAny(new[] { ' ', '\t', ',', '\n', '\r' }) >= 0)
            {
                Console.Error.WriteLine($"aursh: aursh-update: branch name '{branch}' contains invalid whitespace or commas.");
                return 1;
            }

            string? repoPath = GetUpdateRepoPath();
            if (string.IsNullOrEmpty(repoPath))
            {
                Console.Error.WriteLine("aursh: aursh-update: no repository set. Use 'aursh-update set <path>' first.");
                return 1;
            }
            if (!Directory.Exists(repoPath))
            {
                Console.Error.WriteLine($"aursh: aursh-update: repository directory '{repoPath}' not found.");
                return 1;
            }

            RunGitOutput(repoPath, "fetch origin");

            Console.WriteLine($"Switching {repoPath} to branch '{branch}'...");
            int rc = RunGitForeground(repoPath, $"checkout {branch}");
            if (rc != 0)
            {
                Console.Error.WriteLine($"aursh: aursh-update: git checkout {branch} failed.");
                return rc;
            }

            SetUpdateBranch(branch);
            Console.WriteLine($"Now on branch '{branch}'. Stored in update_configs.txt.");
            return 0;
        }

        if (sub == "check")
        {
            string? repoPath = GetUpdateRepoPath();
            if (string.IsNullOrEmpty(repoPath))
            {
                Console.Error.WriteLine("aursh: aursh-update: no repository set. Use 'aursh-update set <path>'.");
                return 1;
            }
            if (!Directory.Exists(repoPath))
            {
                Console.Error.WriteLine($"aursh: aursh-update: repository directory '{repoPath}' not found.");
                return 1;
            }

            RunGitOutput(repoPath, "fetch");

            string branch = ResolveUpdateBranch(repoPath);
            string behind = RunGitOutput(repoPath, $"rev-list HEAD..origin/{branch} --count");

            if (int.TryParse(behind, out int count))
            {
                if (count == 0)
                    Console.WriteLine($"AurShell is up to date (origin/{branch}).");
                else
                    Console.WriteLine($"AurShell is {count} commit(s) behind origin/{branch}.");
            }
            else
            {
                Console.Error.WriteLine($"aursh: aursh-update: failed to check remote status against origin/{branch}.");
                return 1;
            }
            return 0;
        }

        Console.Error.WriteLine($"aursh: aursh-update: unknown subcommand '{sub}'");
        return 1;
    }

    private static int DoUpdate()
    {
        string? sourceDir = GetUpdateRepoPath();
        if (string.IsNullOrEmpty(sourceDir))
        {
            string currentDir = AppContext.BaseDirectory;
            string? dir = currentDir;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                {
                    sourceDir = dir;
                    break;
                }
                string? parent = Directory.GetParent(dir)?.FullName;
                if (parent == dir) break;
                dir = parent;
            }
        }

        if (string.IsNullOrEmpty(sourceDir))
        {
            Console.Error.WriteLine("aursh: aursh-update: no repository set. Use 'aursh-update set <path>'.");
            return 1;
        }

        if (!Directory.Exists(sourceDir))
        {
            Console.Error.WriteLine($"aursh: aursh-update: repository directory '{sourceDir}' not found.");
            return 1;
        }

        string branch = ResolveUpdateBranch(sourceDir);
        Console.WriteLine($"Updating AurShell from {sourceDir} (branch '{branch}')...");

        var gitPsi = new System.Diagnostics.ProcessStartInfo("git", $"pull origin {branch}")
        {
            WorkingDirectory = sourceDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var gitProc = System.Diagnostics.Process.Start(gitPsi);
            if (gitProc != null)
            {
                string gitOut = gitProc.StandardOutput.ReadToEnd();
                string gitErr = gitProc.StandardError.ReadToEnd();
                gitProc.WaitForExit();
                if (!string.IsNullOrEmpty(gitOut)) Console.WriteLine(gitOut.Trim());
                if (!string.IsNullOrEmpty(gitErr)) Console.Error.WriteLine(gitErr.Trim());
                if (gitProc.ExitCode != 0)
                {
                    Console.Error.WriteLine($"aursh: aursh-update: git pull origin {branch} failed.");
                    return 1;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-update: {ex.Message}");
            return 1;
        }

        Console.WriteLine("Installing AurShell...");
        bool useMake = File.Exists(Path.Combine(sourceDir, "Makefile"));
        var installPsi = new System.Diagnostics.ProcessStartInfo(
            useMake ? "make" : "msbuild",
            useMake ? "install" : $"\"{Path.Combine(sourceDir, "src", "AurShell.csproj")}\" /p:Configuration=Release")
        {
            WorkingDirectory = sourceDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var installProc = System.Diagnostics.Process.Start(installPsi);
            if (installProc != null)
            {
                string installOut = installProc.StandardOutput.ReadToEnd();
                string installErr = installProc.StandardError.ReadToEnd();
                installProc.WaitForExit();
                if (!string.IsNullOrEmpty(installOut)) Console.WriteLine(installOut.Trim());
                if (!string.IsNullOrEmpty(installErr)) Console.Error.WriteLine(installErr.Trim());
                if (installProc.ExitCode != 0)
                {
                    Console.Error.WriteLine("aursh: aursh-update: install failed.");
                    return 1;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-update: {ex.Message}");
            return 1;
        }

        Console.WriteLine("Update complete. Exiting shell to apply changes...");
        System.Environment.Exit(0);
        return 0;
    }

}

