using System;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshUnsetCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: unset: not enough arguments");
            return 1;
        }

        foreach (string name in cmd.Args)
        {
            env.Unset(name);
            System.Environment.SetEnvironmentVariable(name, null);
        }

        return 0;
    }
}
