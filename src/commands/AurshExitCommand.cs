using System;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshExitCommand
{
    public static int Execute(SimpleCommandNode cmd)
    {
        int code = 0;
        if (cmd.Args.Count > 0)
            int.TryParse(cmd.Args[0], out code);

        System.Environment.Exit(code);
        return code;
    }
}
