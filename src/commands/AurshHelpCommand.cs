using System;
using System.Linq;

namespace AurShell.Commands;

public static class AurshHelpCommand
{
    public static int Execute()
    {
        Console.WriteLine($"AurShell Help");
        Console.WriteLine($"Version {Core.BuiltinCommands.Version}");
        Console.WriteLine();
        Console.WriteLine("These shell commands are defined internally. Type `help` to see this list.");
        Console.WriteLine("External commands are resolved from PATH. Core UNIX utilities are provided by the bundled BusyBox toolkit.");
        Console.WriteLine();

        var sortedBuiltins = Core.BuiltinCommands.Builtins.OrderBy(b => b).ToList();

        Console.WriteLine("Built-in Commands:");
        int cols = 4;
        int rowCount = (int)Math.Ceiling((double)sortedBuiltins.Count / cols);

        for (int row = 0; row < rowCount; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int index = col * rowCount + row;
                if (index < sortedBuiltins.Count)
                {
                    Console.Write(sortedBuiltins[index].PadRight(18));
                }
            }
            Console.WriteLine();
        }

        return 0;
    }
}
