using System;
using System.Linq;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshExportCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            foreach (var kv in env.Variables.OrderBy(k => k.Key))
                Console.WriteLine($"export {kv.Key}=\"{kv.Value}\"");
            return 0;
        }

        if (cmd.Args.Count >= 1 && cmd.Args[0] == "-obj")
        {
            if (cmd.Args.Count < 3)
            {
                Console.Error.WriteLine("aursh: export -obj: usage: export -obj NAME {key:value, ...}");
                return 1;
            }

            string objName = cmd.Args[1];
            string rest = string.Join(" ", cmd.Args.Skip(2));
            var obj = env.ParseObjectLiteral(rest);

            if (obj == null)
            {
                Console.Error.WriteLine($"aursh: export -obj: invalid object syntax");
                return 1;
            }

            env.SetObject(objName, obj);
            env.ExportToSystem(objName);
            return 0;
        }

        if (cmd.Args.Count >= 1 && cmd.Args[0] == "-p")
        {
            foreach (var kv in env.Variables.OrderBy(k => k.Key))
                Console.WriteLine($"declare -x {kv.Key}=\"{kv.Value}\"");
            return 0;
        }

        foreach (string arg in cmd.Args)
        {
            int eqIdx = arg.IndexOf('=');
            if (eqIdx >= 0)
            {
                string name = arg.Substring(0, eqIdx);
                string value = arg.Substring(eqIdx + 1);

                if ((value.StartsWith('"') && value.EndsWith('"')) ||
                    (value.StartsWith('\'') && value.EndsWith('\'')))
                    value = value.Substring(1, value.Length - 2);

                env.Set(name, value);
                env.ExportToSystem(name);
            }
            else
            {
                env.ExportToSystem(arg);
            }
        }

        return 0;
    }
}
