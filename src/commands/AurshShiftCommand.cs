using System;
using AurShell.Core;

namespace AurShell.Commands;

public static class AurshShiftCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        int count = 1;

        if (cmd.Args.Count > 0)
        {
            if (int.TryParse(cmd.Args[0], out int parsedCount))
            {
                if (parsedCount < 0)
                {
                    Console.Error.WriteLine($"aursh: shift: {parsedCount}: shift count out of range");
                    return 1;
                }
                count = parsedCount;
            }
            else
            {
                Console.Error.WriteLine($"aursh: shift: {cmd.Args[0]}: numeric argument required");
                return 1;
            }
        }

        if (count > env.PositionalArguments.Count)
        {
            // If shifting more than we have, shift them all
            count = env.PositionalArguments.Count;
        }

        env.ShiftPositionalArguments(count);
        return 0;
    }
}
