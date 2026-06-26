using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshSourceCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: source: filename argument required");
            return 1;
        }

        string filePath = Utils.FileSystem.ResolvePath(cmd.Args[0], workingDirectory);

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"aursh: source: {cmd.Args[0]}: No such file or directory");
            return 1;
        }

        var oldPositional = new List<string>(env.PositionalArguments);
        string[] scriptArgs = cmd.Args.Count > 1 ? cmd.Args.Skip(1).ToArray() : Array.Empty<string>();
        try
        {
            if (scriptArgs.Length > 0)
            {
                env.PositionalArguments.Clear();
                env.PositionalArguments.AddRange(scriptArgs);
            }

            string content = File.ReadAllText(filePath);
            var executor = new Executor(env, workingDirectory);
            int result = executor.ExecuteScript(content);
            workingDirectory = executor.WorkingDirectory;
            return result;
        }
        finally
        {
            if (scriptArgs.Length > 0)
            {
                env.PositionalArguments.Clear();
                env.PositionalArguments.AddRange(oldPositional);
            }
        }
    }
}
