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
            Console.Error.WriteLine($"aursh: cd: {cmd.Args.FirstOrDefault() ?? target}: No such file or directory");
            return 1;
        }

        string oldDir = workingDirectory;
        workingDirectory = Utils.FileSystem.NormalizePath(target);

        try
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
        catch { }

        env.Set("OLDPWD", oldDir);
        env.Set("PWD", workingDirectory);

        return 0;
    }
}
