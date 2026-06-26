using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AurShell.Core;
using AurShell.Parser;
using AurShell.Utils;

namespace AurShell.Commands;

public static class AurshLsCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env, ref string workingDirectory)
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

}

