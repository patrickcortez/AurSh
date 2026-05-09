using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using AurShell.Utils;

namespace AurShell.Core;

public static class BuiltinCommands
{
    private static readonly HashSet<string> Builtins = new(StringComparer.OrdinalIgnoreCase)
    {
        "cd", "export", "unset", "exit", "history", "clear", "echo",
        "pwd", "type", "alias", "unalias", "source", "set", "env",
        "true", "false", "shift", "read", "test", "return",
        "jobs", "fg", "kill", "aursh-plugin", "aursh-assoc", "aursh-reload", "aursh-history","aursh-about","aursh-ls"
    };

    public static bool IsBuiltin(string name) => Builtins.Contains(name);

    public static int Execute(CommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        return cmd.Name.ToLowerInvariant() switch
        {
            "cd" => ExecuteCd(cmd, env, ref workingDirectory),
            "export" => ExecuteExport(cmd, env),
            "unset" => ExecuteUnset(cmd, env),
            "exit" => ExecuteExit(cmd),
            "history" or "aursh-history" => ExecuteHistory(cmd, env, workingDirectory),
            "clear" => ExecuteClear(),
            "echo" => ExecuteEcho(cmd),
            "pwd" => ExecutePwd(workingDirectory),
            "type" => ExecuteType(cmd, env, workingDirectory),
            "alias" => ExecuteAlias(cmd, env),
            "unalias" => ExecuteUnalias(cmd, env),
            "source" => ExecuteSource(cmd, env, ref workingDirectory),
            "set" => ExecuteSet(cmd, env),
            "env" => ExecuteEnv(env),
            "true" => 0,
            "false" => 1,
            "read" => ExecuteRead(cmd, env),
            "test" => ExecuteTest(cmd),
            "return" => ExecuteReturn(cmd, env),
            "jobs" => ExecuteJobs(cmd, env),
            "fg" => ExecuteFg(cmd, env),
            "kill" => ExecuteKill(cmd, env),
            "aursh-plugin" => ExecuteAurshPlugin(cmd, env, workingDirectory),
            "aursh-assoc" => ExecuteAssoc(cmd, env),
            "aursh-reload" => ExecuteReload(env),
            "aursh-about" => ExecuteAbout(cmd),
            "aursh-ls" => ExecuteLs(cmd,ref workingDirectory),
            _ => ExecuteFallback(cmd)
        };
    }

    private static string GetPlatform()
    {
        string os = string.Empty;

        if (OperatingSystem.IsWindows())
        {
            os = "Windows";
        }else if (OperatingSystem.IsLinux())
        {
            os = "Linux";
        }else if (OperatingSystem.IsAndroid())
        {
            os = "Android";
        }else if (OperatingSystem.IsMacOS())
        {
            os = "MacOS";
        }
        else
        {
            os = "Unknown";
        }

        return os;
    }

    private static Architecture GetArch()
    {
        return RuntimeInformation.OSArchitecture;
    }

    private static int ExecuteAbout(CommandNode cmd)
    {

        string about = $@"
        {Ansi.FgBrightCyan}-------------------------------------------------------------------------------------------------------
        
        {Ansi.FgBrightBlue}                 About:
                            - This Shell is developed in C# by {Ansi.FgBrightCyan}Tezzz{Ansi.FgBrightBlue}.
                            As a cross platform shell with a purpose to make the command-line
                            look aesthetically pleasing while working. This Shell is under the license of
                            {Ansi.FgBrightCyan}GNU General Public License.

                            {Ansi.FgBrightBlue}Current Platform: {Ansi.FgBrightMagenta}{GetPlatform()}
                            {Ansi.FgBrightBlue}Current Architecture: {Ansi.FgBrightMagenta}{GetArch().ToString()}

       {Ansi.FgBrightCyan} -------------------------------------------------------------------------------------------------------
        ";

        Console.WriteLine(about);

        return 0;
    }

    private static int ExecuteCd(CommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        string target;

        if (cmd.Args.Count == 0)
        {
            target = Utils.Platform.HomeDirectory;
        }
        else if (cmd.Args[0] == "-")
        {
            string? oldPwd = env.Get("OLDPWD");
            if (string.IsNullOrEmpty(oldPwd))
            {
                Console.Error.WriteLine("aursh: cd: OLDPWD not set");
                return 1;
            }
            target = oldPwd;
            Console.WriteLine(target);
        }
        else
        {
            target = Utils.FileSystem.ResolvePath(cmd.Args[0], workingDirectory);
        }

        if (!Directory.Exists(target))
        {
            Console.Error.WriteLine($"aursh: cd: {cmd.Args.FirstOrDefault() ?? target}: No such file or directory");
            return 1;
        }

        string oldDir = workingDirectory;
        workingDirectory = Utils.FileSystem.NormalizePath(target);

        try
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
        catch { }

        env.Set("OLDPWD", oldDir);
        env.Set("PWD", workingDirectory);

        return 0;
    }

private static int ExecuteLs(CommandNode cmd, ref string workingDirectory)
{
    string targetPath = workingDirectory;
    
    if (cmd.Args.Count >= 1)
    {
        if (Path.IsPathRooted(cmd.Args[0]))
            targetPath = cmd.Args[0];
        else
            targetPath = Path.Combine(workingDirectory, cmd.Args[0]);
    }

    if (!Directory.Exists(targetPath))
    {
        Console.Error.WriteLine($"aursh: ls: cannot access '{targetPath}': No such file or directory");
        return 1;
    }

    var entries = Directory.EnumerateFileSystemEntries(targetPath).ToList();
    
    if (entries.Count == 0)
    {
        return 0;
    }

    // Calculate the maximum filename length
    var fileNames = entries.Select(Path.GetFileName).ToList();
    int maxWidth = fileNames.Max(n => n.Length);
    int columnGap = 4; // spaces between columns
    int columnWidth = maxWidth + columnGap;
    
    int numColumns;
    
    if (entries.Count > 16)
    {
        numColumns = 2;
    }
    else
    {
        // Calculate how many columns fit in terminal
        int terminalWidth = Console.WindowWidth;
        numColumns = Math.Max(1, terminalWidth / columnWidth);
    }
    
    int numRows = (int)Math.Ceiling((double)entries.Count / numColumns);

    // Print in columns (top to bottom, then left to right)
    for (int row = 0; row < numRows; row++)
    {
        for (int col = 0; col < numColumns; col++)
        {
            int index = col * numRows + row;
            
            if (index >= entries.Count)
                break;

            string fileName = fileNames[index];
            FileAttributes attr = File.GetAttributes(entries[index]);
            bool isDir = attr.HasFlag(FileAttributes.Directory);
            bool isExe = Path.GetExtension(fileName).Equals(".exe", StringComparison.OrdinalIgnoreCase);
            bool isAur = Path.GetExtension(fileName).Equals(".aur",StringComparison.OrdinalIgnoreCase);

            // Set color
            if (isDir)
                Console.Write(Ansi.FgBrightBlue);
            else if (isExe)
                Console.Write(Ansi.FgBrightGreen);
            else if (isAur)
                Console.Write(Ansi.FgBrightYellow);
            else
                Console.Write(Ansi.FgBrightWhite);

            // Print filename padded to column width
            Console.Write(fileName.PadRight(columnWidth));
        }
        Console.WriteLine();
    }

    Console.Write(Ansi.Reset);
    return 0;
}

    private static int ExecuteExport(CommandNode cmd, ShellEnvironment env)
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

    private static int ExecuteUnset(CommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: unset: not enough arguments");
            return 1;
        }

        foreach (string name in cmd.Args)
        {
            env.Unset(name);
            System.Environment.SetEnvironmentVariable(name, null);
        }

        return 0;
    }

    private static int ExecuteExit(CommandNode cmd)
    {
        int code = 0;
        if (cmd.Args.Count > 0)
            int.TryParse(cmd.Args[0], out code);

        System.Environment.Exit(code);
        return code;
    }

    private static int ExecuteHistory(CommandNode cmd, ShellEnvironment env, string workingDirectory)
    {
        string historyFile = Utils.Platform.HistoryFilePath;
        var history = new History(historyFile);

        string firstArg = cmd.Args.Count > 0 ? cmd.Args[0].ToLowerInvariant() : "";

        if (firstArg == "clear" || firstArg == "-c")
        {
            history.Clear();
            Console.WriteLine("History cleared.");
            return 0;
        }

        string filter = "";
        bool showTui = false;

        if (firstArg == "show")
        {
            showTui = true;
        }
        else if (firstArg.StartsWith("filter="))
        {
            showTui = true;
            filter = cmd.Args[0].Substring("filter=".Length);
        }

        if (showTui)
        {
            string? selected = RunHistoryTui(history, filter);
            if (!string.IsNullOrEmpty(selected))
            {
                Console.WriteLine(Utils.Ansi.FgBrightGreen + "> " + selected + Utils.Ansi.Reset);
                var executor = new Executor(env, workingDirectory);
                return executor.Execute(selected);
            }
            return 0;
        }

        string[] lines = Utils.FileSystem.ReadAllLinesSafe(historyFile);
        int start = 0;
        if (cmd.Args.Count > 0 && int.TryParse(cmd.Args[0], out int count))
            start = Math.Max(0, lines.Length - count);

        for (int i = start; i < lines.Length; i++)
            Console.WriteLine($"  {i + 1}  {lines[i]}");

        return 0;
    }

    private static string? RunHistoryTui(History history, string initialFilter)
    {
        string filter = initialFilter;
        int selectedIndex = 0;
        int scrollOffset = 0;
        bool running = true;
        string? result = null;

        var entries = history.Entries.Select((line, index) => new { Line = line, OriginalIndex = index + 1 }).Reverse().ToList();
        var filtered = string.IsNullOrEmpty(filter) ? entries : entries.Where(e => e.Line.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        // Switch to alternate buffer and hide cursor
        Console.Write("\x1b[?1049h\x1b[?25l");

        try
        {
            while (running)
            {
                int height = Utils.Platform.TerminalHeight - 4; // header, divider, filter bar, padding
                int width = Utils.Platform.TerminalWidth;

                if (selectedIndex < scrollOffset) scrollOffset = selectedIndex;
                if (selectedIndex >= scrollOffset + height) scrollOffset = selectedIndex - height + 1;

                Console.Write(Utils.Ansi.SetCursorPosition(1, 1));
                
                Console.Write(Utils.Ansi.ClearLine);
                Console.WriteLine("\n  AurSh History");
                
                Console.Write(Utils.Ansi.ClearLine);
                Console.WriteLine(Utils.Ansi.FgBrightBlack + new string('\u2500', width) + Utils.Ansi.Reset);

                int visibleCount = Math.Min(filtered.Count - scrollOffset, height);
                for (int i = 0; i < height; i++)
                {
                    Console.Write(Utils.Ansi.ClearLine);
                    int itemIdx = scrollOffset + i;
                    if (itemIdx < filtered.Count)
                    {
                        bool isSelected = itemIdx == selectedIndex;
                        var item = filtered[itemIdx];
                        
                        string numStr = (itemIdx + 1).ToString().PadLeft(3);
                        string origNumStr = item.OriginalIndex.ToString().PadLeft(3);
                        
                        string prefix = $" {numStr} \u2502 {origNumStr} ";
                        string lineText = item.Line;
                        
                        int maxLineLen = width - prefix.Length - 2;
                        if (maxLineLen > 0 && lineText.Length > maxLineLen)
                            lineText = lineText.Substring(0, maxLineLen - 1) + "\u2026";
                        
                        if (isSelected)
                        {
                            Console.Write(Utils.Ansi.BgRgb(50, 50, 70) + Utils.Ansi.FgWhite + prefix + lineText.PadRight(maxLineLen + 2) + Utils.Ansi.Reset);
                        }
                        else
                        {
                            Console.Write(Utils.Ansi.FgBrightBlack + $" {numStr} \u2502 " + Utils.Ansi.Reset + origNumStr + " " + Utils.Ansi.FgRgb(200, 200, 200) + lineText);
                        }
                    }
                    Console.WriteLine();
                }

                Console.Write(Utils.Ansi.ClearLine);
                Console.Write(Utils.Ansi.FgRgb(150, 150, 150) + " Filter: " + Utils.Ansi.FgWhite + filter.PadRight(width - 9) + Utils.Ansi.Reset);

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape || (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0))
                {
                    running = false;
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    if (filtered.Count > 0)
                        result = filtered[selectedIndex].Line;
                    running = false;
                }
                else if (key.Key == ConsoleKey.UpArrow)
                {
                    selectedIndex = Math.Max(0, selectedIndex - 1);
                }
                else if (key.Key == ConsoleKey.DownArrow)
                {
                    selectedIndex = Math.Min(filtered.Count - 1, selectedIndex + 1);
                }
                else if (key.Key == ConsoleKey.PageUp)
                {
                    selectedIndex = Math.Max(0, selectedIndex - height);
                }
                else if (key.Key == ConsoleKey.PageDown)
                {
                    selectedIndex = Math.Min(filtered.Count - 1, selectedIndex + height);
                }
                else if (key.Key == ConsoleKey.Backspace)
                {
                    if (filter.Length > 0)
                    {
                        filter = filter.Substring(0, filter.Length - 1);
                        filtered = string.IsNullOrEmpty(filter) ? entries : entries.Where(e => e.Line.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                        selectedIndex = 0;
                        scrollOffset = 0;
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    filter += key.KeyChar;
                    filtered = string.IsNullOrEmpty(filter) ? entries : entries.Where(e => e.Line.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                    selectedIndex = 0;
                    scrollOffset = 0;
                }
            }
        }
        finally
        {
            // Show cursor and restore main buffer
            Console.Write("\x1b[?25h\x1b[?1049l");
        }

        return result;
    }


    private static int ExecuteClear()
    {
        Console.Write(Utils.Ansi.ClearScreen);
        Console.Write(Utils.Ansi.SetCursorPosition(1, 1));
        return 0;
    }

    private static int ExecuteEcho(CommandNode cmd)
    {
        bool noNewline = false;
        bool interpretEscapes = false;
        int startIdx = 0;

        while (startIdx < cmd.Args.Count)
        {
            string arg = cmd.Args[startIdx];
            if (arg == "-n")
            {
                noNewline = true;
                startIdx++;
            }
            else if (arg == "-e")
            {
                interpretEscapes = true;
                startIdx++;
            }
            else if (arg == "-E")
            {
                interpretEscapes = false;
                startIdx++;
            }
            else
            {
                break;
            }
        }

        string output = string.Join(" ", cmd.Args.Skip(startIdx));

        if (interpretEscapes)
            output = InterpretEscapes(output);

        if (noNewline)
            Console.Write(output);
        else
            Console.WriteLine(output);

        return 0;
    }

    private static int ExecutePwd(string workingDirectory)
    {
        Console.WriteLine(workingDirectory);
        return 0;
    }

    private static int ExecuteType(CommandNode cmd, ShellEnvironment env, string workingDirectory)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: type: not enough arguments");
            return 1;
        }

        int result = 0;

        foreach (string name in cmd.Args)
        {
            if (IsBuiltin(name))
            {
                Console.WriteLine($"{name} is a shell builtin");
            }
            else if (env.GetAlias(name) != null)
            {
                Console.WriteLine($"{name} is aliased to '{env.GetAlias(name)}'");
            }
            else
            {
                string? path = Pipeline.ResolveCommand(name, workingDirectory);
                if (path != null)
                {
                    Console.WriteLine($"{name} is {path}");
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

    private static int ExecuteAlias(CommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            foreach (var kv in env.Aliases.OrderBy(k => k.Key))
                Console.WriteLine($"alias {kv.Key}='{kv.Value}'");
            return 0;
        }

        foreach (string arg in cmd.Args)
        {
            int eqIdx = arg.IndexOf('=');
            if (eqIdx >= 0)
            {
                string name = arg.Substring(0, eqIdx);
                string value = arg.Substring(eqIdx + 1);

                if ((value.StartsWith('\'') && value.EndsWith('\'')) ||
                    (value.StartsWith('"') && value.EndsWith('"')))
                    value = value.Substring(1, value.Length - 2);

                env.SetAlias(name, value);
            }
            else
            {
                string? alias = env.GetAlias(arg);
                if (alias != null)
                    Console.WriteLine($"alias {arg}='{alias}'");
                else
                {
                    Console.Error.WriteLine($"aursh: alias: {arg}: not found");
                    return 1;
                }
            }
        }

        return 0;
    }

    private static int ExecuteUnalias(CommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: unalias: not enough arguments");
            return 1;
        }

        if (cmd.Args[0] == "-a")
        {
            foreach (string key in env.Aliases.Keys.ToList())
                env.UnsetAlias(key);
            return 0;
        }

        foreach (string name in cmd.Args)
        {
            if (!env.UnsetAlias(name))
            {
                Console.Error.WriteLine($"aursh: unalias: {name}: not found");
                return 1;
            }
        }

        return 0;
    }

    private static int ExecuteSource(CommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: source: filename argument required");
            return 1;
        }

        string filePath = Utils.FileSystem.ResolvePath(cmd.Args[0], workingDirectory);

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"aursh: source: {cmd.Args[0]}: No such file or directory");
            return 1;
        }

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        string[] scriptArgs = cmd.Args.Count > 1 ? cmd.Args.Skip(1).ToArray() : Array.Empty<string>();

        if (extension == ".aur")
        {
            var runner = new ScriptRunner(env, workingDirectory);
            int result = runner.RunFile(filePath, scriptArgs);
            return result;
        }

        var executor = new Executor(env, workingDirectory);
        var rcLoader = new RcLoader(env, executor);
        int rcResult = rcLoader.LoadFrom(filePath);
        workingDirectory = executor.WorkingDirectory;
        return rcResult;
    }

    private static int ExecuteSet(CommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            foreach (var kv in env.Variables.OrderBy(k => k.Key))
                Console.WriteLine($"{kv.Key}={kv.Value}");
            return 0;
        }

        for (int i = 0; i < cmd.Args.Count; i++)
        {
            string arg = cmd.Args[i];
            int eqIdx = arg.IndexOf('=');
            if (eqIdx >= 0)
            {
                string name = arg.Substring(0, eqIdx);
                string value = arg.Substring(eqIdx + 1);
                env.Set(name, value);
            }
        }

        return 0;
    }

    private static int ExecuteEnv(ShellEnvironment env)
    {
        foreach (var kv in env.Variables.OrderBy(k => k.Key))
            Console.WriteLine($"{kv.Key}={kv.Value}");
        return 0;
    }

    private static int ExecuteRead(CommandNode cmd, ShellEnvironment env)
    {
        string varName = cmd.Args.Count > 0 ? cmd.Args[0] : "REPLY";
        string prompt = "";

        for (int i = 0; i < cmd.Args.Count - 1; i++)
        {
            if (cmd.Args[i] == "-p" && i + 1 < cmd.Args.Count)
            {
                prompt = cmd.Args[i + 1];
                varName = cmd.Args.Count > i + 2 ? cmd.Args[i + 2] : "REPLY";
                break;
            }
        }

        if (!string.IsNullOrEmpty(prompt))
            Console.Write(prompt);

        string? line = Console.ReadLine();
        env.Set(varName, line ?? "");

        return line == null ? 1 : 0;
    }

    private static int ExecuteTest(CommandNode cmd)
    {
        if (cmd.Args.Count == 0)
            return 1;

        if (cmd.Args.Count == 1)
            return string.IsNullOrEmpty(cmd.Args[0]) ? 1 : 0;

        if (cmd.Args.Count == 2)
        {
            string op = cmd.Args[0];
            string operand = cmd.Args[1];

            return op switch
            {
                "-z" => string.IsNullOrEmpty(operand) ? 0 : 1,
                "-n" => !string.IsNullOrEmpty(operand) ? 0 : 1,
                "-f" => File.Exists(operand) ? 0 : 1,
                "-d" => Directory.Exists(operand) ? 0 : 1,
                "-e" => (File.Exists(operand) || Directory.Exists(operand)) ? 0 : 1,
                "-r" => File.Exists(operand) ? 0 : 1,
                "-w" => File.Exists(operand) ? 0 : 1,
                "-x" => File.Exists(operand) ? 0 : 1,
                "-s" => (File.Exists(operand) && new FileInfo(operand).Length > 0) ? 0 : 1,
                "!" => string.IsNullOrEmpty(operand) ? 0 : 1,
                _ => 1
            };
        }

        if (cmd.Args.Count == 3)
        {
            string left = cmd.Args[0];
            string op = cmd.Args[1];
            string right = cmd.Args[2];

            return op switch
            {
                "=" or "==" => left == right ? 0 : 1,
                "!=" => left != right ? 0 : 1,
                "-eq" => ParseIntCompare(left, right, (a, b) => a == b),
                "-ne" => ParseIntCompare(left, right, (a, b) => a != b),
                "-lt" => ParseIntCompare(left, right, (a, b) => a < b),
                "-le" => ParseIntCompare(left, right, (a, b) => a <= b),
                "-gt" => ParseIntCompare(left, right, (a, b) => a > b),
                "-ge" => ParseIntCompare(left, right, (a, b) => a >= b),
                _ => 1
            };
        }

        return 1;
    }

    private static int ExecuteReturn(CommandNode cmd, ShellEnvironment env)
    {
        int code = 0;
        if (cmd.Args.Count > 0)
            int.TryParse(cmd.Args[0], out code);
        env.LastExitCode = code;
        return code;
    }

    private static int ExecuteFallback(CommandNode cmd)
    {
        Console.Error.WriteLine($"aursh: {cmd.Name}: builtin not implemented");
        return 1;
    }

    private static int ParseIntCompare(string left, string right, Func<int, int, bool> predicate)
    {
        if (int.TryParse(left, out int l) && int.TryParse(right, out int r))
            return predicate(l, r) ? 0 : 1;
        return 2;
    }

    private static string InterpretEscapes(string input)
    {
        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '\\' && i + 1 < input.Length)
            {
                char next = input[i + 1];
                switch (next)
                {
                    case 'n': sb.Append('\n'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 'a': sb.Append('\a'); i++; break;
                    case 'b': sb.Append('\b'); i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    case '0': sb.Append('\0'); i++; break;
                    case 'e': sb.Append('\x1b'); i++; break;
                    default: sb.Append('\\'); sb.Append(next); i++; break;
                }
            }
            else
            {
                sb.Append(input[i]);
            }
        }
        return sb.ToString();
    }

    private static int ExecuteJobs(CommandNode cmd, ShellEnvironment env)
    {
        var allJobs = env.Jobs.GetAll();

        if (allJobs.Count == 0)
        {
            return 0;
        }

        bool showPids = cmd.Args.Contains("-l");

        foreach (var job in allJobs)
        {
            string stateStr = job.State switch
            {
                JobState.Running => "Running",
                JobState.Done => "Done",
                JobState.Killed => "Killed",
                _ => "Unknown"
            };

            if (showPids)
                Console.WriteLine($"[{job.Id}]  {job.Pid,-8} {stateStr,-12} {job.Command}");
            else
                Console.WriteLine($"[{job.Id}]  {stateStr,-12} {job.Command}");
        }

        return 0;
    }

    private static int ExecuteFg(CommandNode cmd, ShellEnvironment env)
    {
        int jobId;

        if (cmd.Args.Count == 0)
        {
            var recent = env.Jobs.GetMostRecent();
            if (recent == null)
            {
                Console.Error.WriteLine("aursh: fg: no current job");
                return 1;
            }
            jobId = recent.Id;
        }
        else
        {
            string arg = cmd.Args[0];
            if (arg.StartsWith("%"))
                arg = arg.Substring(1);

            if (!int.TryParse(arg, out jobId))
            {
                Console.Error.WriteLine($"aursh: fg: {cmd.Args[0]}: no such job");
                return 1;
            }
        }

        var job = env.Jobs.GetById(jobId);
        if (job == null)
        {
            Console.Error.WriteLine($"aursh: fg: %{jobId}: no such job");
            return 1;
        }

        if (job.State != JobState.Running)
        {
            Console.Error.WriteLine($"aursh: fg: %{jobId}: job has already completed");
            env.Jobs.Remove(jobId);
            return job.ExitCode;
        }

        Console.WriteLine(job.Command);
        int exitCode = env.Jobs.ForegroundWait(jobId);
        return exitCode;
    }

    private static int ExecuteKill(CommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: kill: usage: kill %job_id or kill PID");
            return 1;
        }

        int result = 0;

        foreach (string arg in cmd.Args)
        {
            if (arg.StartsWith("%"))
            {
                string idStr = arg.Substring(1);
                if (int.TryParse(idStr, out int jobId))
                {
                    if (env.Jobs.Kill(jobId))
                    {
                        var job = env.Jobs.GetById(jobId);
                        if (job != null)
                            Console.WriteLine($"[{job.Id}]  Killed  {job.Command}");
                    }
                    else
                    {
                        Console.Error.WriteLine($"aursh: kill: %{jobId}: no such job");
                        result = 1;
                    }
                }
                else
                {
                    Console.Error.WriteLine($"aursh: kill: {arg}: invalid job spec");
                    result = 1;
                }
            }
            else
            {
                if (int.TryParse(arg, out int pid))
                {
                    var jobByPid = env.Jobs.GetByPid(pid);
                    if (jobByPid != null)
                    {
                        env.Jobs.Kill(jobByPid.Id);
                        Console.WriteLine($"[{jobByPid.Id}]  Killed  {jobByPid.Command}");
                    }
                    else
                    {
                        try
                        {
                            var proc = System.Diagnostics.Process.GetProcessById(pid);
                            proc.Kill();
                            Console.WriteLine($"Killed process {pid}");
                        }
                        catch
                        {
                            Console.Error.WriteLine($"aursh: kill: ({pid}) - No such process");
                            result = 1;
                        }
                    }
                }
                else
                {
                    Console.Error.WriteLine($"aursh: kill: {arg}: invalid argument");
                    result = 1;
                }
            }
        }

        return result;
    }

    private static int ExecuteAurshPlugin(CommandNode cmd, ShellEnvironment env, string workingDirectory)
    {
        if (cmd.Args.Count == 0)
        {
            Console.WriteLine("Usage: aursh-plugin <list|add|del|init|debug> [args]");
            Console.WriteLine("  list           List installed plugins");
            Console.WriteLine("  add <path>     Install plugin from directory");
            Console.WriteLine("  del <name>     Remove a plugin");
            Console.WriteLine("  init <name>    Create a new plugin template in current directory");
            Console.WriteLine("  debug <file>   Check lua script for syntax errors");
            return 0;
        }

        string action = cmd.Args[0].ToLowerInvariant();
        var pm = env.PluginManager;
        if (pm == null)
        {
            Console.Error.WriteLine("aursh: plugin system not initialized");
            return 1;
        }

        switch (action)
        {
            case "list":
                var plugins = pm.Plugins;
                if (plugins.Count == 0)
                {
                    Console.WriteLine("No plugins installed.");
                    Console.WriteLine($"Plugin directory: {pm.PluginsDirectory}");
                    return 0;
                }
                foreach (var p in plugins)
                {
                    string cmds = p.RegisteredCommands.Count > 0
                        ? string.Join(", ", p.RegisteredCommands.Keys)
                        : "(none)";
                    Console.WriteLine($"  {p.Manifest.Name} v{p.Manifest.Version} by {p.Manifest.Author}");
                    Console.WriteLine($"    {p.Manifest.Description}");
                    Console.WriteLine($"    Commands: {cmds}");
                }
                Console.WriteLine($"\nPlugin directory: {pm.PluginsDirectory}");
                return 0;

            case "add":
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin add <path>"); return 1; }
                return pm.InstallPlugin(cmd.Args[1]);

            case "del":
            case "remove":
            case "rm":
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin del <name>"); return 1; }
                return pm.RemovePlugin(cmd.Args[1]);

            case "init":
            case "create":
            case "new":
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin init <name>"); return 1; }
                return pm.InitPlugin(cmd.Args[1], workingDirectory);

            case "debug":
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin debug <file_or_plugin>"); return 1; }
                return pm.DebugPlugin(cmd.Args[1], workingDirectory);

            default:
                Console.Error.WriteLine($"aursh: aursh-plugin: unknown action '{action}'");
                return 1;
        }
    }

    private static int ExecuteAssoc(CommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            var all = env.Associator.GetAll();
            if (all.Count == 0)
            {
                Console.WriteLine("No file associations set.");
                return 0;
            }

            foreach (var kv in all)
            {
                Console.WriteLine($"aursh-assoc {kv.Key} \"{kv.Value}\"");
            }
            return 0;
        }

        string ext = cmd.Args[0];

        if (cmd.Args.Count == 1)
        {
            string? template = env.Associator.GetAssociation(ext);
            if (template != null)
            {
                Console.WriteLine($"aursh-assoc {ext} \"{template}\"");
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"aursh: aursh-assoc: no association found for extension {ext}");
                return 1;
            }
        }

        if (cmd.Args.Count >= 2)
        {
            if (cmd.Args[1] == "--remove" || cmd.Args[1] == "--delete" || cmd.Args[1] == "--rm")
            {
                if (env.Associator.RemoveAssociation(ext))
                {
                    Console.WriteLine($"Removed association for {ext}");
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"aursh: aursh-assoc: no association found for extension {ext}");
                    return 1;
                }
            }

            string template = string.Join(" ", cmd.Args.Skip(1));
            if ((template.StartsWith('"') && template.EndsWith('"')) || 
                (template.StartsWith('\'') && template.EndsWith('\'')))
            {
                template = template.Substring(1, template.Length - 2);
            }

            env.Associator.SetAssociation(ext, template);
            return 0;
        }

        return 1;
    }

    private static int ExecuteReload(ShellEnvironment env)
    {
        Console.WriteLine("Reloading plugins...");
        if (env.PluginManager != null)
        {
            env.PluginManager.UnloadAll();
            env.PluginManager.LoadAll();
        }
        Console.WriteLine("Reload complete.");
        return 0;
    }
}
