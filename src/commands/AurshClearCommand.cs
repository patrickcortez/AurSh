using System;

namespace AurShell.Commands;

public static class AurshClearCommand
{
    public static int Execute()
    {
        Console.Write(Utils.Ansi.CursorHome + Utils.Ansi.ClearScreen + Utils.Ansi.ClearScrollback);
        Console.Out.Flush();
        return 0;
    }
}
