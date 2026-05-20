using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AurShell;

public class Program
{

    public static int Main(string[] args)
    {
        Utils.Platform.ApplyAndroidWorkarounds();
        Utils.Platform.EnableAnsiOnWindows();
        Utils.Platform.EnsureDirectoriesExist();
        // Start the terminal-size cache + SIGWINCH/poll change detector
        // before any rendering happens. Renderers read TerminalSize.Width /
        // .Height (via Platform.TerminalWidth) which is now backed by this.
        Utils.TerminalSize.Start();


        if (args.Length == 0)
        {
            var shell = new Core.Shell();
            shell.Run();
            return 0;
        }

        if (args[0] == "-c" && args.Length >= 2)
        {
            string command = args[1];
            var shell = new Core.Shell();
            return shell.ExecuteCommand(command);
        }

        if (args[0] == "--box" && args.Length >= 2)
        {
            string command = string.Join(" ", args.Skip(1));
            var shell = new Core.Shell();
            return shell.ExecuteCommandInBox(command);
        }

        if (args[0] == "--version" || args[0] == "-v")
        {
            Console.WriteLine("aursh 2.0.0");
            return 0;
        }

        if (args[0] == "--help" || args[0] == "-h")
        {
            PrintUsage();
            return 0;
        }

        string scriptPath = args[0];
        string[] scriptArgs = args.Length > 1 ? args.Skip(1).ToArray() : Array.Empty<string>();

        if (!File.Exists(scriptPath))
        {
            string resolved = Utils.FileSystem.ResolvePath(scriptPath, Directory.GetCurrentDirectory());
            if (!File.Exists(resolved))
            {
                Console.Error.WriteLine($"aursh: {scriptPath}: No such file or directory");
                return 127;
            }
            scriptPath = resolved;
        }

        var runner = new Core.Shell();
        return runner.RunScript(scriptPath, scriptArgs);
    }

    private static void PrintUsage()
    {


        Console.WriteLine("Usage: aursh [options] [script [args...]]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -c COMMAND    Execute COMMAND and exit");
        Console.WriteLine("  -v, --version Show version");
        Console.WriteLine("  -h, --help    Show this help");
        Console.WriteLine();
        Console.WriteLine("If no arguments are given, starts an interactive shell.");
        Console.WriteLine("If a script file is given, executes it with optional arguments.");
    }
}
