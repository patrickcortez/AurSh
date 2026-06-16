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

    public static int Execute(CommandNode cmd, ShellEnvironment env, ref string workingDirectory)
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
                case "install": return Install(cmd);
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
        Console.WriteLine("GRM - Git Package Manager");
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

    private static async Task<int> SearchAsync(CommandNode cmd, ShellEnvironment env)
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

    private static int Install(CommandNode cmd)
    {
        if (cmd.Args.Count < 2)
        {
            Console.Error.WriteLine("grm install: missing repository name");
            return 1;
        }

        string repoIdentifier = cmd.Args[1];
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
        int rc = RunGit(workDir, $"clone {remoteUrl} \"{repoName}\"");
        if (rc == 0)
        {
            Config.AddRepo(repoIdentifier, targetPath);
            Console.WriteLine($"grm: Successfully installed {repoIdentifier}");
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"grm: Failed to install {repoIdentifier}");
            return rc;
        }
    }

    private static int Uninstall(CommandNode cmd)
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

    private static int Upgrade(CommandNode cmd)
    {
        if (cmd.Args.Count < 2)
        {
            Console.Error.WriteLine("grm upgrade: missing repository name");
            return 1;
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

        int pullCode = RunGit(targetPath, "pull");
        if (pullCode == 0)
        {
            Console.WriteLine($"grm: Successfully upgraded {repoIdentifier}");
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"grm: Failed to upgrade {repoIdentifier}");
            return pullCode;
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

    private static int Goto(CommandNode cmd, ShellEnvironment env, ref string workingDirectory)
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

    private static async Task<int> InfoAsync(CommandNode cmd, ShellEnvironment env)
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
