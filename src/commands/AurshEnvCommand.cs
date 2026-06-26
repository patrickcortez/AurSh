using System;
using System.Linq;
using AurShell.Core;

namespace AurShell.Commands;

public static class AurshEnvCommand
{
    public static int Execute(ShellEnvironment env)
    {
        foreach (var kv in env.Variables.OrderBy(k => k.Key))
            Console.WriteLine($"{kv.Key}={kv.Value}");
        return 0;
    }
}
