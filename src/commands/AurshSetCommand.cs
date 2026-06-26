using System;
using System.Linq;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshSetCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            foreach (var kv in env.Variables.OrderBy(k => k.Key))
                Console.WriteLine($"{kv.Key}={kv.Value}");
            return 0;
        }

        if (cmd.Args[0] == "--")
        {
            env.PositionalArguments.Clear();
            env.PositionalArguments.AddRange(cmd.Args.Skip(1));
            return 0;
        }

        for (int i = 0; i < cmd.Args.Count; i++)
        {
            string arg = cmd.Args[i];
            int eqIdx = arg.IndexOf('=');
            if (eqIdx >= 0)
            {
                string name = arg.Substring(0, eqIdx);
                string value = arg.Substring(eqIdx + 1);
                env.Set(name, value);
            }
        }

        return 0;
    }
}
