using System.Text;
using System.Text.RegularExpressions;

namespace AurShell.Core;

public enum TokenType
{
    Word,
    Pipe,
    And,
    Or,
    Semicolon,
    DoubleSemicolon,
    Background,
    RedirectOut,
    RedirectAppend,
    RedirectIn,
    RedirectErr,
    RedirectErrAppend,
    RedirectErrToOut,
    HereDoc,
    HereString,
    HereDocText,
    Newline,
    LeftParen,
    RightParen,
    LeftBrace, RightBrace, LeftBracket, RightBracket, Dot, Comma, Colon,
    Assign, Equal, NotEqual, LessThan, GreaterThan, LessThanOrEqual, GreaterThanOrEqual,
    Plus, Minus, Multiply, Divide, Not,
    EOF
}

public class Token
{
    public TokenType Type { get; }
    public string Value { get; }
    public int Line { get; }
    public int Column { get; }
    public bool WasQuoted { get; }
    public bool WasSingleQuoted { get; }
    public string RawExpandedValue { get; }
    public bool HasLeadingSpace { get; }

    public Token(TokenType type, string value, int line, int column, bool wasQuoted = false, bool wasSingleQuoted = false, string? rawExpandedValue = null, bool hasLeadingSpace = false)
    {
        Type = type;
        Value = value;
        Line = line;
        Column = column;
        WasQuoted = wasQuoted;
        WasSingleQuoted = wasSingleQuoted;
        RawExpandedValue = rawExpandedValue ?? value;
        HasLeadingSpace = hasLeadingSpace;
    }

    public override string ToString() => $"[{Line}:{Column} {Type}: {Value}]";
}

public class Lexer
{
    private readonly string _input;
    private readonly ShellEnvironment _env;
    private int _pos;
    private readonly List<int> _lineStarts;

    public Lexer(string input, ShellEnvironment env)
    {
        _input = input;
        _env = env;
        _pos = 0;
        _lineStarts = new List<int> { 0 };
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '\n') _lineStarts.Add(i + 1);
        }
    }

    private (int line, int col) GetPosition(int pos)
    {
        int line = _lineStarts.BinarySearch(pos);
        if (line < 0) line = ~line - 1;
        int col = pos - _lineStarts[line] + 1;
        return (line + 1, col); // 1-indexed
    }

    private Token CreateToken(TokenType type, string value, int startPos, bool wasQuoted = false, bool wasSingleQuoted = false, string? rawExpandedValue = null, bool hasLeadingSpace = false)
    {
        var (line, col) = GetPosition(startPos);
        return new Token(type, value, line, col, wasQuoted, wasSingleQuoted, rawExpandedValue, hasLeadingSpace);
    }

    public List<Token> Tokenize(HashSet<string>? inheritedExpandedAliases = null)
    {
        var tokens = new List<Token>();
        bool isFirstWord = true;
        HashSet<string> expandedAliases = new HashSet<string>();
        
        void ResetExpandedAliases()
        {
            expandedAliases.Clear();
            if (inheritedExpandedAliases != null)
            {
                foreach (var alias in inheritedExpandedAliases)
                    expandedAliases.Add(alias);
            }
        }

        ResetExpandedAliases();

        var pendingHereDocs = new Queue<(string delimiter, bool stripTabs, int insertIndex)>();

        while (_pos < _input.Length)
        {
            bool hasLeadingSpace = SkipWhitespace();

            int startPos = _pos;
            if (_pos >= _input.Length)
                break;

            char c = _input[_pos];

            if (c == '#')
            {
                while (_pos < _input.Length && _input[_pos] != '\n')
                    _pos++;
                continue;
            }

            if (c == '\n')
            {
                tokens.Add(CreateToken(TokenType.Newline, "\n", startPos, hasLeadingSpace: hasLeadingSpace));
                _pos++;

                // Process pending HereDocs before continuing to the next line of commands
                int insertedTokens = 0;
                while (pendingHereDocs.Count > 0 && _pos < _input.Length)
                {
                    var hereDoc = pendingHereDocs.Dequeue();
                    var sb = new StringBuilder();
                    int hereDocStartPos = _pos;

                    while (_pos < _input.Length)
                    {
                        int lineStart = _pos;
                        while (_pos < _input.Length && _input[_pos] != '\n') _pos++;

                        string rawLine = _input.Substring(lineStart, _pos - lineStart);
                        string checkLine = hereDoc.stripTabs ? rawLine.TrimStart('\t') : rawLine;

                        if (checkLine == hereDoc.delimiter)
                        {
                            if (_pos < _input.Length && _input[_pos] == '\n') _pos++;
                            break;
                        }

                        sb.AppendLine(rawLine);
                        if (_pos < _input.Length && _input[_pos] == '\n') _pos++;
                    }

                    tokens.Insert(hereDoc.insertIndex + insertedTokens, CreateToken(TokenType.HereDocText, sb.ToString(), hereDocStartPos, hasLeadingSpace: hasLeadingSpace));
                    insertedTokens++;
                }

                isFirstWord = true;
                ResetExpandedAliases();
                continue;
            }

            if (c == ';')
            {
                _pos++;
                if (_pos < _input.Length && _input[_pos] == ';')
                {
                    tokens.Add(CreateToken(TokenType.DoubleSemicolon, ";;", startPos, hasLeadingSpace: hasLeadingSpace));
                    _pos++;
                }
                else
                {
                    tokens.Add(CreateToken(TokenType.Semicolon, ";", startPos, hasLeadingSpace: hasLeadingSpace));
                }
                isFirstWord = true;
                ResetExpandedAliases();
                continue;
            }

            if (c == '|')
            {
                _pos++;
                if (_pos < _input.Length && _input[_pos] == '|')
                {
                    tokens.Add(CreateToken(TokenType.Or, "||", startPos, hasLeadingSpace: hasLeadingSpace));
                    _pos++;
                }
                else
                {
                    tokens.Add(CreateToken(TokenType.Pipe, "|", startPos, hasLeadingSpace: hasLeadingSpace));
                }
                isFirstWord = true;
                ResetExpandedAliases();
                continue;
            }

            if (c == '&')
            {
                _pos++;
                if (_pos < _input.Length && _input[_pos] == '&')
                {
                    tokens.Add(CreateToken(TokenType.And, "&&", startPos, hasLeadingSpace: hasLeadingSpace));
                    _pos++;
                }
                else
                {
                    tokens.Add(CreateToken(TokenType.Background, "&", startPos, hasLeadingSpace: hasLeadingSpace));
                }
                isFirstWord = true;
                ResetExpandedAliases();
                continue;
            }

            if (c == '2' && _pos + 1 < _input.Length && _input[_pos + 1] == '>')
            {
                _pos += 2;
                if (_pos < _input.Length && _input[_pos] == '>')
                {
                    tokens.Add(CreateToken(TokenType.RedirectErrAppend, "2>>", startPos, hasLeadingSpace: hasLeadingSpace));
                    _pos++;
                }
                else if (_pos + 1 < _input.Length && _input[_pos] == '&' && _input[_pos + 1] == '1')
                {
                    tokens.Add(CreateToken(TokenType.RedirectErrToOut, "2>&1", startPos, hasLeadingSpace: hasLeadingSpace));
                    _pos += 2;
                }
                else
                {
                    tokens.Add(CreateToken(TokenType.RedirectErr, "2>", startPos, hasLeadingSpace: hasLeadingSpace));
                }
                continue;
            }

            if (c == '>')
            {
                if (_pos + 1 < _input.Length && _input[_pos + 1] == '(')
                {
                    // Fall through to ReadWord for process substitution >(...)
                }
                else
                {
                    _pos++;
                    if (_pos < _input.Length && _input[_pos] == '>')
                    {
                        tokens.Add(CreateToken(TokenType.RedirectAppend, ">>", startPos, hasLeadingSpace: hasLeadingSpace));
                        _pos++;
                    }
                    else
                    {
                        tokens.Add(CreateToken(TokenType.RedirectOut, ">", startPos, hasLeadingSpace: hasLeadingSpace));
                    }
                    continue;
                }
            }

            if (c == '<')
            {
                if (_pos + 1 < _input.Length && _input[_pos + 1] == '(')
                {
                    // Fall through to ReadWord for process substitution <(...)
                }
                else
                {
                    _pos++;
                    if (_pos < _input.Length && _input[_pos] == '<')
                    {
                        _pos++;
                        if (_pos < _input.Length && _input[_pos] == '<')
                        {
                            _pos++;
                            tokens.Add(CreateToken(TokenType.HereString, "<<<", startPos, hasLeadingSpace: hasLeadingSpace));
                        }
                        else if (_pos < _input.Length && _input[_pos] == '-')
                        {
                            _pos++;
                            tokens.Add(CreateToken(TokenType.HereDoc, "<<-", startPos, hasLeadingSpace: hasLeadingSpace));
                        }
                        else
                        {
                            tokens.Add(CreateToken(TokenType.HereDoc, "<<", startPos, hasLeadingSpace: hasLeadingSpace));
                        }
                    }
                    else
                    {
                        tokens.Add(CreateToken(TokenType.RedirectIn, "<", startPos, hasLeadingSpace: hasLeadingSpace));
                    }
                    continue;
                }
            }

            if (c == '(')
            {
                tokens.Add(CreateToken(TokenType.LeftParen, "(", startPos, hasLeadingSpace: hasLeadingSpace));
                _pos++;
                isFirstWord = true;
                ResetExpandedAliases();
                continue;
            }

            if (c == ')')
            {
                tokens.Add(CreateToken(TokenType.RightParen, ")", startPos, hasLeadingSpace: hasLeadingSpace));
                _pos++;
                continue;
            }

            if (c == '{') { tokens.Add(CreateToken(TokenType.LeftBrace, "{", startPos, hasLeadingSpace: hasLeadingSpace)); _pos++; continue; }
            if (c == '}') { tokens.Add(CreateToken(TokenType.RightBrace, "}", startPos, hasLeadingSpace: hasLeadingSpace)); _pos++; continue; }
            if (c == '[') { tokens.Add(CreateToken(TokenType.LeftBracket, "[", startPos, hasLeadingSpace: hasLeadingSpace)); _pos++; continue; }
            if (c == ']') { tokens.Add(CreateToken(TokenType.RightBracket, "]", startPos, hasLeadingSpace: hasLeadingSpace)); _pos++; continue; }
            if (c == '.') { tokens.Add(CreateToken(TokenType.Dot, ".", startPos, hasLeadingSpace: hasLeadingSpace)); _pos++; continue; }
            if (c == ',') { tokens.Add(CreateToken(TokenType.Comma, ",", startPos, hasLeadingSpace: hasLeadingSpace)); _pos++; continue; }
            if (c == ':') { tokens.Add(CreateToken(TokenType.Colon, ":", startPos, hasLeadingSpace: hasLeadingSpace)); _pos++; continue; }
            if (c == '+') { tokens.Add(CreateToken(TokenType.Plus, "+", startPos, hasLeadingSpace: hasLeadingSpace)); _pos++; continue; }
            if (c == '-') { tokens.Add(CreateToken(TokenType.Minus, "-", startPos, hasLeadingSpace: hasLeadingSpace)); _pos++; continue; }
            if (c == '*') { tokens.Add(CreateToken(TokenType.Multiply, "*", startPos, hasLeadingSpace: hasLeadingSpace)); _pos++; continue; }
            if (c == '/') { tokens.Add(CreateToken(TokenType.Divide, "/", startPos, hasLeadingSpace: hasLeadingSpace)); _pos++; continue; }
            if (c == '!') 
            {
                if (_pos + 1 < _input.Length && _input[_pos + 1] == '=')
                {
                    tokens.Add(CreateToken(TokenType.NotEqual, "!=", startPos, hasLeadingSpace: hasLeadingSpace));
                    _pos += 2;
                }
                else
                {
                    tokens.Add(CreateToken(TokenType.Not, "!", startPos, hasLeadingSpace: hasLeadingSpace));
                    _pos++;
                }
                continue;
            }
            if (c == '=')
            {
                if (_pos + 1 < _input.Length && _input[_pos + 1] == '=')
                {
                    tokens.Add(CreateToken(TokenType.Equal, "==", startPos, hasLeadingSpace: hasLeadingSpace));
                    _pos += 2;
                }
                else
                {
                    tokens.Add(CreateToken(TokenType.Assign, "=", startPos, hasLeadingSpace: hasLeadingSpace));
                    _pos++;
                }
                continue;
            }

            Token wordToken = ReadWord(startPos, hasLeadingSpace);
            if (isFirstWord && !wordToken.WasQuoted)
            {
                string? alias = _env.GetAlias(wordToken.Value);
                if (alias != null && !expandedAliases.Contains(wordToken.Value))
                {
                    expandedAliases.Add(wordToken.Value);
                    var subLexer = new Lexer(alias, _env);
                    var subTokens = subLexer.Tokenize(expandedAliases);
                    if (subTokens.Count > 0 && subTokens.Last().Type == TokenType.EOF)
                        subTokens.RemoveAt(subTokens.Count - 1);
                    tokens.AddRange(subTokens);
                    isFirstWord = false;
                    continue;
                }
            }

            tokens.Add(wordToken);

            if (tokens.Count >= 2)
            {
                var prevToken = tokens[tokens.Count - 2];
                if (prevToken.Type == TokenType.HereDoc)
                {
                    bool stripTabs = prevToken.Value == "<<-";
                    pendingHereDocs.Enqueue((wordToken.Value, stripTabs, tokens.Count));
                }
            }

            isFirstWord = false;
        }

        tokens.Add(CreateToken(TokenType.EOF, "", _pos));
        return tokens;
    }

    private bool SkipWhitespace()
    {
        bool skipped = false;
        while (_pos < _input.Length && _input[_pos] != '\n' && char.IsWhiteSpace(_input[_pos]))
        {
            _pos++;
            skipped = true;
        }
        return skipped;
    }

    private Token ReadWord(int startPos, bool hasLeadingSpace)
    {
        var sb = new StringBuilder();
        var rawSb = new StringBuilder();
        bool wasQuoted = false;
        bool wasSingleQuoted = false;

        while (_pos < _input.Length)
        {
            char c = _input[_pos];

            if (char.IsWhiteSpace(c) && c != '\n')
                break;

            if ("?*+@!".Contains(c) && _pos + 1 < _input.Length && _input[_pos + 1] == '(')
            {
                sb.Append(c);
                sb.Append('(');
                rawSb.Append(c);
                rawSb.Append('(');
                _pos += 2;

                int depth = 1;
                while (_pos < _input.Length && depth > 0)
                {
                    char gc = _input[_pos];
                    if (gc == '(') depth++;
                    else if (gc == ')') depth--;

                    sb.Append(gc);
                    rawSb.Append(gc);
                    _pos++;
                }
                wasSingleQuoted = false;
                continue;
            }

            if (c == '<' || c == '>')
            {
                if (_pos + 1 < _input.Length && _input[_pos + 1] == '(')
                {
                    sb.Append(c);
                    sb.Append('(');
                    rawSb.Append(c);
                    rawSb.Append('(');
                    _pos += 2;
                    int depth = 1;
                    while (_pos < _input.Length && depth > 0)
                    {
                        char gc = _input[_pos];
                        if (gc == '(') depth++;
                        else if (gc == ')') depth--;

                        sb.Append(gc);
                        rawSb.Append(gc);
                        _pos++;
                    }
                    wasSingleQuoted = false;
                    continue;
                }
                else
                {
                    break;
                }
            }

            if (c == '\n' || c == ';' || c == '|' || c == '(' || c == ')')
                break;

            if (c == '&')
                break;

            if (c == '#' && sb.Length == 0)
                break;

            if ("+-*/=<>!.,{}[]:".Contains(c))
                break;

            if (c == '\\' && _pos + 1 < _input.Length)
            {
                if (Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows)
                {
                    sb.Append(c);
                    rawSb.Append(c);
                    _pos++;
                    wasSingleQuoted = false;
                    continue;
                }
                _pos++;
                char escaped = _input[_pos];
                sb.Append(MapEscapeChar(escaped));
                rawSb.Append('\\');
                rawSb.Append(escaped);
                _pos++;
                wasSingleQuoted = false;
                continue;
            }

            if (c == '\'')
            {
                if (sb.Length == 0)
                    wasSingleQuoted = true;
                string inside = ReadSingleQuoted();
                sb.Append(inside);
                rawSb.Append('\'');
                rawSb.Append(inside);
                rawSb.Append('\'');
                wasQuoted = true;
                continue;
            }

            if (c == '`')
            {
                string inside = ReadBackticked();
                sb.Append(inside);
                rawSb.Append(inside);
                wasSingleQuoted = false;
                continue;
            }

            if (c == '"')
            {
                wasSingleQuoted = false;
                string inside = ReadDoubleQuoted();
                sb.Append(inside);
                rawSb.Append('"');
                rawSb.Append(inside);
                rawSb.Append('"');
                wasQuoted = true;
                continue;
            }

            if (c == '$')
            {
                if (_pos + 2 < _input.Length && _input[_pos + 1] == '(' && _input[_pos + 2] == '(')
                {
                    string arithExpanded = ExpandArithmetic();
                    sb.Append(arithExpanded);
                    rawSb.Append(arithExpanded);
                    wasSingleQuoted = false;
                    continue;
                }

                if (_pos + 1 < _input.Length && _input[_pos + 1] == '(')
                {
                    string sub = ReadSubCommand();
                    sb.Append(sub);
                    rawSb.Append(sub);
                    wasSingleQuoted = false;
                    continue;
                }


                string expanded = ExpandInline();
                sb.Append(expanded);
                rawSb.Append(expanded);
                wasSingleQuoted = false;
                continue;
            }

            if (c == '~' && sb.Length == 0)
            {
                if (_pos + 1 >= _input.Length || _input[_pos + 1] == '/' || _input[_pos + 1] == '\\' || char.IsWhiteSpace(_input[_pos + 1]))
                {
                    string home = Utils.Platform.HomeDirectory;
                    sb.Append(home);
                    rawSb.Append(home);
                    _pos++;
                    wasSingleQuoted = false;
                    continue;
                }
            }

            if (c == '*' || c == '?')
            {
                sb.Append(c);
                rawSb.Append(c);
                _pos++;
                wasSingleQuoted = false;
                continue;
            }

            sb.Append(c);
            rawSb.Append(c);
            _pos++;
            wasSingleQuoted = false;
        }

        return CreateToken(TokenType.Word, sb.ToString(), startPos, wasQuoted, wasSingleQuoted, rawSb.ToString(), hasLeadingSpace);
    }

    private string ReadSubCommand()
    {
        _pos++;
        _pos++;
        var sb = new StringBuilder();
        sb.Append("$(");

        int depth = 1;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool escaped = false;

        while (_pos < _input.Length && depth > 0)
        {
            char c = _input[_pos];

            if (escaped)
            {
                escaped = false;
            }
            else if (c == '\\')
            {
                escaped = true;
            }
            else if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
            }
            else if (!inSingleQuote && !inDoubleQuote)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
            }

            if (depth > 0)
            {
                sb.Append(c);
                _pos++;
            }
            else
            {
                sb.Append(')');
                _pos++;
            }
        }

        return sb.ToString();
    }

    private string ReadSingleQuoted()
    {
        _pos++;
        var sb = new StringBuilder();

        while (_pos < _input.Length && _input[_pos] != '\'')
        {
            sb.Append(_input[_pos]);
            _pos++;
        }

        if (_pos < _input.Length)
            _pos++;

        return sb.ToString();
    }

    private string ReadBackticked()
    {
        _pos++;
        var sb = new StringBuilder();
        sb.Append('`');

        while (_pos < _input.Length && _input[_pos] != '`')
        {
            sb.Append(_input[_pos]);
            _pos++;
        }

        if (_pos < _input.Length)
        {
            sb.Append('`');
            _pos++;
        }

        return sb.ToString();
    }

    private string ReadDoubleQuoted()
    {
        _pos++;
        var sb = new StringBuilder();

        while (_pos < _input.Length && _input[_pos] != '"')
        {
            char c = _input[_pos];

            if (c == '\\' && _pos + 1 < _input.Length)
            {
                char next = _input[_pos + 1];
                if (next == '"' || next == '\\' || next == '$' || next == '`' || next == '\n')
                {
                    sb.Append(next);
                    _pos += 2;
                    continue;
                }
            }

            if (c == '$')
            {
                if (_pos + 2 < _input.Length && _input[_pos + 1] == '(' && _input[_pos + 2] == '(')
                {
                    sb.Append(ExpandArithmetic());
                    continue;
                }

                if (_pos + 1 < _input.Length && _input[_pos + 1] == '(')
                {
                    sb.Append(ReadSubCommand());
                    continue;
                }
                sb.Append(ExpandInline());
                continue;
            }

            sb.Append(c);
            _pos++;
        }

        if (_pos < _input.Length)
            _pos++;

        return sb.ToString();
    }

    private string ExpandArithmetic()
    {
        _pos += 3; // skip $((
        int start = _pos;
        int depth = 2; // we passed two ((

        while (_pos < _input.Length && depth > 0)
        {
            if (_input[_pos] == '(') depth++;
            else if (_input[_pos] == ')') depth--;

            if (depth > 0) _pos++;
        }

        string mathExpr = "";
        if (depth == 0)
        {
            mathExpr = _input.Substring(start, _pos - start - 1);
            _pos++; // skip last )
        }
        else
        {
            mathExpr = _input.Substring(start);
        }

        return "$((" + mathExpr + "))";
    }

    private string ExpandInline()
    {
        int start = _pos;
        _pos++;

        if (_pos >= _input.Length)
            return "$";

        char c = _input[_pos];

        if (c == '?')
        {
            _pos++;
            return "$?";
        }

        if (c == '$')
        {
            _pos++;
            return "$$";
        }

        if (c == '!')
        {
            _pos++;
            return "$!";
        }

        if (Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows && c == '_')
        {
            _pos++;
            return "$_";
        }

        if (c == '{')
        {
            _pos++;
            int braceStart = _pos;
            int depth = 1;
            while (_pos < _input.Length && depth > 0)
            {
                if (_input[_pos] == '{') depth++;
                else if (_input[_pos] == '}') depth--;
                if (depth > 0) _pos++;
            }

            string content = _input.Substring(braceStart, _pos - braceStart);
            if (_pos < _input.Length) _pos++;

            return "${" + content + "}";
        }

        var nameBuf = new StringBuilder();
        while (_pos < _input.Length && (char.IsLetterOrDigit(_input[_pos]) || _input[_pos] == '_'))
        {
            nameBuf.Append(_input[_pos]);
            _pos++;
        }

        if (_pos < _input.Length && _input[_pos] == '.' && nameBuf.Length > 0)
        {
            string objName = nameBuf.ToString();
            _pos++;
            var fieldBuf = new StringBuilder();
            while (_pos < _input.Length && (char.IsLetterOrDigit(_input[_pos]) || _input[_pos] == '_'))
            {
                fieldBuf.Append(_input[_pos]);
                _pos++;
            }
            return "$" + objName + "." + fieldBuf.ToString();
        }

        string name = nameBuf.ToString();
        if (string.IsNullOrEmpty(name))
            return "$";

        return "$" + name;
    }

    private static char MapEscapeChar(char c) => c switch
    {
        'n' => '\n',
        't' => '\t',
        'r' => '\r',
        'a' => '\a',
        'b' => '\b',
        '0' => '\0',
        _ => c
    };
}
