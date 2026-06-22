using System.Diagnostics;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using AurShell.Utils;

namespace AurShell.Core;

public static class BuiltinCommands
{

    private readonly static string version = "3.0";
    private static readonly HashSet<string> Builtins = new(StringComparer.OrdinalIgnoreCase)
    {
        "cd", "export", "unset", "exit", "history", "echo",
        "pwd", "type", "alias", "unalias", "source", "set", "env",
        "true", "false", "shift", "read", "test", "return", "aursh-context",
        "jobs", "fg", "kill", "aursh-plugin", "aursh-assoc", "aursh-reload", "aursh-history","aursh-about","aursh-ls","aursh-cat", "aursh-update", "aursh-net", "aursh-view", "aursh-music", "aursh-ssh", "local", "declare", "readonly", "help", "grm", "[", "[[", "clear"
    };

    public static bool IsBuiltin(string name) => Builtins.Contains(name);

    public static void WriteOut(string text, bool newline = true)
    {
        var stream = AstEvaluator.OutStream;
        if (stream != null)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text + (newline ? "\n" : ""));
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
        else
        {
            if (newline) Console.WriteLine(text);
            else Console.Write(text);
        }
    }

    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        return cmd.Name.ToLowerInvariant() switch
        {
            "clear" => ExecuteClear(),
            "cd" => ExecuteCd(cmd, env, ref workingDirectory),
            "export" => ExecuteExport(cmd, env),
            "local" => ExecuteLocal(cmd, env),
            "declare" => ExecuteDeclare(cmd, env),
            "readonly" => ExecuteReadonly(cmd, env),
            "unset" => ExecuteUnset(cmd, env),
            "exit" => ExecuteExit(cmd),
            "history" or "aursh-history" => ExecuteHistory(cmd, env, workingDirectory),
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
            "test" or "[" or "[[" => ExecuteTest(cmd),
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
            "aursh-update" => ExecuteUpdate(cmd),
            "aursh-context" => ExecuteContext(cmd),
            "aursh-net" => AurshNetCommand.Execute(cmd, env, ref workingDirectory),
            "aursh-view" => ExecuteAurshView(cmd, env, workingDirectory),
            "aursh-music" => ExecuteAurshMusic(cmd),
            "aursh-ssh" => ExecuteSsh(cmd, env, workingDirectory),
            "help" => ExecuteHelp(),
            "grm" => AurShell.Grm.GrmController.Execute(cmd, env, ref workingDirectory),
            _ => ExecuteFallback(cmd)
        };
    }

    private static string? FindAurshContextExecutable()
    {
        string exeName = Platform.CurrentOS == OperatingSystemType.Windows
            ? "aursh-context.exe"
            : "aursh-context";

        string baseDir = AppContext.BaseDirectory;
        string adjacent = Path.Combine(baseDir, exeName);
        if (File.Exists(adjacent))
            return adjacent;

        string? dir = baseDir;
        for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, "bin", exeName);
            if (File.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        string? onPath = Utils.Platform.FindExecutableInPath(
            Platform.CurrentOS == OperatingSystemType.Windows ? "aursh-context.exe" : "aursh-context");
        if (!string.IsNullOrEmpty(onPath))
            return onPath;

        return null;
    }

    private static int ExecuteAurshMusic(SimpleCommandNode cmd)
    {
        if (cmd.Args.Count == 0 || cmd.Args[0] != "start")
        {
            Console.Error.WriteLine("aursh-music: usage: aursh-music start");
            return 1;
        }

        try
        {
            AurShell.Music.MusicServer.Start(new string[0]);
            return 0;
        }
        catch (System.Exception ex)
        {
            Console.Error.WriteLine($"aursh-music: error: {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteHelp()
    {
        Console.WriteLine($"AurShell Help");
        Console.WriteLine($"Version {version}");
        Console.WriteLine();
        Console.WriteLine("These shell commands are defined internally. Type `help` to see this list.");
        Console.WriteLine("External commands are resolved from PATH. Core UNIX utilities are provided by the bundled BusyBox toolkit.");
        Console.WriteLine();

        var sortedBuiltins = Builtins.OrderBy(b => b).ToList();

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

    private static int ExecuteAurshView(SimpleCommandNode cmd, ShellEnvironment env, string workingDirectory)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: aursh-view: missing file operand");
            return 1;
        }

        string targetFile = Utils.FileSystem.ResolvePath(cmd.Args[0], workingDirectory);
        if (!System.IO.File.Exists(targetFile))
        {
            Console.Error.WriteLine($"aursh: aursh-view: cannot access '{targetFile}': No such file or directory");
            return 1;
        }

        string ext = System.IO.Path.GetExtension(targetFile).ToLowerInvariant();
        if (ext == ".md")
        {
            try
            {
                int windowWidth = 800;
                int windowHeight = 600;

                AurShell.Graphics.Compositor compositor = new AurShell.Graphics.Compositor(windowWidth, windowHeight);
                compositor.BackgroundColor = new AurShell.Graphics.Color32(255, 30, 30, 30);

                var scrollView = new AurShell.Graphics.UI.ScrollViewerElement { X = 0, Y = 0, Width = windowWidth, Height = windowHeight, ZIndex = 0 };
                var mdElem = new AurShell.Graphics.UI.MarkdownElement { X = 15, Y = 10, Width = windowWidth - 30, ZIndex = 0 };
                mdElem.BasePath = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(targetFile)) ?? System.Environment.CurrentDirectory;
                mdElem.MarkdownText = System.IO.File.ReadAllText(targetFile);
                scrollView.Content.Children.Add(mdElem);
                compositor.AddElement(scrollView);

                using (var host = new AurShell.Graphics.SdlWindowHost(windowWidth, windowHeight, $"Markdown Viewer - {System.IO.Path.GetFileName(targetFile)}"))
                {
                    host.Show(compositor);
                }

                return 0;
            }
            catch (System.Exception ex)
            {
                Console.Error.WriteLine($"aursh: aursh-view: Error rendering markdown viewer - {ex.Message}");
                return 1;
            }
        }

        AurShell.Graphics.VirtualScreen imageBuffer;
        try
        {
            imageBuffer = ext switch
            {
                ".bmp" => AurShell.Graphics.BmpDecoder.Decode(targetFile),
                ".jpg" or ".jpeg" => AurShell.Graphics.JpgDecoder.Decode(targetFile),
                ".svg" => AurShell.Graphics.SvgDecoder.Decode(targetFile),
                _ => AurShell.Graphics.PngDecoder.Decode(targetFile) // default to png
            };
        }
        catch (System.Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-view: Error decoding image '{System.IO.Path.GetFileName(targetFile)}' - {ex.Message}");
            return 1;
        }

        try
        {
            int windowWidth = imageBuffer.Width + 40;
            int windowHeight = imageBuffer.Height + 80;

            AurShell.Graphics.Compositor compositor = new AurShell.Graphics.Compositor(windowWidth, windowHeight);
            compositor.BackgroundColor = new AurShell.Graphics.Color32(255, 30, 30, 30);

            AurShell.Graphics.WindowElement win = new AurShell.Graphics.WindowElement
            {
                X = 10,
                Y = 10,
                Width = imageBuffer.Width + 20,
                Height = imageBuffer.Height + 50,
                ZIndex = 1,
                Title = $"Image Viewer - {System.IO.Path.GetFileName(targetFile)}"
            };

            AurShell.Graphics.ImageElement img = new AurShell.Graphics.ImageElement
            {
                X = 20,
                Y = 40,
                ZIndex = 2,
                Image = imageBuffer
            };

            compositor.AddElement(win);
            compositor.AddElement(img);

            using (var host = new AurShell.Graphics.SdlWindowHost(windowWidth, windowHeight, "AurSh Native Window Viewer"))
            {
                host.Show(compositor);
            }

            return 0;
        }
        catch (System.Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-view: Error rendering image viewer - {ex.Message}");
            return 1;
        }
    }

    private static int ExecuteContext(SimpleCommandNode cmd)
    {
        string? contextPath = FindAurshContextExecutable();
        if (string.IsNullOrEmpty(contextPath))
        {
            Console.Error.WriteLine("aursh: aursh-context: executable not found.");
            return 127;
        }

        var psi = new System.Diagnostics.ProcessStartInfo(contextPath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in cmd.Args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return 127;

            var outTask = System.Threading.Tasks.Task.Run(() =>
            {
                string? line;
                while ((line = proc.StandardOutput.ReadLine()) != null)
                    Console.WriteLine(line);
            });
            var errTask = System.Threading.Tasks.Task.Run(() =>
            {
                string? line;
                while ((line = proc.StandardError.ReadLine()) != null)
                    Console.Error.WriteLine(line);
            });

            proc.WaitForExit();
            try { outTask.Wait(System.TimeSpan.FromSeconds(2)); } catch { }
            try { errTask.Wait(System.TimeSpan.FromSeconds(2)); } catch { }
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-context: {ex.Message}");
            return 127;
        }
    }

    private static int ExecuteCat(SimpleCommandNode cmd, ShellEnvironment env, ref string workingDirectory)
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

        bool insertMode = false;
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
                    string modeStr = insertMode ? "-- INSERT --" : "-- NORMAL --";
                    string info = $"{modeStr} | Ln {cursorRow + 1}, Col {cursorCol + 1} | Press ':' for commands";
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

                if (insertMode && key.Key == ConsoleKey.Escape)
                {
                    insertMode = false;
                    continue;
                }

                if (!insertMode)
                {
                    if (key.KeyChar == ':')
                    {
                        commandMode = true;
                        commandInput = "";
                        continue;
                    }
                    else if (key.KeyChar == 'i' || key.KeyChar == 'I')
                    {
                        insertMode = true;
                        continue;
                    }
                    else if (key.KeyChar == 'a' || key.KeyChar == 'A')
                    {
                        insertMode = true;
                        if (cursorCol < buffer[cursorRow].Length) cursorCol++;
                        continue;
                    }
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
                else if (insertMode)
                {
                    if (key.Key == ConsoleKey.Backspace)
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
                else if (!insertMode)
                {
                    if (key.Key == ConsoleKey.Delete || key.KeyChar == 'x')
                    {
                        if (cursorCol < buffer[cursorRow].Length)
                        {
                            buffer[cursorRow].Remove(cursorCol, 1);
                        }
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
        }
        else if (OperatingSystem.IsLinux())
        {
            os = "Linux";
        }
        else if (OperatingSystem.IsAndroid())
        {
            os = "Android";
        }
        else if (OperatingSystem.IsMacOS())
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

    private static int ExecuteClear()
    {
        Console.Clear();
        return 0;
    }

    private static int ExecuteAbout(SimpleCommandNode cmd)
    {

        string about = $@"
        {Ansi.FgBrightCyan}-------------------------------------------------------------------------------------------------------
        
                        {Ansi.FgBrightBlue}                 About:
                           {Ansi.FgBrightBlue} - This frontend shell is developed in C# by {Ansi.FgBrightCyan}Tezzz{Ansi.FgBrightBlue}.
                           {Ansi.FgBrightBlue} As a cross platform shell with a purpose to make the command-line
                          {Ansi.FgBrightBlue}  look aesthetically pleasing while working. This Shell is under the license of
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
                                - {Ansi.FgBrightCyan}aursh-update : {Ansi.FgBrightBlue}Updates the shell from the remote repository.
                                - {Ansi.FgBrightCyan}aursh-context : {Ansi.FgBrightBlue}Create, Modify or Delete Contexts.
                                - {Ansi.FgBrightCyan}aursh-net : {Ansi.FgBrightBlue}A network tool for connecting,disconnecting and recieving/sending data through the command-line.
                                - {Ansi.FgBrightCyan}aursh-ssh : {Ansi.FgBrightBlue}TUI interface for managing SSH keys and remote hosts.
                                - {Ansi.FgBrightCyan}grm : {Ansi.FgBrightBlue}Git Repo Manager for installing repositories from GitHub.
                                - {Ansi.FgBrightCyan}aursh-music : {Ansi.FgBrightBlue}A websever music player, available at http://127.0.0.1:7007.

       {Ansi.FgBrightCyan} -------------------------------------------------------------------------------------------------------
        ";

        Console.WriteLine(about);

        return 0;
    }

    private static int ExecuteCd(SimpleCommandNode cmd, ShellEnvironment env, ref string workingDirectory)
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

    private static int ExecuteLs(SimpleCommandNode cmd, ShellEnvironment env, ref string workingDirectory)
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

    private static int ExecuteExport(SimpleCommandNode cmd, ShellEnvironment env)
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

    private static int ExecuteUnset(SimpleCommandNode cmd, ShellEnvironment env)
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

    private static int ExecuteExit(SimpleCommandNode cmd)
    {
        int code = 0;
        if (cmd.Args.Count > 0)
            int.TryParse(cmd.Args[0], out code);

        System.Environment.Exit(code);
        return code;
    }

    private static int ExecuteHistory(SimpleCommandNode cmd, ShellEnvironment env, string workingDirectory)
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

    private static int ExecuteEcho(SimpleCommandNode cmd)
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
            WriteOut(output, false);
        else
            WriteOut(output, true);

        return 0;
    }

    private static int ExecutePwd(string workingDirectory)
    {
        WriteOut(workingDirectory);
        return 0;
    }

    private static int ExecuteType(SimpleCommandNode cmd, ShellEnvironment env, string workingDirectory)
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
                WriteOut($"{name} is a shell builtin");
            }
            else if (env.GetAlias(name) != null)
            {
                WriteOut($"{name} is aliased to '{env.GetAlias(name)}'");
            }
            else
            {
                string? path = Pipeline.ResolveCommand(name, workingDirectory);
                if (path != null)
                {
                    WriteOut($"{name} is {path}");
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

    private static int ExecuteAlias(SimpleCommandNode cmd, ShellEnvironment env)
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

    private static int ExecuteUnalias(SimpleCommandNode cmd, ShellEnvironment env)
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

    private static int ExecuteSource(SimpleCommandNode cmd, ShellEnvironment env, ref string workingDirectory)
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

        var oldPositional = new List<string>(env.PositionalArguments);
        string[] scriptArgs = cmd.Args.Count > 1 ? cmd.Args.Skip(1).ToArray() : Array.Empty<string>();
        try
        {
            if (scriptArgs.Length > 0)
            {
                env.PositionalArguments.Clear();
                env.PositionalArguments.AddRange(scriptArgs);
            }

            string content = File.ReadAllText(filePath);
            var executor = new Executor(env, workingDirectory);
            int result = executor.ExecuteScript(content);
            workingDirectory = executor.WorkingDirectory;
            return result;
        }
        finally
        {
            if (scriptArgs.Length > 0)
            {
                env.PositionalArguments.Clear();
                env.PositionalArguments.AddRange(oldPositional);
            }
        }
    }

    private static int ExecuteSet(SimpleCommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            foreach (var kv in env.Variables.OrderBy(k => k.Key))
                Console.WriteLine($"{kv.Key}={kv.Value}");
            return 0;
        }

        if (cmd.Args[0] == "--")
        {
            env.PositionalArguments.Clear();
            env.PositionalArguments.AddRange(cmd.Args.Skip(1));
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

    private static int ExecuteRead(SimpleCommandNode cmd, ShellEnvironment env)
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

    private static int ExecuteTest(SimpleCommandNode cmd)
    {
        var args = cmd.Args.ToList();
        if (cmd.Name == "[" && args.Count > 0 && args.Last() == "]")
        {
            args.RemoveAt(args.Count - 1);
        }
        else if (cmd.Name == "[[" && args.Count > 0 && args.Last() == "]]")
        {
            args.RemoveAt(args.Count - 1);
        }

        if (args.Count == 0)
            return 1;

        if (args.Count == 1)
            return string.IsNullOrEmpty(args[0]) ? 1 : 0;

        if (args.Count == 2)
        {
            string op = args[0];
            string operand = args[1];

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

        if (args.Count == 3)
        {
            string left = args[0];
            string op = args[1];
            string right = args[2];

            return op switch
            {
                "=" or "==" => cmd.Name == "[[" ? (System.Text.RegularExpressions.Regex.IsMatch(left, WordExpander.GlobSegmentToRegex(right)) ? 0 : 1) : (left == right ? 0 : 1),
                "!=" => cmd.Name == "[[" ? (!System.Text.RegularExpressions.Regex.IsMatch(left, WordExpander.GlobSegmentToRegex(right)) ? 0 : 1) : (left != right ? 0 : 1),
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

    private static int ExecuteReturn(SimpleCommandNode cmd, ShellEnvironment env)
    {
        int code = 0;
        if (cmd.Args.Count > 0)
            int.TryParse(cmd.Args[0], out code);
        env.LastExitCode = code;
        return code;
    }

    private static int ExecuteFallback(SimpleCommandNode cmd)
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

    private static int ExecuteJobs(SimpleCommandNode cmd, ShellEnvironment env)
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

    private static int ExecuteFg(SimpleCommandNode cmd, ShellEnvironment env)
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

    private static int ExecuteKill(SimpleCommandNode cmd, ShellEnvironment env)
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

    private static int ExecuteAurshPlugin(SimpleCommandNode cmd, ShellEnvironment env, string workingDirectory)
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

    private static int ExecuteAssoc(SimpleCommandNode cmd, ShellEnvironment env)
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

    // Update logic lives in a separate aursh-update executable so the user
    // can `sudo aursh-update` without having to elevate the entire shell.
    // The ExecuteUpdate builtin below just shells out to that binary.

    private static string? FindAurshUpdateExecutable()
    {
        string exeName = Platform.CurrentOS == OperatingSystemType.Windows
            ? "aursh-update.exe"
            : "aursh-update";

        // 1. Look next to the running aursh binary first (install-time layout).
        string baseDir = AppContext.BaseDirectory;
        string adjacent = Path.Combine(baseDir, exeName);
        if (File.Exists(adjacent))
            return adjacent;

        // 2. Walk up looking for a bin/ directory (developer layout: repo/bin/).
        string? dir = baseDir;
        for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, "bin", exeName);
            if (File.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        // 3. Fall back to PATH.
        string? onPath = Utils.Platform.FindExecutableInPath(
            Platform.CurrentOS == OperatingSystemType.Windows ? "aursh-update.exe" : "aursh-update");
        if (!string.IsNullOrEmpty(onPath))
            return onPath;

        return null;
    }

    // Stored in <ConfigDirectory>/update_configs.txt as one `key=value,`
    // pair per line. The schema is shared with the standalone aursh-update
    // binary so both readers agree on the same file.
    //
    // Recognized keys: `path` (repo checkout location), `branch` (which
    // branch check/update/pull should track). The legacy `update-repo`
    // flat-text file is still read for backward compatibility.
    private static string UpdateConfigPath => Path.Combine(Platform.ConfigDirectory, "update_configs.txt");
    private static string LegacyUpdateRepoPath => Path.Combine(Platform.ConfigDirectory, "update-repo");

    private static string? GetUpdateRepoPath()
    {
        if (File.Exists(UpdateConfigPath))
        {
            string? p = ReadUpdateConfigField("path");
            if (!string.IsNullOrEmpty(p)) return p;
        }
        if (File.Exists(LegacyUpdateRepoPath))
        {
            string content = File.ReadAllText(LegacyUpdateRepoPath).Trim();
            if (!string.IsNullOrEmpty(content)) return content;
        }
        return null;
    }

    private static void SetUpdateRepoPath(string path)
    {
        Directory.CreateDirectory(Platform.ConfigDirectory);
        WriteUpdateConfigField("path", path);
    }

    private static string? GetUpdateBranch()
    {
        if (!File.Exists(UpdateConfigPath)) return null;
        return ReadUpdateConfigField("branch");
    }

    private static void SetUpdateBranch(string branch)
    {
        Directory.CreateDirectory(Platform.ConfigDirectory);
        WriteUpdateConfigField("branch", branch);
    }

    /// <summary>
    /// Resolves the branch to use for git operations in the configured repo.
    /// Prefers <c>branch=</c> from update_configs.txt; falls back to the
    /// repo's currently checked-out HEAD; finally to <c>main</c>.
    /// </summary>
    private static string ResolveUpdateBranch(string repoPath)
    {
        string? stored = GetUpdateBranch();
        if (!string.IsNullOrEmpty(stored)) return stored!;

        string head = RunGitOutput(repoPath, "rev-parse --abbrev-ref HEAD").Trim();
        if (!string.IsNullOrEmpty(head) && head != "HEAD") return head;

        return "main";
    }

    private static string? ReadUpdateConfigField(string key)
    {
        if (!File.Exists(UpdateConfigPath)) return null;
        string content;
        try { content = File.ReadAllText(UpdateConfigPath); }
        catch { return null; }

        foreach (string raw in content.Split(','))
        {
            string entry = raw.Trim();
            if (string.IsNullOrEmpty(entry)) continue;
            int eq = entry.IndexOf('=');
            if (eq <= 0) continue;
            string k = entry.Substring(0, eq).Trim();
            string v = entry.Substring(eq + 1).Trim();
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrEmpty(v) ? null : v;
        }
        return null;
    }

    private static void WriteUpdateConfigField(string key, string value)
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(UpdateConfigPath))
        {
            try
            {
                foreach (string raw in File.ReadAllText(UpdateConfigPath).Split(','))
                {
                    string e = raw.Trim();
                    if (string.IsNullOrEmpty(e)) continue;
                    int eq = e.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = e.Substring(0, eq).Trim();
                    string v = e.Substring(eq + 1).Trim();
                    if (!string.IsNullOrEmpty(k)) entries[k] = v;
                }
            }
            catch { /* overwrite unreadable file */ }
        }

        entries[key] = value;

        // Write one `key=value,` per line. `path` always appears first,
        // followed by `branch` directly below it; future keys append in
        // insertion order so the file stays diffable.
        var ordered = new List<KeyValuePair<string, string>>();
        if (entries.TryGetValue("path", out string? p)) ordered.Add(new("path", p));
        if (entries.TryGetValue("branch", out string? b)) ordered.Add(new("branch", b));
        foreach (var kv in entries)
        {
            if (kv.Key.Equals("path", StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Key.Equals("branch", StringComparison.OrdinalIgnoreCase)) continue;
            ordered.Add(new(kv.Key, kv.Value));
        }

        var sb = new System.Text.StringBuilder();
        foreach (var kv in ordered)
        {
            sb.Append(kv.Key);
            sb.Append('=');
            sb.Append(kv.Value);
            sb.Append(',');
            sb.Append('\n');
        }
        File.WriteAllText(UpdateConfigPath, sb.ToString());
    }

    private static bool ValidateRepo(string path)
    {
        string gitDir = Path.Combine(path, ".git");
        if (!Directory.Exists(gitDir))
        {
            Console.Error.WriteLine($"aursh: aursh-update: '{path}' is not a git repository.");
            return false;
        }

        var psi = new System.Diagnostics.ProcessStartInfo("git", "remote -v")
        {
            WorkingDirectory = path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return false;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0) return false;

            string expected = "https://github.com/patrickcortez/AurSh.git";
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                int urlStart = trimmed.IndexOf('\t');
                if (urlStart < 0) continue;
                string urlPart = trimmed.Substring(urlStart + 1).Trim();
                int spaceIdx = urlPart.IndexOf(' ');
                if (spaceIdx >= 0) urlPart = urlPart.Substring(0, spaceIdx);
                string normalized = urlPart.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                    ? urlPart.Substring(0, urlPart.Length - 4)
                    : urlPart;
                if (normalized.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("https://github.com/patrickcortez/AurSh", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch { }

        Console.Error.WriteLine("aursh: aursh-update: repository does not have the expected remote (https://github.com/patrickcortez/AurSh.git).");
        return false;
    }

    private static string RunGitOutput(string workingDir, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return "";
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            if (proc.ExitCode != 0) return "";
            return output;
        }
        catch { return ""; }
    }

    /// <summary>
    /// Run git with stdout/stderr inherited from the current process so the
    /// user sees progress / conflict messages directly. Returns the exit
    /// code (or 127 if the binary could not be launched).
    /// </summary>
    private static int RunGitForeground(string workingDir, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return 127;
            proc.WaitForExit();
            return proc.ExitCode;
        }
        catch
        {
            return 127;
        }
    }

    private static int ExecuteUpdate(SimpleCommandNode cmd)
    {
        string? updaterPath = FindAurshUpdateExecutable();
        if (!string.IsNullOrEmpty(updaterPath))
        {
            var psi = new System.Diagnostics.ProcessStartInfo(updaterPath)
            {
                UseShellExecute = false,
                CreateNoWindow = false,

                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in cmd.Args)
                psi.ArgumentList.Add(arg);

            try
            {
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return 127;

                var outTask = System.Threading.Tasks.Task.Run(() =>
                {
                    string? line;
                    while ((line = proc.StandardOutput.ReadLine()) != null)
                        Console.WriteLine(line);
                });
                var errTask = System.Threading.Tasks.Task.Run(() =>
                {
                    string? line;
                    while ((line = proc.StandardError.ReadLine()) != null)
                        Console.Error.WriteLine(line);
                });

                proc.WaitForExit();
                try { outTask.Wait(System.TimeSpan.FromSeconds(2)); } catch { }
                try { errTask.Wait(System.TimeSpan.FromSeconds(2)); } catch { }
                return proc.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"aursh: aursh-update: {ex.Message}");
                return 127;
            }
        }

        if (cmd.Args.Count == 0)
            return DoUpdate();

        string sub = cmd.Args[0].ToLowerInvariant();

        if (sub == "set")
        {
            if (cmd.Args.Count < 2)
            {
                Console.Error.WriteLine("aursh: aursh-update set <path-to-repo>");
                return 1;
            }
            string path = Path.GetFullPath(cmd.Args[1]);
            if (!Directory.Exists(path))
            {
                Console.Error.WriteLine($"aursh: aursh-update: directory '{path}' not found.");
                return 1;
            }
            if (!ValidateRepo(path))
                return 1;
            SetUpdateRepoPath(path);
            Console.WriteLine($"Update repository set to: {path}");
            return 0;
        }

        if (sub == "change")
        {
            if (cmd.Args.Count < 2)
            {
                Console.Error.WriteLine("aursh: aursh-update change <branch-name>");
                return 1;
            }
            string branch = cmd.Args[1].Trim();
            if (string.IsNullOrEmpty(branch))
            {
                Console.Error.WriteLine("aursh: aursh-update: branch name is empty.");
                return 1;
            }
            if (branch.IndexOfAny(new[] { ' ', '\t', ',', '\n', '\r' }) >= 0)
            {
                Console.Error.WriteLine($"aursh: aursh-update: branch name '{branch}' contains invalid whitespace or commas.");
                return 1;
            }

            string? repoPath = GetUpdateRepoPath();
            if (string.IsNullOrEmpty(repoPath))
            {
                Console.Error.WriteLine("aursh: aursh-update: no repository set. Use 'aursh-update set <path>' first.");
                return 1;
            }
            if (!Directory.Exists(repoPath))
            {
                Console.Error.WriteLine($"aursh: aursh-update: repository directory '{repoPath}' not found.");
                return 1;
            }

            RunGitOutput(repoPath, "fetch origin");

            Console.WriteLine($"Switching {repoPath} to branch '{branch}'...");
            int rc = RunGitForeground(repoPath, $"checkout {branch}");
            if (rc != 0)
            {
                Console.Error.WriteLine($"aursh: aursh-update: git checkout {branch} failed.");
                return rc;
            }

            SetUpdateBranch(branch);
            Console.WriteLine($"Now on branch '{branch}'. Stored in update_configs.txt.");
            return 0;
        }

        if (sub == "check")
        {
            string? repoPath = GetUpdateRepoPath();
            if (string.IsNullOrEmpty(repoPath))
            {
                Console.Error.WriteLine("aursh: aursh-update: no repository set. Use 'aursh-update set <path>'.");
                return 1;
            }
            if (!Directory.Exists(repoPath))
            {
                Console.Error.WriteLine($"aursh: aursh-update: repository directory '{repoPath}' not found.");
                return 1;
            }

            RunGitOutput(repoPath, "fetch");

            string branch = ResolveUpdateBranch(repoPath);
            string behind = RunGitOutput(repoPath, $"rev-list HEAD..origin/{branch} --count");

            if (int.TryParse(behind, out int count))
            {
                if (count == 0)
                    Console.WriteLine($"AurShell is up to date (origin/{branch}).");
                else
                    Console.WriteLine($"AurShell is {count} commit(s) behind origin/{branch}.");
            }
            else
            {
                Console.Error.WriteLine($"aursh: aursh-update: failed to check remote status against origin/{branch}.");
                return 1;
            }
            return 0;
        }

        Console.Error.WriteLine($"aursh: aursh-update: unknown subcommand '{sub}'");
        return 1;
    }

    private static int DoUpdate()
    {
        string? sourceDir = GetUpdateRepoPath();
        if (string.IsNullOrEmpty(sourceDir))
        {
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
        }

        if (string.IsNullOrEmpty(sourceDir))
        {
            Console.Error.WriteLine("aursh: aursh-update: no repository set. Use 'aursh-update set <path>'.");
            return 1;
        }

        if (!Directory.Exists(sourceDir))
        {
            Console.Error.WriteLine($"aursh: aursh-update: repository directory '{sourceDir}' not found.");
            return 1;
        }

        string branch = ResolveUpdateBranch(sourceDir);
        Console.WriteLine($"Updating AurShell from {sourceDir} (branch '{branch}')...");

        var gitPsi = new System.Diagnostics.ProcessStartInfo("git", $"pull origin {branch}")
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
                    Console.Error.WriteLine($"aursh: aursh-update: git pull origin {branch} failed.");
                    return 1;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-update: {ex.Message}");
            return 1;
        }

        Console.WriteLine("Installing AurShell...");
        bool useMake = File.Exists(Path.Combine(sourceDir, "Makefile"));
        var installPsi = new System.Diagnostics.ProcessStartInfo(
            useMake ? "make" : "msbuild",
            useMake ? "install" : $"\"{Path.Combine(sourceDir, "src", "AurShell.csproj")}\" /p:Configuration=Release")
        {
            WorkingDirectory = sourceDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var installProc = System.Diagnostics.Process.Start(installPsi);
            if (installProc != null)
            {
                string installOut = installProc.StandardOutput.ReadToEnd();
                string installErr = installProc.StandardError.ReadToEnd();
                installProc.WaitForExit();
                if (!string.IsNullOrEmpty(installOut)) Console.WriteLine(installOut.Trim());
                if (!string.IsNullOrEmpty(installErr)) Console.Error.WriteLine(installErr.Trim());
                if (installProc.ExitCode != 0)
                {
                    Console.Error.WriteLine("aursh: aursh-update: install failed.");
                    return 1;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-update: {ex.Message}");
            return 1;
        }

        Console.WriteLine("Update complete. Exiting shell to apply changes...");
        System.Environment.Exit(0);
        return 0;
    }

    private static int ExecuteBlackBoxDemo(SimpleCommandNode cmd)
    {
        string[] args = cmd.Args.ToArray();
        return AurShell.BlackBoxView.BlackBoxDemo.Run(args);
    }

    private static int ExecuteSsh(SimpleCommandNode cmd, ShellEnvironment env, string workingDirectory)
    {
        if (!env.SshAvailable)
        {
            Console.Error.WriteLine("aursh: aursh-ssh: ssh is not installed");
            return 127;
        }

        return SshTui.Run(workingDirectory);
    }
    private static int ExecuteLocal(SimpleCommandNode cmd, ShellEnvironment env)
    {
        foreach (string arg in cmd.Args)
        {
            int eq = arg.IndexOf('=');
            if (eq > 0)
            {
                string name = arg.Substring(0, eq).Trim();
                string val = arg.Substring(eq + 1).Trim();
                if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
                    val = val.Substring(1, val.Length - 2);

                env.SetLocal(name, env.Expand(val));
            }
            else
            {
                env.SetLocal(arg, "");
            }
        }
        return 0;
    }

    private static int ExecuteDeclare(SimpleCommandNode cmd, ShellEnvironment env)
    {
        bool isIndexed = false;
        bool isAssoc = false;
        bool isReadonly = false;
        int i = 0;
        while (i < cmd.Args.Count && cmd.Args[i].StartsWith("-"))
        {
            string flag = cmd.Args[i];
            if (flag.Contains("a")) isIndexed = true;
            if (flag.Contains("A")) isAssoc = true;
            if (flag.Contains("r")) isReadonly = true;
            i++;
        }

        for (; i < cmd.Args.Count; i++)
        {
            string arg = cmd.Args[i];
            int eq = arg.IndexOf('=');
            string name = eq > 0 ? arg.Substring(0, eq).Trim() : arg;
            string val = eq > 0 ? arg.Substring(eq + 1).Trim() : "";

            if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
                val = val.Substring(1, val.Length - 2);

            if (isAssoc)
            {
                if (env.GetAssocArray(name) == null)
                    env.SetAssocArray(name, new Dictionary<string, string>(StringComparer.Ordinal));
            }
            else if (isIndexed)
            {
                if (env.GetArray(name) == null)
                    env.SetArray(name, new List<string>());
            }

            if (eq > 0)
            {
                env.Set(name, env.Expand(val));
            }

            if (isReadonly)
            {
                env.MarkReadonly(name);
            }
        }
        return 0;
    }

    private static int ExecuteReadonly(SimpleCommandNode cmd, ShellEnvironment env)
    {
        foreach (string arg in cmd.Args)
        {
            int eq = arg.IndexOf('=');
            if (eq > 0)
            {
                string name = arg.Substring(0, eq).Trim();
                string val = arg.Substring(eq + 1).Trim();
                if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
                    val = val.Substring(1, val.Length - 2);

                env.Set(name, env.Expand(val));
                env.MarkReadonly(name);
            }
            else
            {
                env.MarkReadonly(arg);
            }
        }
        return 0;
    }
}

