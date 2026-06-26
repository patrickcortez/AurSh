using System;
using System.Linq;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshUnaliasCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: unalias: not enough arguments");
            return 1;
        }

        if (cmd.Args[0] == "-a")
        {
            foreach (string key in env.Aliases.Keys.ToList())
                env.UnsetAlias(key);
            return 0;
        }

        foreach (string name in cmd.Args)
        {
            if (!env.UnsetAlias(name))
            {
                Console.Error.WriteLine($"aursh: unalias: {name}: not found");
                return 1;
            }
        }

        return 0;
    }
}
