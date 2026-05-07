using System.Text;

namespace AurShell.Core;

public class InputHandler
{
    private readonly History _history;
    private readonly ShellEnvironment _env;
    private readonly StringBuilder _buffer = new();
    private int _cursorPos;
    private int _promptVisibleLen;
    private string _ghostText = "";
    private bool _inReverseSearch;
    private string _searchQuery = "";
    private int _searchIndex = -1;
    private int _lastTermWidth;
    private string _currentPrompt = "";

    public InputHandler(History history, ShellEnvironment env)
    {
        _history = history;
        _env = env;
    }

    public string? ReadLine(string prompt)
    {
        _currentPrompt = prompt;
        Console.Write(prompt);
        _promptVisibleLen = ComputeLastLineVisibleLength(prompt);
        _lastTermWidth = Utils.Platform.TerminalWidth;

        _buffer.Clear();
        _cursorPos = 0;
        _ghostText = "";
        _inReverseSearch = false;
        _searchQuery = "";
        _searchIndex = -1;
        _history.ResetNavigation();

        UpdateGhostText();

        while (true)
        {
            if (Console.KeyAvailable == false)
            {
                CheckTerminalResize();
                System.Threading.Thread.Sleep(10);
                if (!Console.KeyAvailable)
                    continue;
            }

            ConsoleKeyInfo key;
            try
            {
                key = Console.ReadKey(true);
            }
            catch
            {
                return null;
            }

            if (_inReverseSearch)
            {
                string? searchResult = HandleReverseSearchKey(key);
                if (searchResult != null)
                    return searchResult;
                continue;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                if (Console.KeyAvailable)
                {
                    _buffer.Insert(_cursorPos, '\n');
                    _cursorPos++;
                    
                    while (Console.KeyAvailable)
                    {
                        var nextKey = Console.ReadKey(true);
                        if (nextKey.Key == ConsoleKey.Enter)
                        {
                            _buffer.Insert(_cursorPos, '\n');
                            _cursorPos++;
                        }
                        else if (!char.IsControl(nextKey.KeyChar))
                        {
                            _buffer.Insert(_cursorPos, nextKey.KeyChar);
                            _cursorPos++;
                        }
                    }
                    ClearGhostText();
                    Console.WriteLine();
                    
                    string pasted = _buffer.ToString();
                    int firstNewline = pasted.IndexOf('\n');
                    if (firstNewline >= 0 && firstNewline < pasted.Length - 1)
                    {
                        Console.WriteLine(pasted.Substring(firstNewline + 1));
                    }
                    return pasted;
                }

                ClearGhostText();
                Console.WriteLine();
                return _buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                HandleBackspace();
                continue;
            }

            if (key.Key == ConsoleKey.Delete)
            {
                HandleDelete();
                continue;
            }

            if (key.Key == ConsoleKey.LeftArrow)
            {
                if ((key.Modifiers & ConsoleModifiers.Control) != 0)
                    HandleCtrlLeft();
                else
                    HandleLeft();
                continue;
            }

            if (key.Key == ConsoleKey.RightArrow)
            {
                if ((key.Modifiers & ConsoleModifiers.Control) != 0)
                    HandleCtrlRight();
                else
                    HandleRight();
                continue;
            }

            if (key.Key == ConsoleKey.UpArrow)
            {
                HandleUp();
                continue;
            }

            if (key.Key == ConsoleKey.DownArrow)
            {
                HandleDown();
                continue;
            }

            if (key.Key == ConsoleKey.Home)
            {
                HandleHome();
                continue;
            }

            if (key.Key == ConsoleKey.End)
            {
                HandleEnd();
                continue;
            }

            if (key.Key == ConsoleKey.Tab)
            {
                HandleTab();
                continue;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                HandleEscape();
                continue;
            }

            if ((key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                switch (key.Key)
                {
                    case ConsoleKey.A:
                        HandleHome();
                        continue;
                    case ConsoleKey.E:
                        HandleEnd();
                        continue;
                    case ConsoleKey.K:
                        HandleCtrlK();
                        continue;
                    case ConsoleKey.U:
                        HandleCtrlU();
                        continue;
                    case ConsoleKey.W:
                        HandleCtrlW();
                        continue;
                    case ConsoleKey.L:
                        HandleCtrlL();
                        continue;
                    case ConsoleKey.C:
                        HandleInterrupt();
                        return "";
                    case ConsoleKey.D:
                        if (_buffer.Length == 0)
                        {
                            Console.WriteLine();
                            return null;
                        }
                        HandleDelete();
                        continue;
                    case ConsoleKey.R:
                        EnterReverseSearch();
                        continue;
                }
            }

            if (!char.IsControl(key.KeyChar))
            {
                HandleCharacter(key.KeyChar);
            }
        }
    }

    private void HandleInterrupt()
    {
        _ghostText = "";
        Console.Write(Utils.Ansi.Reset);
        Console.Write(Utils.Ansi.ClearLineFromCursor);
        Console.WriteLine("^C");
        _buffer.Clear();
        _cursorPos = 0;
    }

    private void HandleCharacter(char c)
    {
        if (_cursorPos == _buffer.Length)
        {
            _buffer.Append(c);
            _cursorPos++;
        }
        else
        {
            _buffer.Insert(_cursorPos, c);
            _cursorPos++;
        }
        RedrawLine();
    }

    private void HandleBackspace()
    {
        if (_cursorPos <= 0)
            return;

        _cursorPos--;
        _buffer.Remove(_cursorPos, 1);
        RedrawLine();
    }

    private void HandleDelete()
    {
        if (_cursorPos >= _buffer.Length)
            return;

        _buffer.Remove(_cursorPos, 1);
        RedrawLine();
    }

    private void HandleLeft()
    {
        if (_cursorPos > 0)
        {
            _cursorPos--;
            Console.Write(Utils.Ansi.CursorLeft);
        }
    }

    private void HandleRight()
    {
        if (_cursorPos < _buffer.Length)
        {
            _cursorPos++;
            Console.Write(Utils.Ansi.CursorRight);
        }
        else if (!string.IsNullOrEmpty(_ghostText))
        {
            AcceptGhostText();
        }
    }

    private void HandleCtrlLeft()
    {
        if (_cursorPos <= 0)
            return;

        int newPos = _cursorPos - 1;
        while (newPos > 0 && _buffer[newPos] == ' ')
            newPos--;
        while (newPos > 0 && _buffer[newPos - 1] != ' ')
            newPos--;

        int delta = _cursorPos - newPos;
        _cursorPos = newPos;
        Console.Write(Utils.Ansi.MoveCursorLeft(delta));
    }

    private void HandleCtrlRight()
    {
        if (_cursorPos >= _buffer.Length)
        {
            if (!string.IsNullOrEmpty(_ghostText))
                AcceptGhostText();
            return;
        }

        int newPos = _cursorPos;
        while (newPos < _buffer.Length && _buffer[newPos] != ' ')
            newPos++;
        while (newPos < _buffer.Length && _buffer[newPos] == ' ')
            newPos++;

        int delta = newPos - _cursorPos;
        _cursorPos = newPos;
        Console.Write(Utils.Ansi.MoveCursorRight(delta));
    }

    private void HandleUp()
    {
        string? prev = _history.NavigateUp(_buffer.ToString());
        if (prev != null)
            ReplaceLine(prev);
    }

    private void HandleDown()
    {
        string? next = _history.NavigateDown(_buffer.ToString());
        if (next != null)
            ReplaceLine(next);
    }

    private void HandleHome()
    {
        if (_cursorPos > 0)
        {
            _cursorPos = 0;
            Console.Write(Utils.Ansi.SetCursorColumn(_promptVisibleLen + 1));
        }
    }

    private void HandleEnd()
    {
        if (_cursorPos < _buffer.Length)
        {
            int delta = _buffer.Length - _cursorPos;
            _cursorPos = _buffer.Length;
            Console.Write(Utils.Ansi.MoveCursorRight(delta));
        }
        else if (!string.IsNullOrEmpty(_ghostText))
        {
            AcceptGhostText();
        }
    }

    private void HandleTab()
    {
        if (!string.IsNullOrEmpty(_ghostText))
        {
            AcceptGhostText();
            return;
        }

        string currentWord = GetCurrentWord();
        if (string.IsNullOrEmpty(currentWord))
            return;

        var completions = GetCompletions(currentWord);

        if (completions.Count == 0)
            return;

        if (completions.Count == 1)
        {
            ReplaceCurrentWord(completions[0]);
            return;
        }

        string common = FindCommonPrefix(completions);
        if (common.Length > currentWord.Length)
        {
            ReplaceCurrentWord(common);
            return;
        }

        ClearGhostText();
        Console.WriteLine();
        int colWidth = completions.Max(c => c.Length) + 2;
        int cols = Math.Max(1, Utils.Platform.TerminalWidth / colWidth);

        for (int i = 0; i < completions.Count; i++)
        {
            Console.Write(completions[i].PadRight(colWidth));
            if ((i + 1) % cols == 0)
                Console.WriteLine();
        }
        if (completions.Count % cols != 0)
            Console.WriteLine();

        FullRedraw();
    }

    private void HandleEscape()
    {
        if (!string.IsNullOrEmpty(_ghostText))
        {
            _ghostText = "";
            RedrawLine();
        }
    }

    private void HandleCtrlK()
    {
        if (_cursorPos < _buffer.Length)
        {
            _buffer.Remove(_cursorPos, _buffer.Length - _cursorPos);
            RedrawLine();
        }
    }

    private void HandleCtrlU()
    {
        if (_cursorPos > 0)
        {
            _buffer.Remove(0, _cursorPos);
            _cursorPos = 0;
            RedrawLine();
        }
    }

    private void HandleCtrlW()
    {
        if (_cursorPos <= 0)
            return;

        int end = _cursorPos;
        int start = _cursorPos - 1;
        while (start > 0 && _buffer[start] == ' ')
            start--;
        while (start > 0 && _buffer[start - 1] != ' ')
            start--;

        _buffer.Remove(start, end - start);
        _cursorPos = start;
        RedrawLine();
    }

    private void HandleCtrlL()
    {
        Console.Write(Utils.Ansi.ClearScreen);
        Console.Write(Utils.Ansi.SetCursorPosition(1, 1));
        FullRedraw();
    }

    private void AcceptGhostText()
    {
        _buffer.Append(_ghostText);
        _cursorPos = _buffer.Length;
        _ghostText = "";
        RedrawLine();
    }

    private void ReplaceLine(string newContent)
    {
        _buffer.Clear();
        _buffer.Append(newContent);
        _cursorPos = _buffer.Length;
        RedrawLine();
    }

    private void RedrawLine()
    {
        UpdateGhostText();

        var sb = new StringBuilder();
        sb.Append(Utils.Ansi.CursorHide);
        sb.Append('\r');
        sb.Append(Utils.Ansi.SetCursorColumn(_promptVisibleLen + 1));
        sb.Append(Utils.Ansi.ClearLineFromCursor);
        sb.Append(_buffer.ToString());

        if (!string.IsNullOrEmpty(_ghostText))
        {
            sb.Append(Utils.Ansi.FgRgb(80, 80, 100));
            sb.Append(_ghostText);
            sb.Append(Utils.Ansi.Reset);
        }

        sb.Append(Utils.Ansi.ClearLineFromCursor);

        int targetCol = _promptVisibleLen + 1 + _cursorPos;
        sb.Append(Utils.Ansi.SetCursorColumn(targetCol));
        sb.Append(Utils.Ansi.CursorShow);

        Console.Write(sb.ToString());
    }

    private void ClearGhostText()
    {
        if (!string.IsNullOrEmpty(_ghostText))
        {
            _ghostText = "";
            var sb = new StringBuilder();
            sb.Append(Utils.Ansi.CursorHide);
            sb.Append(Utils.Ansi.SetCursorColumn(_promptVisibleLen + 1 + _buffer.Length));
            sb.Append(Utils.Ansi.ClearLineFromCursor);
            int targetCol = _promptVisibleLen + 1 + _cursorPos;
            sb.Append(Utils.Ansi.SetCursorColumn(targetCol));
            sb.Append(Utils.Ansi.CursorShow);
            Console.Write(sb.ToString());
        }
    }

    private void UpdateGhostText()
    {
        string current = _buffer.ToString();
        if (_cursorPos != _buffer.Length || string.IsNullOrEmpty(current))
        {
            _ghostText = "";
            return;
        }

        string? suggestion = _history.GetSuggestion(current);
        if (suggestion != null && suggestion.Length > current.Length)
            _ghostText = suggestion.Substring(current.Length);
        else
            _ghostText = "";
    }

    private void CheckTerminalResize()
    {
        int currentWidth = Utils.Platform.TerminalWidth;
        if (currentWidth != _lastTermWidth)
        {
            _lastTermWidth = currentWidth;
            FullRedraw();
        }
    }

    private void FullRedraw()
    {
        var prompt = new Prompt(_env);
        string cwd = _env.Get("PWD") ?? Directory.GetCurrentDirectory();
        int exitCode = _env.LastExitCode;
        _currentPrompt = prompt.Render(cwd, exitCode);
        _promptVisibleLen = prompt.PromptVisibleLength(exitCode);

        var sb = new StringBuilder();
        sb.Append(_currentPrompt);
        sb.Append(_buffer.ToString());

        if (!string.IsNullOrEmpty(_ghostText))
        {
            sb.Append(Utils.Ansi.FgRgb(80, 80, 100));
            sb.Append(_ghostText);
            sb.Append(Utils.Ansi.Reset);
        }

        sb.Append(Utils.Ansi.ClearLineFromCursor);

        int targetCol = _promptVisibleLen + 1 + _cursorPos;
        sb.Append(Utils.Ansi.SetCursorColumn(targetCol));

        Console.Write(sb.ToString());
    }

    private void EnterReverseSearch()
    {
        _inReverseSearch = true;
        _searchQuery = "";
        _searchIndex = -1;
        RedrawSearchPrompt();
    }

    private string? HandleReverseSearchKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            _inReverseSearch = false;
            Console.Write(Utils.Ansi.ClearLine);
            Console.Write('\r');
            FullRedraw();
            return null;
        }

        if (key.Key == ConsoleKey.Escape || (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0))
        {
            _inReverseSearch = false;
            _buffer.Clear();
            _cursorPos = 0;
            Console.Write(Utils.Ansi.ClearLine);
            Console.Write('\r');
            FullRedraw();
            return null;
        }

        if (key.Key == ConsoleKey.R && (key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            if (_searchIndex > 0)
            {
                int newIdx = _history.ReverseSearchIndex(_searchQuery, _searchIndex - 1);
                if (newIdx >= 0)
                {
                    _searchIndex = newIdx;
                    _buffer.Clear();
                    _buffer.Append(_history.Entries[_searchIndex]);
                    _cursorPos = _buffer.Length;
                }
            }
            RedrawSearchPrompt();
            return null;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (_searchQuery.Length > 0)
            {
                _searchQuery = _searchQuery.Substring(0, _searchQuery.Length - 1);
                PerformSearch();
            }
            RedrawSearchPrompt();
            return null;
        }

        if (!char.IsControl(key.KeyChar))
        {
            _searchQuery += key.KeyChar;
            PerformSearch();
            RedrawSearchPrompt();
        }

        return null;
    }

    private void PerformSearch()
    {
        int idx = _history.ReverseSearchIndex(_searchQuery);
        if (idx >= 0)
        {
            _searchIndex = idx;
            _buffer.Clear();
            _buffer.Append(_history.Entries[idx]);
            _cursorPos = _buffer.Length;
        }
    }

    private void RedrawSearchPrompt()
    {
        var sb = new StringBuilder();
        sb.Append(Utils.Ansi.CursorHide);
        sb.Append('\r');
        sb.Append(Utils.Ansi.ClearLine);
        sb.Append(Utils.Ansi.FgRgb(255, 200, 100));
        sb.Append("(reverse-i-search)`");
        sb.Append(_searchQuery);
        sb.Append("': ");
        sb.Append(Utils.Ansi.Reset);
        sb.Append(_buffer.ToString());
        sb.Append(Utils.Ansi.CursorShow);
        Console.Write(sb.ToString());
    }

    private string GetCurrentWord()
    {
        int end = _cursorPos;
        int start = _cursorPos;

        while (start > 0 && _buffer[start - 1] != ' ')
            start--;

        return _buffer.ToString().Substring(start, end - start);
    }

    private void ReplaceCurrentWord(string replacement)
    {
        int end = _cursorPos;
        int start = _cursorPos;

        while (start > 0 && _buffer[start - 1] != ' ')
            start--;

        _buffer.Remove(start, end - start);
        _buffer.Insert(start, replacement);
        _cursorPos = start + replacement.Length;
        RedrawLine();
    }

    private List<string> GetCompletions(string partial)
    {
        var completions = new List<string>();

        bool isCommand = true;
        for (int i = 0; i < _cursorPos; i++)
        {
            if (_buffer[i] == ' ')
            {
                isCommand = false;
                break;
            }
        }

        if (isCommand)
        {
            foreach (string builtin in new[]
            {
                "cd", "export", "unset", "exit", "history", "clear", "echo",
                "pwd", "type", "alias", "unalias", "source", "set", "env",
                "true", "false", "read", "test", "return",
                "jobs", "fg", "kill", "aursh-plugin"
            })
            {
                if (builtin.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    completions.Add(builtin);
            }

            foreach (string alias in _env.Aliases.Keys)
            {
                if (alias.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    completions.Add(alias);
            }

            string? pathEnv = System.Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string[] dirs = pathEnv.Split(Utils.Platform.PathListSeparator, StringSplitOptions.RemoveEmptyEntries);
                foreach (string dir in dirs)
                {
                    try
                    {
                        if (!Directory.Exists(dir))
                            continue;
                        foreach (string file in Directory.GetFiles(dir))
                        {
                            string name = Path.GetFileNameWithoutExtension(file);
                            string ext = Path.GetExtension(file).ToLowerInvariant();

                            bool isExe = Utils.Platform.IsUnixLike
                                ? !file.Contains('.')
                                : Utils.Platform.ExecutableExtensions.Contains(ext);

                            if (isExe && name.StartsWith(partial, StringComparison.OrdinalIgnoreCase) && seen.Add(name))
                                completions.Add(name);
                        }
                    }
                    catch { }
                }
            }
        }
        else
        {
            string expanded = Utils.Platform.ExpandTilde(partial);
            string? dir = Path.GetDirectoryName(expanded);
            string prefix = Path.GetFileName(expanded);

            if (string.IsNullOrEmpty(dir))
                dir = ".";

            try
            {
                if (Directory.Exists(dir))
                {
                    foreach (string entry in Directory.GetFileSystemEntries(dir))
                    {
                        string name = Path.GetFileName(entry);
                        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            string completion = dir == "." ? name : Path.Combine(dir, name);

                            if (Directory.Exists(entry))
                                completion += Path.DirectorySeparatorChar;

                            if (partial.StartsWith("~/") || partial.StartsWith("~\\"))
                            {
                                string home = Utils.Platform.HomeDirectory;
                                if (completion.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                                    completion = "~" + completion.Substring(home.Length);
                            }

                            completions.Add(completion);
                        }
                    }
                }
            }
            catch { }
        }

        completions.Sort(StringComparer.OrdinalIgnoreCase);
        return completions;
    }

    private string FindCommonPrefix(List<string> strings)
    {
        if (strings.Count == 0)
            return "";

        string first = strings[0];
        int len = first.Length;

        for (int i = 1; i < strings.Count; i++)
        {
            len = Math.Min(len, strings[i].Length);
            for (int j = 0; j < len; j++)
            {
                if (char.ToLowerInvariant(first[j]) != char.ToLowerInvariant(strings[i][j]))
                {
                    len = j;
                    break;
                }
            }
        }

        return first.Substring(0, len);
    }

    private static int ComputeLastLineVisibleLength(string text)
    {
        string stripped = Utils.Ansi.Strip(text);
        int lastNewline = stripped.LastIndexOf('\n');
        if (lastNewline < 0)
            return stripped.Length;
        return stripped.Length - lastNewline - 1;
    }
}
