using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AurShell.Core;

namespace AurShell.Gpm;

public static class GpmController
{
    private static readonly GpmConfigManager Config = new GpmConfigManager();
    private static readonly GpmNetworkService Network = new GpmNetworkService();

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
            Console.Error.WriteLine($"gpm: Unexpected error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("GPM - Git Package Manager");
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
            Console.Error.WriteLine("gpm search: missing query");
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
            Console.Error.WriteLine("gpm install: missing repository name");
            return 1;
        }

        string repoIdentifier = cmd.Args[1];
        if (!repoIdentifier.Contains('/'))
        {
            Console.Error.WriteLine("gpm install: repository must be in 'owner/repo' format");
            return 1;
        }
        
        string repoName = repoIdentifier.Split('/')[1];
        
        var installed = Config.GetInstalledRepos();
        if (installed.ContainsKey(repoName) || installed.ContainsKey(repoIdentifier))
        {
            Console.WriteLine($"gpm: Repository '{repoIdentifier}' already exists locally.");
            return 0;
        }

        string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string reposDir = Path.Combine(homeDir, "Repos");
        
        if (!Directory.Exists(reposDir))
        {
            Directory.CreateDirectory(reposDir);
        }

        string targetPath = Path.Combine(reposDir, repoName);
        if (Directory.Exists(targetPath))
        {
            Console.Error.WriteLine($"gpm: Target directory '{targetPath}' already exists but is not tracked by GPM.");
            return 1;
        }

        string remoteUrl = $"https://github.com/{repoIdentifier}.git";
        Console.WriteLine($"Installing {repoIdentifier} into {targetPath}...");

        int rc = RunGit(reposDir, $"clone {remoteUrl} {repoName}");
        if (rc == 0)
        {
            Config.AddRepo(repoIdentifier, targetPath);
            Console.WriteLine($"gpm: Successfully installed {repoIdentifier}");
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"gpm: Failed to install {repoIdentifier}");
            return rc;
        }
    }

    private static int Uninstall(CommandNode cmd)
    {
        if (cmd.Args.Count < 2)
        {
            Console.Error.WriteLine("gpm uninstall: missing repository name");
            return 1;
        }

        string repoIdentifier = cmd.Args[1];
        if (!repoIdentifier.Contains('/'))
        {
            Console.Error.WriteLine("gpm uninstall: repository must be in 'owner/repo' format");
            return 1;
        }

        var installed = Config.GetInstalledRepos();
        if (!installed.TryGetValue(repoIdentifier, out string? targetPath))
        {
            Console.Error.WriteLine($"gpm: Repository '{repoIdentifier}' is not installed.");
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
            Console.WriteLine($"gpm: Successfully uninstalled {repoIdentifier}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"gpm: Failed to uninstall {repoIdentifier}: {ex.Message}");
            return 1;
        }
    }

    private static int Upgrade(CommandNode cmd)
    {
        if (cmd.Args.Count < 2)
        {
            Console.Error.WriteLine("gpm upgrade: missing repository name");
            return 1;
        }

        string repoIdentifier = cmd.Args[1];
        if (!repoIdentifier.Contains('/'))
        {
            Console.Error.WriteLine("gpm upgrade: repository must be in 'owner/repo' format");
            return 1;
        }

        var installed = Config.GetInstalledRepos();
        if (!installed.TryGetValue(repoIdentifier, out string? targetPath))
        {
            Console.Error.WriteLine($"gpm: Repository '{repoIdentifier}' is not installed.");
            return 1;
        }

        if (!Directory.Exists(targetPath))
        {
            Console.Error.WriteLine($"gpm: Repository directory '{targetPath}' is missing. Cannot upgrade.");
            return 1;
        }

        Console.WriteLine($"Upgrading {repoIdentifier}...");
        
        int fetchCode = RunGit(targetPath, "fetch --dry-run");
        if (fetchCode != 0)
        {
            Console.Error.WriteLine($"gpm: Remote for {repoIdentifier} seems unreachable or vanished. Aborting upgrade.");
            return 1;
        }

        int pullCode = RunGit(targetPath, "pull");
        if (pullCode == 0)
        {
            Console.WriteLine($"gpm: Successfully upgraded {repoIdentifier}");
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"gpm: Failed to upgrade {repoIdentifier}");
            return pullCode;
        }
    }

    private static int List()
    {
        var installed = Config.GetInstalledRepos();
        if (installed.Count == 0)
        {
            Console.WriteLine("No repositories installed via GPM.");
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
            Console.Error.WriteLine("gpm goto: missing repository name");
            return 1;
        }

        string repoIdentifier = cmd.Args[1];
        if (!repoIdentifier.Contains('/'))
        {
            Console.Error.WriteLine("gpm goto: repository must be in 'owner/repo' format");
            return 1;
        }

        var installed = Config.GetInstalledRepos();
        if (!installed.TryGetValue(repoIdentifier, out string? targetPath))
        {
            Console.Error.WriteLine($"gpm: Repository '{repoIdentifier}' is not installed.");
            return 1;
        }

        if (!Directory.Exists(targetPath))
        {
            Console.Error.WriteLine($"gpm: Repository directory '{targetPath}' is missing.");
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
            Console.Error.WriteLine($"gpm: Failed to change directory: {ex.Message}");
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
            Console.Error.WriteLine("gpm info: missing repository name");
            return 1;
        }

        string repoIdentifier = cmd.Args[1];
        if (!repoIdentifier.Contains('/'))
        {
            Console.Error.WriteLine("gpm info: repository must be in 'owner/repo' format");
            return 1;
        }

        string? info = await Network.GetRepoInfoAsync(repoIdentifier, GetToken(env));
        if (info == null)
        {
            return 1;
        }

        Console.WriteLine(info);
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
            Console.Error.WriteLine($"gpm: Failed to execute git: {ex.Message}");
            return 127;
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
