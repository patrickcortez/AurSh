using System;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshReturnCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        int code = 0;
        if (cmd.Args.Count > 0)
            int.TryParse(cmd.Args[0], out code);
        env.LastExitCode = code;
        return code;
    }
}
