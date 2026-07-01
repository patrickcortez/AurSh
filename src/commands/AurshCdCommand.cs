using System;
using System.IO;
using System.Linq;
using AurShell.Core;
using AurShell.Utils;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshCdCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        string target;

        if (cmd.Args.Count == 0)
        {
            target = Utils.Platform.HomeDirectory;
        }
        else if (cmd.Args[0] == "-")
        {
            string? oldPwd = env.Get("OLDPWD");
            if (string.IsNullOrEmpty(oldPwd))
            {
                Console.Error.WriteLine("aursh: cd: OLDPWD not set");
                return 1;
            }
            target = oldPwd;
            Console.WriteLine(target);
        }
        else
        {
            target = Utils.FileSystem.ResolvePath(cmd.Args[0], workingDirectory);
        }

        if (!Directory.Exists(target))
        {
            // Fallback: if the target starts with "." (dotfile/dotdir convention) and
            // doesn't exist relative to CWD, try resolving from $HOME.
            // This handles the common case of `cd .aursh` from any directory.
            string arg = cmd.Args.FirstOrDefault() ?? "";
            if (arg.StartsWith(".") && !arg.StartsWith("..") && !arg.StartsWith("./") && !arg.StartsWith(".\\"))
            {
                string homeTarget = Utils.FileSystem.ResolvePath(arg, Utils.Platform.HomeDirectory);
                if (Directory.Exists(homeTarget))
                {
                    target = homeTarget;
                }
            }

            // Also check CDPATH (POSIX convention)
            if (!Directory.Exists(target))
            {
                string? cdpath = env.Get("CDPATH");
                if (!string.IsNullOrEmpty(cdpath))
                {
                    foreach (string dir in cdpath.Split(Path.PathSeparator))
                    {
                        if (string.IsNullOrWhiteSpace(dir)) continue;
                        string candidate = Utils.FileSystem.ResolvePath(arg, dir.Trim());
                        if (Directory.Exists(candidate))
                        {
                            target = candidate;
                            break;
                        }
                    }
                }
            }

            if (!Directory.Exists(target))
            {
                Console.Error.WriteLine($"aursh: cd: {arg}: No such file or directory");
                return 1;
            }
        }

        string oldDir = workingDirectory;
        workingDirectory = Utils.FileSystem.NormalizePath(target);

        try
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: cd: failed to set working directory: {ex.Message}");
        }

        env.Set("OLDPWD", oldDir);
        env.Set("PWD", workingDirectory);

        return 0;
    }
}
