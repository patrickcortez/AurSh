using System;
using System.Linq;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshAliasCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            foreach (var kv in env.Aliases.OrderBy(k => k.Key))
                Console.WriteLine($"alias {kv.Key}='{kv.Value}'");
            return 0;
        }

        foreach (string arg in cmd.Args)
        {
            int eqIdx = arg.IndexOf('=');
            if (eqIdx >= 0)
            {
                string name = arg.Substring(0, eqIdx);
                string value = arg.Substring(eqIdx + 1);

                if ((value.StartsWith('\'') && value.EndsWith('\'')) ||
                    (value.StartsWith('"') && value.EndsWith('"')))
                    value = value.Substring(1, value.Length - 2);

                env.SetAlias(name, value);
            }
            else
            {
                string? alias = env.GetAlias(arg);
                if (alias != null)
                    Console.WriteLine($"alias {arg}='{alias}'");
                else
                {
                    Console.Error.WriteLine($"aursh: alias: {arg}: not found");
                    return 1;
                }
            }
        }

        return 0;
    }
}
