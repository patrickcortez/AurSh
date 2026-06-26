using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AurShell.Core;
using AurShell.Utils;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshCatCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env, ref string workingDirectory)
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
                    var outStream = AstEvaluator.OutStream ?? Console.OpenStandardOutput();
                    stream.CopyTo(outStream);
                    outStream.Flush();
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
}
