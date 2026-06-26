using System;
using System.Linq;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshHistoryCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env, string workingDirectory)
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
}
