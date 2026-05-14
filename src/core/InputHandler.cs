using System.Reflection.PortableExecutable;
using System.Text;
using AurShell.Utils;

namespace AurShell.Core;

public class InputHandler
{
    private readonly History _history;
    private readonly ShellEnvironment _env;
    private readonly SuggestionProvider? _suggestions;
    private readonly StringBuilder _buffer = new();
    private int _cursorPos;
    private int _promptVisibleLen;
    private string _ghostText = "";
    private bool _inReverseSearch;
    private string _searchQuery = "";
    private int _searchIndex = -1;
    private int _lastTermWidth;
    private int _lastTermHeight;
    private string _currentPrompt = "";
    private int _cursorRowOffset;
    private int _pendingResizeWidth;
    private int _pendingResizeHeight;
    private long _resizeChangeTick;
    private bool _multilineActive;
    private int _continuationPromptLen = 5;

    private int count;

    private const string ContinuationBoxChar = "\u2570\u2500";
    private const string ContinuationChevron = "\u276F";

    public InputHandler(History history, ShellEnvironment env, SuggestionProvider? suggestions = null)
    {
        _history = history;
        _env = env;
        _suggestions = suggestions;
        count = 0;
    }

    public string? ReadLine(string prompt) // AurSh Input hanndler
    {
        if (Console.IsInputRedirected)
        {
            Console.Write(prompt);
            return Console.ReadLine();
        }

        _currentPrompt = prompt;
        Console.Write(prompt);
        _promptVisibleLen = ComputeLastLineVisibleLength(prompt);
        _lastTermWidth = Utils.Platform.TerminalWidth;
        _lastTermHeight = Utils.Platform.TerminalHeight;

        _buffer.Clear();
        _cursorPos = 0;
        _ghostText = "";
        _inReverseSearch = false;
        _searchQuery = "";
        _searchIndex = -1;
        _history.ResetNavigation();

        UpdateGhostText();
        _cursorRowOffset = ComputeCursorPosition(_lastTermWidth).Row;

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
                key = Console.ReadKey(true); // Get input from the user,key by key
            }
            catch
            {
                return null;
            }

            if(key.Key == ConsoleKey.Spacebar)
            {
                count++;
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
                    _multilineActive = _buffer.ToString().Contains('\n');
                    RedrawLine();
                    
                    string pasted = _buffer.ToString();
                    if (!IsInputIncomplete(pasted))
                    {
                        Console.WriteLine();
                        _multilineActive = false;
                        return pasted;
                    }
                    continue;
                }

                string currentInput = _buffer.ToString();
                if (IsInputIncomplete(currentInput))
                {
                    _buffer.Append('\n');
                    _cursorPos = _buffer.Length;
                    _multilineActive = true;
                    RedrawLine();
                    continue;
                }

                _ghostText = "";
                RedrawLine(); // Ensure ghost text is cleared visually before confirming
                Console.WriteLine();
                _multilineActive = false;
                return currentInput;
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

            if ((key.Modifiers & ConsoleModifiers.Control) != 0) // ctrl characters e.g: ctrl + c, ctrl + u, ctrl + w and etc...
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

            if (!char.IsControl(key.KeyChar)) // Appending reg chars to Buffer
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

    private void HandleCharacter(char c) // Input handler for regular characters
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

    private static bool IsInputIncomplete(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        int doubleQuotes = 0;
        int singleQuotes = 0;
        bool inDouble = false;
        bool inSingle = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '\\' && i + 1 < input.Length && !inSingle)
            {
                i++;
                continue;
            }

            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                singleQuotes++;
            }
            else if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                doubleQuotes++;
            }
        }

        if (doubleQuotes % 2 != 0)
            return true;
        if (singleQuotes % 2 != 0)
            return true;

        string trimmed = input.TrimEnd();
        if (trimmed.Length == 0)
            return false;

        if (trimmed.EndsWith("|") && !trimmed.EndsWith("||"))
            return true;

        if (trimmed.EndsWith("||"))
            return true;

        if (trimmed.EndsWith("&&"))
            return true;

        if (Utils.Platform.IsUnixLike)
        {
            if (trimmed.EndsWith("\\") && !trimmed.EndsWith("\\\\"))
                return true;
        }

        return false;
    }

    private static bool IsPathLikeInput(string word)
    {
        if (string.IsNullOrEmpty(word))
            return false;

        return word.StartsWith("./") || word.StartsWith(".\\") ||
               word.StartsWith("../") || word.StartsWith("..\\") ||
               word.StartsWith("/") || word.StartsWith("~/") || word.StartsWith("~\\");
    }

    private void ReplaceLine(string newContent)
    {
        _buffer.Clear();
        _buffer.Append(newContent);
        _cursorPos = _buffer.Length;
        _multilineActive = newContent.Contains('\n');
        RedrawLine();
    }

    private bool isParenthesis(char c)
    {
        return c switch{
            '(' => true,
            ')' => true,
            '[' => true,
            ']' => true,
            '{' => true,
            '}' => true,
            _ => false

        };
    }

 private string SyntaxHighlight(string data) // Syntax highlightning
{

    data = Utils.Ansi.Strip(data);

    var sb = new StringBuilder();

    bool inQuotes = false;
    bool inSingles = false;
    bool firstToken = true;

    int i = 0;

    while (i < data.Length)
    {
        char c = data[i];


        if (c == '"') // if were in qoutes, display as meganta
        {
            inQuotes = !inQuotes;

            sb.Append(Ansi.FgBrightMagenta);
            sb.Append(c);
            sb.Append(Ansi.Reset);

            i++;
            continue;
        }

        if(c == '\'')
        {
            inSingles = !inSingles;

            sb.Append(Ansi.FgBrightMagenta);
            sb.Append(c);
            sb.Append(Ansi.Reset);

            i++;
            continue;
        }

        if (inSingles && !inQuotes)
        {
            sb.Append(Ansi.FgBrightMagenta);

            while(i < data.Length && !char.IsWhiteSpace(data[i]))
            {
                sb.Append(data[i]);
                i++;
            }

            continue;
        }

            if (isParenthesis(c) && !inQuotes && !firstToken)
            {
                sb.Append(Ansi.FgBrightRed);
                sb.Append(c);
                sb.Append(Ansi.Reset);

                i++;
                continue;
            }

        if(c == '$' && !inQuotes && !firstToken)
        {
                sb.Append(Ansi.FgBrightGreen);

                while(i < data.Length && !char.IsWhiteSpace(data[i])){
                    sb.Append(data[i]);
                    i++;
                }

                continue;
        }

       
        if (firstToken)
        {
            if (char.IsWhiteSpace(c))
            {
                firstToken = false;

                if(i == data.Length - 1)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append(Ansi.Reset);
                    sb.Append(c);
                }
            }
            else
            {
                sb.Append(Ansi.FgBrightCyan);
                sb.Append(c);
            }

            i++;
            continue;
        }

        if (!inQuotes && c == '-')
        {
            sb.Append(Ansi.Dim);

            while (i < data.Length &&
                   !char.IsWhiteSpace(data[i]))
            {
                sb.Append(data[i]);
                i++;
            }

            sb.Append(Ansi.Reset);
            continue;
        }

        if (inQuotes)
        {
            sb.Append(Ansi.FgBrightMagenta);
            sb.Append(c);
            sb.Append(Ansi.Reset);

            i++;
            continue;
        }

        sb.Append(Ansi.FgBrightWhite);
        sb.Append(c);
        sb.Append(Ansi.Reset);

        i++;
    }

    sb.Append(Ansi.Reset);

    return sb.ToString();
}

    private void RedrawLine()
    {
        UpdateGhostText();

        int width = Utils.Platform.TerminalWidth;
        if (width <= 0) width = 80;

        var sb = new StringBuilder();
        sb.Append(Utils.Ansi.CursorHide);
        sb.Append('\r');

        if (_cursorRowOffset > 0)
        {
            sb.Append(Utils.Ansi.MoveCursorUp(_cursorRowOffset));
        }

        sb.Append(Utils.Ansi.ClearScreenFromCursor);
        sb.Append(_currentPrompt);

        string fullText = _buffer.ToString();
        string highlightedFull = SyntaxHighlight(fullText);

        string contPrompt = Utils.Ansi.FgBrightBlack + ContinuationBoxChar + Utils.Ansi.Reset + " " + Utils.Ansi.FgRgb(100, 230, 150) + ContinuationChevron + Utils.Ansi.Reset + " ";
        string displayText = highlightedFull.Replace("\n", "\n" + contPrompt);
        sb.Append(displayText);

        if (!string.IsNullOrEmpty(_ghostText))
        {
            string ghostColor = Utils.Ansi.FgRgb(80, 80, 100);
            string ghostReset = Utils.Ansi.Reset;
            string ghostWithPrompts = _ghostText.Replace("\n", ghostReset + "\n" + contPrompt + ghostColor);
            sb.Append(ghostColor);
            sb.Append(ghostWithPrompts);
            sb.Append(ghostReset);
        }

        int totalRows = ComputeDisplayLinesAtWidth(width);

        var cursorPos = ComputeCursorPosition(width);

        int rowsToMoveUp = (totalRows - 1) - cursorPos.Row;
        if (rowsToMoveUp > 0)
        {
            sb.Append(Utils.Ansi.MoveCursorUp(rowsToMoveUp));
        }

        sb.Append(Utils.Ansi.SetCursorColumn(cursorPos.Col + 1));
        sb.Append(Utils.Ansi.CursorShow);

        _cursorRowOffset = cursorPos.Row;

        Console.Write(sb.ToString());
    }

    private void ClearGhostText()
    {
        if (!string.IsNullOrEmpty(_ghostText))
        {
            _ghostText = "";
            RedrawLine();
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
        {
            _ghostText = suggestion.Substring(current.Length);
            return;
        }

        bool isCommand = true;
        int firstSpaceIdx = -1;
        for (int i = 0; i < _cursorPos; i++)
        {
            if (_buffer[i] == ' ')
            {
                isCommand = false;
                if (firstSpaceIdx < 0)
                    firstSpaceIdx = i;
                break;
            }
        }

        if (!isCommand && _suggestions != null && firstSpaceIdx > 0)
        {
            string commandName = current.Substring(0, firstSpaceIdx);
            string word = GetCurrentWord();
            var sugCompletions = _suggestions.GetCompletionsForArg(commandName, word);
            if (sugCompletions.Count > 0)
            {
                string first = sugCompletions[0];
                if (!string.IsNullOrEmpty(word) && first.Length > word.Length &&
                    first.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                {
                    _ghostText = first.Substring(word.Length);
                    return;
                }
                else if (string.IsNullOrEmpty(word))
                {
                    _ghostText = first;
                    return;
                }
            }
        }

        if (!isCommand)
        {
            string word = GetCurrentWord();
            var completions = GetCompletions(word);
            if (completions.Count > 0)
            {
                string first = completions[0];
                if (first.Length > word.Length && first.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                {
                    _ghostText = first.Substring(word.Length);
                    return;
                }
                else if (string.IsNullOrEmpty(word))
                {
                    _ghostText = first;
                    return;
                }
            }
        }

        if (isCommand && IsPathLikeInput(current))
        {
            var pathCompletions = GetPathCompletions(current);
            if (pathCompletions.Count > 0)
            {
                string first = pathCompletions[0];
                if (first.Length > current.Length && first.StartsWith(current, StringComparison.OrdinalIgnoreCase))
                {
                    _ghostText = first.Substring(current.Length);
                    return;
                }
            }
        }

        _ghostText = "";
    }

    private void CheckTerminalResize()
    {
        int currentWidth = Utils.Platform.TerminalWidth;
        int currentHeight = Utils.Platform.TerminalHeight;

        if (currentWidth == _lastTermWidth && currentHeight == _lastTermHeight)
        {
            _pendingResizeWidth = 0;
            _pendingResizeHeight = 0;
            return;
        }

        if (currentWidth != _pendingResizeWidth || currentHeight != _pendingResizeHeight)
        {
            _pendingResizeWidth = currentWidth;
            _pendingResizeHeight = currentHeight;
            _resizeChangeTick = Environment.TickCount64;
            return;
        }

        long elapsed = Environment.TickCount64 - _resizeChangeTick;
        if (elapsed >= 100)
        {
            int cursorRowsFromTop = ComputeCursorPosition(currentWidth).Row;

            _lastTermWidth = currentWidth;
            _lastTermHeight = currentHeight;
            _pendingResizeWidth = 0;
            _pendingResizeHeight = 0;
            FullRedraw(clearPrevious: true, cursorRowsFromTop: cursorRowsFromTop);
        }
    }

    private (int Row, int Col) ComputeCursorPosition(int width)
    {
        if (width <= 0) return (0, 0);

        string[] promptLines = _currentPrompt.Split('\n');
        int rows = 0;

        for (int i = 0; i < promptLines.Length - 1; i++)
        {
            int vis = Utils.Ansi.VisibleLength(promptLines[i]);
            rows += Math.Max(1, (vis + width - 1) / width);
        }

        int lastPromptVis = promptLines.Length > 0 ? Utils.Ansi.VisibleLength(promptLines[^1]) : _promptVisibleLen;

        string bufferText = _buffer.ToString();
        string textBeforeCursor = bufferText.Substring(0, _cursorPos);
        string[] linesBeforeCursor = textBeforeCursor.Split('\n');
        
        for (int i = 0; i < linesBeforeCursor.Length - 1; i++)
        {
            int prefixVis = (i == 0) ? lastPromptVis : _continuationPromptLen;
            int textVis = Utils.Ansi.VisibleLength(linesBeforeCursor[i]);
            rows += Math.Max(1, (prefixVis + textVis + width - 1) / width);
        }
        
        int lastLinePrefixVis = (linesBeforeCursor.Length == 1) ? lastPromptVis : _continuationPromptLen;
        int lastLineTextVis = Utils.Ansi.VisibleLength(linesBeforeCursor[^1]);
        int totalVisOnLastLine = lastLinePrefixVis + lastLineTextVis;
        
        rows += totalVisOnLastLine / width;
        int col = totalVisOnLastLine % width;
        
        return (rows, col);
    }

    private void FullRedraw(bool clearPrevious = false, int cursorRowsFromTop = -1)
    {
        var prompt = new Prompt(_env);
        string cwd = _env.Get("PWD") ?? Directory.GetCurrentDirectory();
        int exitCode = _env.LastExitCode;
        _currentPrompt = prompt.Render(cwd, exitCode);
        _promptVisibleLen = prompt.PromptVisibleLength(exitCode);

        if (clearPrevious)
        {
            int moveUp = cursorRowsFromTop >= 0 ? cursorRowsFromTop : _cursorRowOffset;
            if (moveUp > 0)
                Console.Write("\r" + Utils.Ansi.MoveCursorUp(moveUp) + Utils.Ansi.ClearScreenFromCursor);
            else
                Console.Write("\r" + Utils.Ansi.ClearScreenFromCursor);
            
            _cursorRowOffset = 0;
        }

        RedrawLine();
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
        int firstSpaceIdx = -1;
        for (int i = 0; i < _cursorPos; i++)
        {
            if (_buffer[i] == ' ')
            {
                isCommand = false;
                if (firstSpaceIdx < 0)
                    firstSpaceIdx = i;
                break;
            }
        }

        if (isCommand)
        {
            if (IsPathLikeInput(partial))
            {
                return GetPathCompletions(partial);
            }

            foreach (string builtin in new[]
            {
                "cd", "export", "unset", "exit", "history", "clear", "echo",
                "pwd", "type", "alias", "unalias", "source", "set", "env",
                "true", "false", "read", "test", "return",
                "jobs", "fg", "kill", "aursh-plugin", "aursh-assoc",
                "aursh-reload", "aursh-history", "aursh-about",
                "aursh-ls", "aursh-cat", "aursh-update"
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
            string commandName = firstSpaceIdx > 0
                ? _buffer.ToString().Substring(0, firstSpaceIdx)
                : "";

            if (_suggestions != null && !string.IsNullOrEmpty(commandName) && _suggestions.HasCommand(commandName))
            {
                var sugResults = _suggestions.GetCompletionsForArg(commandName, partial);
                completions.AddRange(sugResults);
            }

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

    private List<string> GetPathCompletions(string partial)
    {
        var completions = new List<string>();
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

                        string originalPrefix = partial.Substring(0, partial.Length - prefix.Length);
                        completion = originalPrefix + name;
                        if (Directory.Exists(entry))
                            completion += Path.DirectorySeparatorChar;

                        completions.Add(completion);
                    }
                }
            }
        }
        catch { }

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

    private int ComputeDisplayLines()
    {
        int termWidth = Utils.Platform.TerminalWidth;
        if (termWidth <= 0) return 2;
        return ComputeDisplayLinesAtWidth(termWidth);
    }

    private int ComputeDisplayLinesAtWidth(int width)
    {
        if (width <= 0) return 2;

        string[] promptLines = _currentPrompt.Split('\n');
        int totalLines = 0;

        for (int i = 0; i < promptLines.Length - 1; i++)
        {
            int vis = Utils.Ansi.VisibleLength(promptLines[i]);
            totalLines += Math.Max(1, (vis + width - 1) / width);
        }

        int lastPromptVis = promptLines.Length > 0 ? Utils.Ansi.VisibleLength(promptLines[^1]) : _promptVisibleLen;
        
        string fullContent = _buffer.ToString() + _ghostText;
        string[] contentLines = fullContent.Split('\n');
        
        for (int i = 0; i < contentLines.Length; i++)
        {
            int prefixVis = (i == 0) ? lastPromptVis : _continuationPromptLen;
            int lineVis = prefixVis + Utils.Ansi.VisibleLength(contentLines[i]);
            totalLines += Math.Max(1, (lineVis + width - 1) / width);
        }

        return totalLines;
    }
}