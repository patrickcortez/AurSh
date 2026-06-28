using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AurShell.Core;

namespace AurShell.Grm;

public static class GrmController
{
    private static readonly GrmConfigManager Config = new GrmConfigManager();
    private static readonly GrmNetworkService Network = new GrmNetworkService();

    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        if (cmd.Args.Count == 0)
        {
            PrintHelp();
            return 1;
        }

        string subCommand = cmd.Args[0].ToLowerInvariant();
        try
        {
            switch (subCommand)
            {
                case "search": return SearchAsync(cmd, env).GetAwaiter().GetResult();
                case "install": return Install(cmd, env);
                case "run": return RunCommand(cmd, env);
                case "uninstall": return Uninstall(cmd);
                case "upgrade": return Upgrade(cmd);
                case "list": return List();
                case "goto": return Goto(cmd, env, ref workingDirectory);
                case "info": return InfoAsync(cmd, env).GetAwaiter().GetResult();
                default:
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"grm: Unexpected error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("GRM - Git Repository Manager");
        Console.WriteLine("Commands:");
        Console.WriteLine("  search <query>    Search GitHub for repositories.");
        Console.WriteLine("  install <repo>    Clone a repository (e.g., username/repo or exact name if distinct).");
        Console.WriteLine("  uninstall <repo>  Delete an installed repository.");
        Console.WriteLine("  upgrade <repo>    Pull the latest changes for an installed repository.");
        Console.WriteLine("  list              List all installed repositories.");
        Console.WriteLine("  goto <repo>       Change the current directory to the repository.");
        Console.WriteLine("  info <repo>       Get detailed information about a repository from GitHub.");
    }

    private static string? GetToken(ShellEnvironment env)
    {
        string? token = env.Get("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
            token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        return token;
    }

    private static async Task<int> SearchAsync(SimpleCommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count < 2)
        {
            Console.Error.WriteLine("grm search: missing query");
            return 1;
        }

        string query = cmd.Args[1];
        Console.WriteLine($"Searching GitHub for '{query}'...");

        var results = await Network.SearchRepositoriesAsync(query, GetToken(env));
        if (results.Count == 0)
        {
            Console.WriteLine("No repositories found.");
            return 0;
        }

        Console.WriteLine("Top results:");
        foreach (var repo in results)
        {
            Console.WriteLine($"  - {repo}");
        }
        return 0;
    }

    private static int Install(SimpleCommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count < 2)
        {
            Console.Error.WriteLine("grm install: missing repository name");
            return 1;
        }

        string repoIdentifier = cmd.Args[1];
        string branch = "";

        if (cmd.Args.Count >= 4 && cmd.Args[2] == "--branch")
        {
            branch = cmd.Args[3];
        }

        if (!repoIdentifier.Contains('/'))
        {
            Console.Error.WriteLine("grm install: repository must be in 'owner/repo' format");
            return 1;
        }

        string owner = repoIdentifier.Split('/')[0];
        string repoName = repoIdentifier.Split('/')[1];

        var installed = Config.GetInstalledRepos();
        if (installed.ContainsKey(repoIdentifier))
        {
            Console.Error.WriteLine($"grm: Repository '{repoIdentifier}' is already installed.");
            return 1;
        }

        string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string reposDir = Path.Combine(homeDir, "Repos");
        string targetPath = Path.Combine(reposDir, owner, repoName);

        Console.WriteLine($"Installing {repoIdentifier}...");

        if (!Directory.Exists(Path.GetDirectoryName(targetPath)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        }

        if (Directory.Exists(targetPath))
        {
            Console.Error.WriteLine($"grm: Target directory '{targetPath}' already exists but is not tracked by GRM.");
            return 1;
        }

        string remoteUrl = $"https://github.com/{repoIdentifier}.git";
        Console.WriteLine($"Installing {repoIdentifier} into {targetPath}...");

        string workDir = Path.GetDirectoryName(targetPath)!;

        string gitArgs = $"clone {remoteUrl} \"{repoName}\"";
        if (!string.IsNullOrWhiteSpace(branch))
        {
            gitArgs = $"clone -b {branch} {remoteUrl} \"{repoName}\"";
        }

        int rc = RunGit(workDir, gitArgs);
        if (rc == 0)
        {
            Config.AddRepo(repoIdentifier, targetPath);
            Console.WriteLine($"grm: {repoIdentifier} successfully installed.");
            ExecuteGrmSection(targetPath, repoIdentifier, "INSTALL", env);
        }
        else
        {
            Console.Error.WriteLine($"grm: Failed to clone {repoIdentifier}");
        }
        return rc;
    }

    private static int ExecuteGrmSection(string targetPath, string repoIdentifier, string targetSection, ShellEnvironment env)
    {
        string grmFile = Path.Combine(targetPath, ".grm");
        if (!File.Exists(grmFile)) return 0;

        bool isTrusted = Config.IsRepoTrusted(repoIdentifier);
        if (!isTrusted)
        {
            Console.WriteLine($"\nRepository contains a .grm script. Section: [{targetSection}]");
            Console.WriteLine("This script can execute arbitrary shell commands.");
            Console.WriteLine($"Run {targetSection} script?");
            Console.WriteLine("[y] Yes");
            Console.WriteLine("[n] No");
            Console.WriteLine("[a] Always trust this repository.");

            while (true)
            {
                Console.Write("> ");
                string? input = Console.ReadLine()?.Trim().ToLower();
                if (input == "n")
                {
                    Console.WriteLine($"Skipping {targetSection} script.");
                    return 0;
                }
                else if (input == "a")
                {
                    Config.TrustRepo(repoIdentifier);
                    break;
                }
                else if (input == "y")
                {
                    break;
                }
            }
        }

        Console.WriteLine($"Running .grm script for {repoIdentifier} (Section: [{targetSection}])...");

        string[] lines = File.ReadAllLines(grmFile);
        var declarationBlock = new System.Collections.Generic.List<string>();
        var executionBlock = new System.Collections.Generic.List<string>();

        bool foundFirstSection = false;
        bool inTargetSection = false;
        bool inExecutionBlock = false;

        foreach (var line in lines)
        {
            string tLine = line.Trim();

            if (tLine.StartsWith("[") && tLine.EndsWith("]"))
            {
                foundFirstSection = true;
                string sectionName = tLine.Substring(1, tLine.Length - 2).Trim();
                inTargetSection = string.Equals(sectionName, targetSection, StringComparison.OrdinalIgnoreCase);
                inExecutionBlock = false;
                continue;
            }

            if (!foundFirstSection)
            {
                declarationBlock.Add(line);
                continue;
            }

            if (inTargetSection)
            {
                if (tLine == "@start")
                {
                    inExecutionBlock = true;
                    continue;
                }
                if (tLine == "@end")
                {
                    break;
                }

                if (inExecutionBlock)
                {
                    executionBlock.Add(line);
                }
            }
        }

        if (!foundFirstSection)
        {
            Console.Error.WriteLine($"\ngrm: Invalid .grm format. Expected explicit section headers (e.g. [INSTALL] or [RUN]).");
            return 1;
        }

        if (executionBlock.Count == 0)
        {
            Console.Error.WriteLine($"\ngrm: Section [{targetSection}] not found or empty in .grm file.");
            return 1;
        }

        var executor = new AurShell.Core.Executor(env, targetPath);

        bool oldStopOnError = env.StopOnError;

        env.StopOnError = false;
        executor.ExecuteScript(string.Join(Environment.NewLine, declarationBlock));

        env.StopOnError = true;
        int rc = executor.ExecuteScript(string.Join(Environment.NewLine, executionBlock));
        env.StopOnError = oldStopOnError;

        if (rc != 0)
        {
            Console.Error.WriteLine($"\ngrm: {targetSection} script failed with exit code {rc}.");
        }
        else
        {
            Console.WriteLine($"\ngrm: {targetSection} script completed successfully.");
        }
        return rc;
    }

    private static int RunCommand(SimpleCommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count < 2)
        {
            Console.Error.WriteLine("grm run: missing repository name");
            return 1;
        }

        string repoIdentifier = cmd.Args[1];
        string branch = "master";

        if (cmd.Args.Count >= 4 && cmd.Args[2] == "--branch")
        {
            branch = cmd.Args[3];
        }

        var repos = Config.GetInstalledRepos();
        if (!repos.TryGetValue(repoIdentifier, out string? targetPath))
        {
            Console.Error.WriteLine($"grm: Repository '{repoIdentifier}' is not installed.");
            return 1;
        }

        if (!Directory.Exists(targetPath))
        {
            Console.Error.WriteLine($"grm: Repository directory '{targetPath}' is missing.");
            return 1;
        }

        int checkoutCode = RunGit(targetPath, $"checkout {branch}");
        if (checkoutCode != 0)
        {
            Console.Error.WriteLine($"grm: Failed to checkout branch '{branch}' in {repoIdentifier}");
            return checkoutCode;
        }

        string grmFile = Path.Combine(targetPath, ".grm");
        if (!File.Exists(grmFile))
        {
            Console.Error.WriteLine($"grm: Repository '{repoIdentifier}' does not contain a .grm file.");
            return 1;
        }

        return ExecuteGrmSection(targetPath, repoIdentifier, "RUN", env);
    }

    private static int Uninstall(SimpleCommandNode cmd)
    {
        if (cmd.Args.Count < 2)
        {
            Console.Error.WriteLine("grm uninstall: missing repository name");
            return 1;
        }

        string repoIdentifier = cmd.Args[1];
        if (!repoIdentifier.Contains('/'))
        {
            Console.Error.WriteLine("grm uninstall: repository must be in 'owner/repo' format");
            return 1;
        }

        var installed = Config.GetInstalledRepos();
        if (!installed.TryGetValue(repoIdentifier, out string? targetPath))
        {
            Console.Error.WriteLine($"grm: Repository '{repoIdentifier}' is not installed.");
            return 1;
        }

        Console.WriteLine($"Uninstalling {repoIdentifier}...");

        try
        {
            if (Directory.Exists(targetPath))
            {
                DeleteDirectory(targetPath);
            }

            Config.RemoveRepo(repoIdentifier);
            Console.WriteLine($"grm: Successfully uninstalled {repoIdentifier}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"grm: Failed to uninstall {repoIdentifier}: {ex.Message}");
            return 1;
        }
    }

    private static int Upgrade(SimpleCommandNode cmd)
    {
        if (cmd.Args.Count < 2)
        {
            var repos = Config.GetInstalledRepos();
            if (repos.Count == 0)
            {
                Console.WriteLine("grm: No repositories installed to upgrade.");
                return 0;
            }

            int finalRc = 0;
            int upgradedCount = 0;
            int upToDateCount = 0;
            var upToDateRepos = new System.Collections.Generic.List<string>();

            foreach (var kvp in repos)
            {
                int rc = UpgradeRepo(kvp.Key, kvp.Value);
                if (rc == 0) upgradedCount++;
                else if (rc == 2)
                {
                    upToDateCount++;
                    upToDateRepos.Add(kvp.Key);
                }
                else finalRc = 1;
            }

            if (upgradedCount == 0 && finalRc == 0 && upToDateCount > 0)
            {
                Console.WriteLine("\ngrm: All installed repositories are up to date.");
            }
            else if (upToDateCount > 0)
            {
                Console.WriteLine($"\ngrm: The following repositories were already up to date: {string.Join(", ", upToDateRepos)}");
            }

            return finalRc;
        }

        string repoIdentifier = cmd.Args[1];
        if (!repoIdentifier.Contains('/'))
        {
            Console.Error.WriteLine("grm upgrade: repository must be in 'owner/repo' format");
            return 1;
        }

        var installed = Config.GetInstalledRepos();
        if (!installed.TryGetValue(repoIdentifier, out string? targetPath))
        {
            Console.Error.WriteLine($"grm: Repository '{repoIdentifier}' is not installed.");
            return 1;
        }

        return UpgradeRepo(repoIdentifier, targetPath);
    }

    private static int UpgradeRepo(string repoIdentifier, string targetPath)
    {
        if (!Directory.Exists(targetPath))
        {
            Console.Error.WriteLine($"grm: Repository directory '{targetPath}' is missing. Cannot upgrade.");
            return 1;
        }

        var (statusExit, statusOutput) = RunGitOutput(targetPath, "status --porcelain");
        if (statusExit != 0)
        {
            Console.Error.WriteLine($"grm: Failed to check repository status for {repoIdentifier}");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(statusOutput))
        {
            Console.Error.WriteLine($"grm: Repository '{repoIdentifier}' has uncommitted changes. Please commit or stash them before upgrading.");
            return 1;
        }

        Console.WriteLine($"Upgrading {repoIdentifier}...");

        int fetchCode = RunGit(targetPath, "fetch --dry-run");
        if (fetchCode != 0)
        {
            Console.Error.WriteLine($"grm: Remote for {repoIdentifier} seems unreachable or vanished. Aborting upgrade.");
            return 1;
        }

        var (pullCode, pullOutput) = RunGitOutput(targetPath, "pull");
        if (pullCode == 0)
        {
            if (pullOutput.Contains("Already up to date."))
            {
                Console.WriteLine($"grm: {repoIdentifier} is already up to date.");
                return 2;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(pullOutput))
                {
                    Console.Write(pullOutput);
                }
                Console.WriteLine($"grm: Successfully upgraded {repoIdentifier}");
                return 0;
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(pullOutput))
            {
                Console.Error.Write(pullOutput);
            }
            Console.Error.WriteLine($"grm: Failed to upgrade {repoIdentifier}");
            return pullCode == 0 ? 1 : pullCode;
        }
    }

    private static int List()
    {
        var installed = Config.GetInstalledRepos();
        if (installed.Count == 0)
        {
            Console.WriteLine("No repositories installed via GRM.");
            return 0;
        }

        Console.WriteLine("Installed Repositories:");
        foreach (var kvp in installed)
        {
            Console.WriteLine($"  {kvp.Key} => {kvp.Value}");
        }
        return 0;
    }

    private static int Goto(SimpleCommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        if (cmd.Args.Count < 2)
        {
            Console.Error.WriteLine("grm goto: missing repository name");
            return 1;
        }

        string repoIdentifier = cmd.Args[1];
        if (!repoIdentifier.Contains('/'))
        {
            Console.Error.WriteLine("grm goto: repository must be in 'owner/repo' format");
            return 1;
        }

        var installed = Config.GetInstalledRepos();
        if (!installed.TryGetValue(repoIdentifier, out string? targetPath))
        {
            Console.Error.WriteLine($"grm: Repository '{repoIdentifier}' is not installed.");
            return 1;
        }

        if (!Directory.Exists(targetPath))
        {
            Console.Error.WriteLine($"grm: Repository directory '{targetPath}' is missing.");
            return 1;
        }

        string oldDir = workingDirectory;
        workingDirectory = targetPath;
        try
        {
            Environment.CurrentDirectory = targetPath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"grm: Failed to change directory: {ex.Message}");
            return 1;
        }

        env.Set("OLDPWD", oldDir);
        env.Set("PWD", workingDirectory);

        return 0;
    }

    private static async Task<int> InfoAsync(SimpleCommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count < 2)
        {
            Console.Error.WriteLine("grm info: missing repository name");
            return 1;
        }

        string repoIdentifier = cmd.Args[1];
        if (!repoIdentifier.Contains('/'))
        {
            Console.Error.WriteLine("grm info: repository must be in 'owner/repo' format");
            return 1;
        }

        string? info = await Network.GetRepoInfoAsync(repoIdentifier, GetToken(env));
        if (info == null)
        {
            return 1;
        }

        Console.WriteLine(info);

        string? readme = await Network.GetRepoReadmeAsync(repoIdentifier, GetToken(env));
        if (!string.IsNullOrWhiteSpace(readme))
        {
            Console.WriteLine("\n--- README ---");
            Console.WriteLine(readme);
        }

        return 0;
    }

    private static int RunGit(string workingDir, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false
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
            Console.Error.WriteLine($"grm: Failed to execute git: {ex.Message}");
            return 127;
        }
    }

    private static (int exitCode, string output) RunGitOutput(string workingDir, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return (127, string.Empty);
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return (proc.ExitCode, output);
        }
        catch
        {
            return (127, string.Empty);
        }
    }

    private static void DeleteDirectory(string target_dir)
    {
        string[] files = Directory.GetFiles(target_dir);
        string[] dirs = Directory.GetDirectories(target_dir);

        foreach (string file in files)
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        foreach (string dir in dirs)
        {
            DeleteDirectory(dir);
        }

        Directory.Delete(target_dir, false);
    }
}

