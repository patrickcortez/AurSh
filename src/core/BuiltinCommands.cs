using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using AurShell.Utils;

namespace AurShell.Core;

public static class BuiltinCommands
{

    private readonly static string version = "1.3";
    private static readonly HashSet<string> Builtins = new(StringComparer.OrdinalIgnoreCase)
    {
        "cd", "export", "unset", "exit", "history", "clear", "echo",
        "pwd", "type", "alias", "unalias", "source", "set", "env",
        "true", "false", "shift", "read", "test", "return",
        "jobs", "fg", "kill", "aursh-plugin", "aursh-assoc", "aursh-reload", "aursh-history","aursh-about","aursh-ls","aursh-cat", "aursh-update"
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
            "aursh-ls" => ExecuteLs(cmd, env, ref workingDirectory),
            "aursh-cat" => ExecuteCat(cmd, env, ref workingDirectory),
            "aursh-update" => ExecuteUpdate(),
            _ => ExecuteFallback(cmd)
        };
    }

    private static int ExecuteCat(CommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        bool isEditorMode = false;
        string targetFile = "";

        if (cmd.Args.Count == 0)
        {
            isEditorMode = true;
        }
        else if (cmd.Args[0] == "-e")
        {
            isEditorMode = true;
            if (cmd.Args.Count > 1)
            {
                targetFile = Utils.FileSystem.ResolvePath(cmd.Args[1], workingDirectory);
            }
        }

        if (!isEditorMode)
        {
            int streamExitCode = 0;
            foreach (string arg in cmd.Args)
            {
                string resolvedPath = Utils.FileSystem.ResolvePath(arg, workingDirectory);
                if (!File.Exists(resolvedPath))
                {
                    Console.Error.WriteLine($"aursh: aursh-cat: {arg}: No such file or directory");
                    streamExitCode = 1;
                    continue;
                }

                try
                {
                    using var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        Console.WriteLine(line);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"aursh: aursh-cat: {arg}: {ex.Message}");
                    streamExitCode = 1;
                }
            }
            return streamExitCode;
        }

        string displayFile = string.IsNullOrEmpty(targetFile) ? "[New File]" : targetFile;
        List<StringBuilder> buffer = new List<StringBuilder>();

        if (!string.IsNullOrEmpty(targetFile) && File.Exists(targetFile))
        {
            string[] lines = Utils.FileSystem.ReadAllLinesSafe(targetFile);
            if (lines.Length == 0) 
                buffer.Add(new StringBuilder());
            else 
                foreach (string line in lines) 
                    buffer.Add(new StringBuilder(line));
        }
        else
        {
            buffer.Add(new StringBuilder());
        }

        int cursorRow = 0;
        int cursorCol = 0;
        int scrollRow = 0;
        int scrollCol = 0;

        bool commandMode = false;
        string commandInput = "";
        string statusMessage = "";
        string findQuery = "";

        bool running = true;
        int exitCode = 0;

        Console.Write("\x1b[?1049h\x1b[?25l");

        try
        {
            while (running)
            {
                int width = Utils.Platform.TerminalWidth;
                int height = Utils.Platform.TerminalHeight;
                int headerLines = 3;
                int bottomLines = 1;
                int textHeight = height - headerLines - bottomLines;

                if (cursorRow < 0) cursorRow = 0;
                if (cursorRow >= buffer.Count) cursorRow = buffer.Count - 1;
                if (cursorCol < 0) cursorCol = 0;
                if (cursorCol > buffer[cursorRow].Length) cursorCol = buffer[cursorRow].Length;

                if (cursorRow < scrollRow) scrollRow = cursorRow;
                if (cursorRow >= scrollRow + textHeight) scrollRow = cursorRow - textHeight + 1;

                int maxLineNumStrLen = buffer.Count.ToString().Length;
                int prefixLen = maxLineNumStrLen + 4; 
                int maxTextWidth = width - prefixLen - 1;

                if (cursorCol < scrollCol) scrollCol = cursorCol;
                if (cursorCol >= scrollCol + maxTextWidth) scrollCol = cursorCol - maxTextWidth + 1;

                Console.Write("\x1b[1;1H" + Utils.Ansi.ClearLine + "  AurSh Cat (Editor)");
                Console.Write("\x1b[2;1H" + Utils.Ansi.ClearLine + $"  {Utils.Ansi.FgBrightBlack}{displayFile}{Utils.Ansi.Reset}");
                Console.Write("\x1b[3;1H" + Utils.Ansi.ClearLine + Utils.Ansi.FgBrightBlack + new string('\u2500', width - 1) + Utils.Ansi.Reset);

                for (int i = 0; i < textHeight; i++)
                {
                    int screenRow = headerLines + i + 1;
                    Console.Write($"\x1b[{screenRow};1H");
                    Console.Write(Utils.Ansi.ClearLine);

                    int r = scrollRow + i;
                    if (r < buffer.Count)
                    {
                        string lineNumStr = (r + 1).ToString().PadLeft(maxLineNumStrLen);
                        string prefix = $" {lineNumStr} \u2502 ";

                        string line = buffer[r].ToString();
                        string visible = "";
                        if (scrollCol < line.Length)
                        {
                            visible = line.Substring(scrollCol);
                            if (visible.Length > maxTextWidth) visible = visible.Substring(0, maxTextWidth);
                        }

                        if (!string.IsNullOrEmpty(findQuery))
                        {
                            string highlight = Utils.Ansi.BgBrightYellow + Utils.Ansi.FgBlack;
                            string reset = Utils.Ansi.Reset;
                            visible = visible.Replace(findQuery, highlight + findQuery + reset);
                        }

                        if (r == cursorRow && !commandMode)
                        {
                            Console.Write(Utils.Ansi.FgBrightCyan + $" {lineNumStr} " + Utils.Ansi.FgBrightBlack + "\u2502 " + Utils.Ansi.Reset + visible);
                        }
                        else
                        {
                            Console.Write(Utils.Ansi.FgBrightBlack + prefix + Utils.Ansi.Reset + visible);
                        }
                    }
                    else
                    {
                        string emptyPrefix = $" {new string(' ', maxLineNumStrLen)} \u2502 ";
                        Console.Write(Utils.Ansi.FgBrightBlack + emptyPrefix + Utils.Ansi.Reset);
                    }
                }

                Console.Write($"\x1b[{height};1H");
                Console.Write(Utils.Ansi.ClearLine);
                
                if (commandMode)
                {
                    Console.Write(Utils.Ansi.FgRgb(150, 150, 150) + " :" + Utils.Ansi.FgWhite + commandInput);
                }
                else if (!string.IsNullOrEmpty(statusMessage))
                {
                    Console.Write(Utils.Ansi.FgBrightYellow + $"  {statusMessage}" + Utils.Ansi.Reset);
                }
                else
                {
                    string info = $"Ln {cursorRow + 1}, Col {cursorCol + 1} | Press ':' for commands";
                    Console.Write(Utils.Ansi.FgRgb(150, 150, 150) + $"  {info}" + Utils.Ansi.Reset);
                }

                if (commandMode)
                {
                    Console.Write($"\x1b[{height};{3 + commandInput.Length}H");
                    Console.Write("\x1b[?25h"); 
                }
                else
                {
                    int screenRow = headerLines + (cursorRow - scrollRow) + 1;
                    int screenCol = prefixLen + (cursorCol - scrollCol) + 1;
                    Console.Write($"\x1b[{screenRow};{screenCol}H");
                    Console.Write("\x1b[?25h"); 
                }

                var key = Console.ReadKey(true);
                statusMessage = "";

                if (commandMode)
                {
                    if (key.Key == ConsoleKey.Escape)
                    {
                        commandMode = false;
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        commandMode = false;
                        string cmdStr = commandInput.Trim();
                        
                        if (cmdStr == "q" || cmdStr == "q!")
                        {
                            running = false;
                        }
                        else if (cmdStr.StartsWith("w ") || cmdStr.StartsWith("wq ") || cmdStr == "w" || cmdStr == "wq")
                        {
                            string[] parts = cmdStr.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                            string fileToSave = targetFile; 

                            if (parts.Length > 1)
                            {
                                fileToSave = Utils.FileSystem.ResolvePath(parts[1], workingDirectory);
                            }

                            if (string.IsNullOrEmpty(fileToSave))
                            {
                                statusMessage = "Error: No filename specified.";
                            }
                            else
                            {
                                try
                                {
                                    System.IO.File.WriteAllLines(fileToSave, buffer.Select(b => b.ToString()));
                                    statusMessage = $"Written to {fileToSave}";
                                    targetFile = fileToSave; 
                                    displayFile = targetFile;
                                    if (parts[0] == "wq" || parts[0] == "wq ") running = false;
                                }
                                catch (Exception ex)
                                {
                                    statusMessage = $"Save failed: {ex.Message}";
                                }
                            }
                        }
                        else if (cmdStr.StartsWith("find "))
                        {
                            string[] parts = cmdStr.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 1)
                            {
                                findQuery = parts[1];
                                int count = 0;
                                bool foundFirst = false;

                                for (int r = 0; r < buffer.Count; r++)
                                {
                                    string s = buffer[r].ToString();
                                    int idx = 0;
                                    while ((idx = s.IndexOf(findQuery, idx, StringComparison.Ordinal)) != -1)
                                    {
                                        count++;
                                        if (!foundFirst)
                                        {
                                            cursorRow = r;
                                            cursorCol = idx;
                                            foundFirst = true;
                                        }
                                        idx += findQuery.Length;
                                    }
                                }
                                statusMessage = $"Found {count} occurrences of '{findQuery}'.";
                            }
                            else
                            {
                                statusMessage = "Error: No search term specified.";
                                findQuery = "";
                            }
                        }
                        else if (cmdStr == "find")
                        {
                            findQuery = "";
                            statusMessage = "Search cleared.";
                        }
                        else if (cmdStr.StartsWith("replace "))
                        {
                            string[] parts = cmdStr.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 3)
                            {
                                string oldText = parts[1];
                                string newText = parts[2];
                                int totalReplaced = 0;
                                
                                foreach (var b in buffer)
                                {
                                    string s = b.ToString();
                                    int lineCount = (s.Length - s.Replace(oldText, "").Length) / oldText.Length;
                                    if (lineCount > 0)
                                    {
                                        totalReplaced += lineCount;
                                        b.Replace(oldText, newText);
                                    }
                                }
                                statusMessage = $"Replaced {totalReplaced} occurrences of '{oldText}'.";
                                if (findQuery == oldText) findQuery = newText;
                            }
                            else
                            {
                                statusMessage = "Usage: :replace <old> <new>";
                            }
                        }
                        else
                        {
                            statusMessage = "Unknown command.";
                        }
                    }
                    else if (key.Key == ConsoleKey.Backspace)
                    {
                        if (commandInput.Length > 0)
                            commandInput = commandInput.Substring(0, commandInput.Length - 1);
                        else
                            commandMode = false;
                    }
                    else if (!char.IsControl(key.KeyChar))
                    {
                        commandInput += key.KeyChar;
                    }
                    continue;
                }

                if (key.KeyChar == ':')
                {
                    commandMode = true;
                    commandInput = "";
                    continue;
                }

                if (key.Key == ConsoleKey.LeftArrow)
                {
                    if (cursorCol > 0) cursorCol--;
                    else if (cursorRow > 0) { cursorRow--; cursorCol = buffer[cursorRow].Length; }
                }
                else if (key.Key == ConsoleKey.RightArrow)
                {
                    if (cursorCol < buffer[cursorRow].Length) cursorCol++;
                    else if (cursorRow < buffer.Count - 1) { cursorRow++; cursorCol = 0; }
                }
                else if (key.Key == ConsoleKey.UpArrow)
                {
                    cursorRow--;
                }
                else if (key.Key == ConsoleKey.DownArrow)
                {
                    cursorRow++;
                }
                else if (key.Key == ConsoleKey.Home)
                {
                    cursorCol = 0;
                }
                else if (key.Key == ConsoleKey.End)
                {
                    cursorCol = buffer[cursorRow].Length;
                }
                else if (key.Key == ConsoleKey.PageUp)
                {
                    cursorRow -= textHeight;
                }
                else if (key.Key == ConsoleKey.PageDown)
                {
                    cursorRow += textHeight;
                }
                else if (key.Key == ConsoleKey.Backspace)
                {
                    if (cursorCol > 0)
                    {
                        buffer[cursorRow].Remove(cursorCol - 1, 1);
                        cursorCol--;
                    }
                    else if (cursorRow > 0)
                    {
                        string remainder = buffer[cursorRow].ToString();
                        buffer.RemoveAt(cursorRow);
                        cursorRow--;
                        cursorCol = buffer[cursorRow].Length;
                        buffer[cursorRow].Append(remainder);
                    }
                }
                else if (key.Key == ConsoleKey.Delete)
                {
                    if (cursorCol < buffer[cursorRow].Length)
                    {
                        buffer[cursorRow].Remove(cursorCol, 1);
                    }
                    else if (cursorRow < buffer.Count - 1)
                    {
                        string remainder = buffer[cursorRow + 1].ToString();
                        buffer.RemoveAt(cursorRow + 1);
                        buffer[cursorRow].Append(remainder);
                    }
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    string remainder = buffer[cursorRow].ToString().Substring(cursorCol);
                    buffer[cursorRow].Length = cursorCol;
                    buffer.Insert(cursorRow + 1, new StringBuilder(remainder));
                    cursorRow++;
                    cursorCol = 0;
                }
                else if (!char.IsControl(key.KeyChar) || key.KeyChar == '\t')
                {
                    char c = key.KeyChar;
                    if (c == '\t')
                    {
                        buffer[cursorRow].Insert(cursorCol, "    ");
                        cursorCol += 4;
                    }
                    else
                    {
                        buffer[cursorRow].Insert(cursorCol, c);
                        cursorCol++;
                    }
                }
            }
        }
        finally
        {
            Console.Write("\x1b[?1049l\x1b[?25h"); 
        }

        return exitCode;
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
                            {Ansi.FgBrightBlue}Current Version: {Ansi.FgBrightMagenta}{version}

                            {Ansi.FgBrightBlue}AurSh has some native commands that you can invoke/use:
                                - {Ansi.FgBrightCyan}aursh-ls : {Ansi.FgBrightBlue}TUI file-system explorer.
                                - {Ansi.FgBrightCyan}aursh-about : {Ansi.FgBrightBlue}Shows this message.
                                - {Ansi.FgBrightCyan}aursh-assoc : {Ansi.FgBrightBlue}Associate file extensions with its compiler/interpreter.
                                - {Ansi.FgBrightCyan}aursh-plugin : {Ansi.FgBrightBlue}Plugin management of AurSh.
                                - {Ansi.FgBrightCyan}aursh-history : {Ansi.FgBrightBlue}TUI command history.
                                - {Ansi.FgBrightCyan}aursh-reload : {Ansi.FgBrightBlue}Reloads the Shell to apply newly added plugins.
                                - {Ansi.FgBrightCyan}aursh-cat <options: -e> <file> : {Ansi.FgBrightBlue}Pipable file reader and vim-like TUI text editor.

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

    private static int ExecuteLs(CommandNode cmd, ShellEnvironment env, ref string workingDirectory)
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

        string? selected = RunLsTui(targetPath);
        if (string.IsNullOrEmpty(selected))
            return 0;

        if (Directory.Exists(selected))
        {
            string oldDir = workingDirectory;
            workingDirectory = Utils.FileSystem.NormalizePath(selected);
            try { Directory.SetCurrentDirectory(workingDirectory); } catch { }
            env.Set("OLDPWD", oldDir);
            env.Set("PWD", workingDirectory);
        }
        else
        {
            Console.WriteLine(selected);
        }

        return 0;
    }

   
    private static void ExecuteCommand(string command, List<string> lines, bool isEditMode, string filePath, ref bool running, ref bool bufferModified)
    {
        var parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        switch (parts[0].ToLower())
        {
            case "q":
                if (bufferModified && isEditMode)
                {
                    Console.Error.WriteLine("No write since last change (add ! to override)");
                }
                else
                {
                    running = false;
                }
                break;
                
            case "q!":
                running = false;
                break;
                
            case "w":
                if (parts.Length >= 2)
                {
                    try
                    {
                        File.WriteAllLines(parts[1], lines);
                        bufferModified = false;
                        Console.WriteLine($"\"{parts[1]}\" {lines.Count}L written");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error writing file: {ex.Message}");
                    }
                }
                else if (!string.IsNullOrEmpty(filePath))
                {
                    try
                    {
                        File.WriteAllLines(filePath, lines);
                        bufferModified = false;
                        Console.WriteLine($"\"{filePath}\" {lines.Count}L written");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error writing file: {ex.Message}");
                    }
                }
                else
                {
                    Console.Error.WriteLine("No file name specified");
                }
                break;
                
            case "wq":
                if (parts.Length >= 2)
                {
                    try
                    {
                        File.WriteAllLines(parts[1], lines);
                        running = false;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error writing file: {ex.Message}");
                    }
                }
                else if (!string.IsNullOrEmpty(filePath))
                {
                    try
                    {
                        File.WriteAllLines(filePath, lines);
                        running = false;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error writing file: {ex.Message}");
                    }
                }
                else
                {
                    Console.Error.WriteLine("No file name specified");
                }
                break;
                
            default:
                Console.Error.WriteLine($"Not an editor command: {parts[0]}");
                break;
        }
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

    private static string? RunLsTui(string targetPath)
    {
        string filter = "";
        int selectedIndex = 0;
        int scrollOffset = 0;
        bool running = true;
        string? result = null;

        var allEntries = new DirectoryInfo(targetPath).GetFileSystemInfos()
            .OrderBy(e => e is DirectoryInfo ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filtered = allEntries.ToList();

        Console.Write("\x1b[?1049h\x1b[?25l");

        try
        {
            while (running)
            {
                int height = Utils.Platform.TerminalHeight - 5;
                int width = Utils.Platform.TerminalWidth;

                if (selectedIndex < scrollOffset) scrollOffset = selectedIndex;
                if (selectedIndex >= scrollOffset + height) scrollOffset = selectedIndex - height + 1;

                Console.Write(Utils.Ansi.SetCursorPosition(1, 1));

                Console.Write(Utils.Ansi.ClearLine);
                Console.WriteLine("\n  AurSh File Browser");

                Console.Write(Utils.Ansi.ClearLine);
                Console.WriteLine($"  {Utils.Ansi.FgBrightBlack}{targetPath}{Utils.Ansi.Reset}");

                Console.Write(Utils.Ansi.ClearLine);
                Console.WriteLine(Utils.Ansi.FgBrightBlack + new string('\u2500', width) + Utils.Ansi.Reset);

                for (int i = 0; i < height; i++)
                {
                    Console.Write(Utils.Ansi.ClearLine);
                    int itemIdx = scrollOffset + i;
                    if (itemIdx < filtered.Count)
                    {
                        bool isSelected = itemIdx == selectedIndex;
                        var item = filtered[itemIdx];
                        bool isDir = item is DirectoryInfo;
                        bool isExe = !isDir && Path.GetExtension(item.Name).Equals(".exe", StringComparison.OrdinalIgnoreCase);
                        bool isAur = !isDir && Path.GetExtension(item.Name).Equals(".aur", StringComparison.OrdinalIgnoreCase);

                        string displayName = item.Name;
                        if (isDir) displayName += "/";

                        string sizeStr = isDir ? "--" : FormatSize(((FileInfo)item).Length);
                        string dateStr = item.LastWriteTime.ToString("yyyy-MM-dd HH:mm");

                        int dateWidth = 16;
                        int sizeWidth = 7;
                        int gap = 2;
                        int leftMargin = 2;
                        int nameMaxWidth = Math.Max(10, width - leftMargin - sizeWidth - gap - dateWidth - gap - 1);

                        if (displayName.Length > nameMaxWidth)
                            displayName = displayName.Substring(0, nameMaxWidth - 1) + "\u2026";

                        string namePadded = displayName.PadRight(nameMaxWidth);
                        string sizePadded = sizeStr.PadLeft(sizeWidth);

                        if (isSelected)
                        {
                            string line = $"  {namePadded}  {sizePadded}  {dateStr}";
                            Console.Write(Utils.Ansi.BgRgb(50, 50, 70) + Utils.Ansi.FgWhite + line.PadRight(width - 1) + Utils.Ansi.Reset);
                        }
                        else
                        {
                            string fg;
                            if (isDir) fg = Utils.Ansi.FgBrightBlue;
                            else if (isExe) fg = Utils.Ansi.FgBrightGreen;
                            else if (isAur) fg = Utils.Ansi.FgBrightYellow;
                            else fg = Utils.Ansi.FgBrightWhite;

                            Console.Write($"  {fg}{namePadded}{Utils.Ansi.Reset}  {Utils.Ansi.FgBrightBlack}{sizePadded}{Utils.Ansi.Reset}  {Utils.Ansi.FgBrightBlack}{dateStr}{Utils.Ansi.Reset}");
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
                        result = filtered[selectedIndex].FullName;
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
                        ApplyFilter();
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    filter += key.KeyChar;
                    ApplyFilter();
                }
            }
        }
        finally
        {
            Console.Write("\x1b[?25h\x1b[?1049l");
        }

        return result;

        void ApplyFilter()
        {
            if (string.IsNullOrEmpty(filter))
                filtered = allEntries.ToList();
            else
                filtered = allEntries.Where(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            selectedIndex = 0;
            scrollOffset = 0;
        }

        static string FormatSize(long bytes)
        {
            string[] units = { "B", "K", "M", "G", "T" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            if (unit == 0) return $"{bytes}".PadLeft(6);
            return $"{size:0.0}{units[unit]}".PadLeft(6);
        }
    }

    private static int ExecuteClear()
    {
        Console.Write("\x1b[2J\x1b[3J\x1b[H");
        Console.Out.Flush();
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
            Console.WriteLine("Usage: aursh-plugin <list|add|del|init|debug|update|unload> [args]");
            Console.WriteLine("  list                      List installed plugins");
            Console.WriteLine("  add <path>                Install plugin from directory");
            Console.WriteLine("  del <name>                Remove a plugin");
            Console.WriteLine("  init <name> [--type lua|fsharp]  Create a new plugin template");
            Console.WriteLine("  debug <file>              Check script for syntax errors");
            Console.WriteLine("  update <name>             Unload and reload a plugin");
            Console.WriteLine("  unload <name>             Unload a plugin from memory");
            Console.WriteLine("  unload -d <name>          Unload, delete directory, and reload");
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
                        : (p.Manifest.Commands.Count > 0
                            ? string.Join(", ", p.Manifest.Commands)
                            : "(none)");
                    Console.WriteLine($"  {p.Manifest.Name} v{p.Manifest.Version} by {p.Manifest.Author}");
                    Console.WriteLine($"    Type: {p.Manifest.Type}");
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
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin init <name> [--type lua|fsharp]"); return 1; }
                string pluginType = "lua";
                for (int i = 2; i < cmd.Args.Count; i++)
                {
                    if (cmd.Args[i] == "--type" && i + 1 < cmd.Args.Count)
                    {
                        pluginType = cmd.Args[i + 1];
                        break;
                    }
                }
                return pm.InitPlugin(cmd.Args[1], workingDirectory, pluginType);

            case "debug":
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin debug <file_or_plugin>"); return 1; }
                return pm.DebugPlugin(cmd.Args[1], workingDirectory);

            case "update":
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin update <name>"); return 1; }
                return pm.UpdatePlugin(cmd.Args[1]);

            case "unload":
                if (cmd.Args.Count < 2) { Console.Error.WriteLine("aursh: aursh-plugin unload <name>"); return 1; }
                if (cmd.Args[1] == "-d" && cmd.Args.Count < 3) { Console.Error.WriteLine("aursh: aursh-plugin unload -d <name>"); return 1; }
                {
                    bool deleteDir = cmd.Args[1] == "-d";
                    string name = deleteDir ? cmd.Args[2] : cmd.Args[1];
                    if (pm.UnloadPlugin(name))
                    {
                        if (deleteDir)
                        {
                            string pluginDir = Path.Combine(pm.PluginsDirectory, name);
                            if (Directory.Exists(pluginDir))
                            {
                                try
                                {
                                    Directory.Delete(pluginDir, true);
                                    Console.WriteLine($"Unloaded and deleted plugin '{name}'");
                                }
                                catch (Exception ex)
                                {
                                    Console.Error.WriteLine($"aursh: failed to delete plugin '{name}': {ex.Message}");
                                    return 1;
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Unloaded plugin '{name}'");
                            }
                            return pm.UpdatePlugin(name);
                        }
                        Console.WriteLine($"Unloaded plugin '{name}'");
                        return 0;
                    }
                    Console.Error.WriteLine($"aursh: plugin '{name}' not loaded");
                    return 1;
                }

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

    private static int ExecuteUpdate()
    {
        string? sourceDir = null;
        string currentDir = AppContext.BaseDirectory;
        string? dir = currentDir;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
            {
                sourceDir = dir;
                break;
            }
            string? parent = Directory.GetParent(dir)?.FullName;
            if (parent == dir) break;
            dir = parent;
        }

        if (string.IsNullOrEmpty(sourceDir))
        {
            Console.Error.WriteLine("aursh: aursh-update: could not find git repository. Ensure aursh is running from a cloned directory.");
            return 1;
        }

        Console.WriteLine($"Updating AurShell from {sourceDir}...");

        string aurshPath = Platform.FindExecutableInPath("aursh") ?? Path.Combine(AppContext.BaseDirectory, Platform.ExecutableExtension == ".exe" ? "aursh.exe" : "aursh");
        if (!File.Exists(aurshPath))
        {
            aurshPath = Path.Combine(AppContext.BaseDirectory, "aursh" + Platform.ExecutableExtension);
        }

        var gitPsi = new System.Diagnostics.ProcessStartInfo("git", "pull origin main")
        {
            WorkingDirectory = sourceDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var gitProc = System.Diagnostics.Process.Start(gitPsi);
            if (gitProc != null)
            {
                string gitOut = gitProc.StandardOutput.ReadToEnd();
                string gitErr = gitProc.StandardError.ReadToEnd();
                gitProc.WaitForExit();
                if (!string.IsNullOrEmpty(gitOut)) Console.WriteLine(gitOut.Trim());
                if (!string.IsNullOrEmpty(gitErr)) Console.Error.WriteLine(gitErr.Trim());
                if (gitProc.ExitCode != 0)
                {
                    var gitPsiMaster = new System.Diagnostics.ProcessStartInfo("git", "pull origin master")
                    {
                        WorkingDirectory = sourceDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var gitProcMaster = System.Diagnostics.Process.Start(gitPsiMaster);
                    if (gitProcMaster != null)
                    {
                        string gitOutMaster = gitProcMaster.StandardOutput.ReadToEnd();
                        string gitErrMaster = gitProcMaster.StandardError.ReadToEnd();
                        gitProcMaster.WaitForExit();
                        if (!string.IsNullOrEmpty(gitOutMaster)) Console.WriteLine(gitOutMaster.Trim());
                        if (!string.IsNullOrEmpty(gitErrMaster)) Console.Error.WriteLine(gitErrMaster.Trim());
                        if (gitProcMaster.ExitCode != 0)
                        {
                            Console.Error.WriteLine("aursh: aursh-update: git pull failed.");
                            return 1;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-update: {ex.Message}");
            return 1;
        }

        Console.WriteLine("Building AurShell...");
        bool useMake = File.Exists(Path.Combine(sourceDir, "Makefile"));
        var buildPsi = new System.Diagnostics.ProcessStartInfo(
            useMake ? "make" : "dotnet",
            useMake ? "build" : $"build \"{Path.Combine(sourceDir, "src", "AurShell.csproj")}\" -c Release")
        {
            WorkingDirectory = sourceDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var buildProc = System.Diagnostics.Process.Start(buildPsi);
            if (buildProc != null)
            {
                string buildOut = buildProc.StandardOutput.ReadToEnd();
                string buildErr = buildProc.StandardError.ReadToEnd();
                buildProc.WaitForExit();
                if (!string.IsNullOrEmpty(buildOut)) Console.WriteLine(buildOut.Trim());
                if (!string.IsNullOrEmpty(buildErr)) Console.Error.WriteLine(buildErr.Trim());
                if (buildProc.ExitCode != 0)
                {
                    Console.Error.WriteLine("aursh: aursh-update: build failed.");
                    return 1;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-update: {ex.Message}");
            return 1;
        }

        Console.WriteLine("Restarting AurShell...");
        try
        {
            var restartPsi = new System.Diagnostics.ProcessStartInfo(aurshPath)
            {
                UseShellExecute = false
            };
            System.Diagnostics.Process.Start(restartPsi);
            System.Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-update: failed to restart: {ex.Message}");
            return 1;
        }

        return 0;
    }
}
