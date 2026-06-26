using System;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshTypeCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env, string workingDirectory)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: type: not enough arguments");
            return 1;
        }

        int result = 0;

        foreach (string name in cmd.Args)
        {
            if (Core.BuiltinCommands.IsBuiltin(name))
            {
                Core.BuiltinCommands.WriteOut($"{name} is a shell builtin");
            }
            else if (env.GetAlias(name) != null)
            {
                Core.BuiltinCommands.WriteOut($"{name} is aliased to '{env.GetAlias(name)}'");
            }
            else
            {
                string? path = Pipeline.ResolveCommand(name, workingDirectory);
                if (path != null)
                {
                    Core.BuiltinCommands.WriteOut($"{name} is {path}");
                }
                else
                {
                    Console.Error.WriteLine($"aursh: type: {name}: not found");
                    result = 1;
                }
            }
        }

        return result;
    }
}
