using System;
using System.Collections.Generic;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshDeclareCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        bool isIndexed = false;
        bool isAssoc = false;
        bool isReadonly = false;
        int i = 0;
        while (i < cmd.Args.Count && cmd.Args[i].StartsWith("-"))
        {
            string flag = cmd.Args[i];
            if (flag.Contains("a")) isIndexed = true;
            if (flag.Contains("A")) isAssoc = true;
            if (flag.Contains("r")) isReadonly = true;
            i++;
        }

        for (; i < cmd.Args.Count; i++)
        {
            string arg = cmd.Args[i];
            int eq = arg.IndexOf('=');
            string name = eq > 0 ? arg.Substring(0, eq).Trim() : arg;
            string val = eq > 0 ? arg.Substring(eq + 1).Trim() : "";

            if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
                val = val.Substring(1, val.Length - 2);

            if (isAssoc)
            {
                if (env.GetAssocArray(name) == null)
                    env.SetAssocArray(name, new Dictionary<string, string>(StringComparer.Ordinal));
            }
            else if (isIndexed)
            {
                if (env.GetArray(name) == null)
                    env.SetArray(name, new List<string>());
            }

            if (eq > 0)
            {
                env.Set(name, env.Expand(val));
            }

            if (isReadonly)
            {
                env.MarkReadonly(name);
            }
        }
        return 0;
    }
}
