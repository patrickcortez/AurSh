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
    Newline,
    LeftParen,
    RightParen,
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

    public Token(TokenType type, string value, int line, int column, bool wasQuoted = false, bool wasSingleQuoted = false, string? rawExpandedValue = null)
    {
        Type = type;
        Value = value;
        Line = line;
        Column = column;
        WasQuoted = wasQuoted;
        WasSingleQuoted = wasSingleQuoted;
        RawExpandedValue = rawExpandedValue ?? value;
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

    private Token CreateToken(TokenType type, string value, int startPos, bool wasQuoted = false, bool wasSingleQuoted = false, string? rawExpandedValue = null)
    {
        var (line, col) = GetPosition(startPos);
        return new Token(type, value, line, col, wasQuoted, wasSingleQuoted, rawExpandedValue);
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        bool isFirstWord = true;
        HashSet<string> expandedAliases = new HashSet<string>();

        while (_pos < _input.Length)
        {
            SkipWhitespace();

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
                tokens.Add(CreateToken(TokenType.Newline, "\n", startPos));
                _pos++;
                isFirstWord = true;
                expandedAliases.Clear();
                continue;
            }

            if (c == ';')
            {
                _pos++;
                if (_pos < _input.Length && _input[_pos] == ';')
                {
                    tokens.Add(CreateToken(TokenType.DoubleSemicolon, ";;", startPos));
                    _pos++;
                }
                else
                {
                    tokens.Add(CreateToken(TokenType.Semicolon, ";", startPos));
                }
                isFirstWord = true;
                expandedAliases.Clear();
                continue;
            }

            if (c == '|')
            {
                _pos++;
                if (_pos < _input.Length && _input[_pos] == '|')
                {
                    tokens.Add(CreateToken(TokenType.Or, "||", startPos));
                    _pos++;
                }
                else
                {
                    tokens.Add(CreateToken(TokenType.Pipe, "|", startPos));
                }
                isFirstWord = true;
                expandedAliases.Clear();
                continue;
            }

            if (c == '&')
            {
                _pos++;
                if (_pos < _input.Length && _input[_pos] == '&')
                {
                    tokens.Add(CreateToken(TokenType.And, "&&", startPos));
                    _pos++;
                }
                else
                {
                    tokens.Add(CreateToken(TokenType.Background, "&", startPos));
                }
                isFirstWord = true;
                expandedAliases.Clear();
                continue;
            }

            if (c == '2' && _pos + 1 < _input.Length && _input[_pos + 1] == '>')
            {
                _pos += 2;
                if (_pos < _input.Length && _input[_pos] == '>')
                {
                    tokens.Add(CreateToken(TokenType.RedirectErrAppend, "2>>", startPos));
                    _pos++;
                }
                else if (_pos + 1 < _input.Length && _input[_pos] == '&' && _input[_pos + 1] == '1')
                {
                    tokens.Add(CreateToken(TokenType.RedirectErrToOut, "2>&1", startPos));
                    _pos += 2;
                }
                else
                {
                    tokens.Add(CreateToken(TokenType.RedirectErr, "2>", startPos));
                }
                continue;
            }

            if (c == '>')
            {
                _pos++;
                if (_pos < _input.Length && _input[_pos] == '>')
                {
                    tokens.Add(CreateToken(TokenType.RedirectAppend, ">>", startPos));
                    _pos++;
                }
                else
                {
                    tokens.Add(CreateToken(TokenType.RedirectOut, ">", startPos));
                }
                continue;
            }

            if (c == '<')
            {
                _pos++;
                if (_pos < _input.Length && _input[_pos] == '<')
                {
                    _pos++;
                    if (_pos < _input.Length && _input[_pos] == '<')
                    {
                        _pos++;
                        tokens.Add(CreateToken(TokenType.HereString, "<<<", startPos));
                    }
                    else if (_pos < _input.Length && _input[_pos] == '-')
                    {
                        _pos++;
                        tokens.Add(CreateToken(TokenType.HereDoc, "<<-", startPos));
                    }
                    else
                    {
                        tokens.Add(CreateToken(TokenType.HereDoc, "<<", startPos));
                    }
                }
                else
                {
                    tokens.Add(CreateToken(TokenType.RedirectIn, "<", startPos));
                }
                continue;
            }

            if (c == '(')
            {
                tokens.Add(CreateToken(TokenType.LeftParen, "(", startPos));
                _pos++;
                isFirstWord = true;
                expandedAliases.Clear();
                continue;
            }

            if (c == ')')
            {
                tokens.Add(CreateToken(TokenType.RightParen, ")", startPos));
                _pos++;
                continue;
            }

            Token wordToken = ReadWord(startPos);
            if (isFirstWord && !wordToken.WasQuoted)
            {
                string? alias = _env.GetAlias(wordToken.Value);
                if (alias != null && !expandedAliases.Contains(wordToken.Value))
                {
                    expandedAliases.Add(wordToken.Value);
                    var subLexer = new Lexer(alias, _env);
                    var subTokens = subLexer.Tokenize();
                    if (subTokens.Count > 0 && subTokens.Last().Type == TokenType.EOF)
                        subTokens.RemoveAt(subTokens.Count - 1);
                    tokens.AddRange(subTokens);
                    isFirstWord = false;
                    continue;
                }
            }

            tokens.Add(wordToken);
            isFirstWord = false;
        }

        tokens.Add(CreateToken(TokenType.EOF, "", _pos));
        return tokens;
    }

    private void SkipWhitespace()
    {
        while (_pos < _input.Length && _input[_pos] != '\n' && char.IsWhiteSpace(_input[_pos]))
            _pos++;
    }

    private Token ReadWord(int startPos)
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

            if (c == '\n' || c == ';' || c == '|' || c == '<' || c == '(' || c == ')')
                break;

            if (c == '>')
                break;

            if (c == '&')
                break;

            if (c == '#' && sb.Length == 0)
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

        return CreateToken(TokenType.Word, sb.ToString(), startPos, wasQuoted, wasSingleQuoted, rawSb.ToString());
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

        if (Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows && c == '_')
        {
            _pos++;
            return "$_";
        }

        if (c == '?')
        {
            _pos++;
            return _env.LastExitCode.ToString();
        }

        if (c == '$')
        {
            _pos++;
            return _env.ShellPid.ToString();
        }

        if (c == '!')
        {
            _pos++;
            return _env.BackgroundPid.ToString();
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

            return ExpandBracedContent(content);
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
            if (_env.Objects.ContainsKey(objName))
            {
                _pos++;
                var fieldBuf = new StringBuilder();
                while (_pos < _input.Length && (char.IsLetterOrDigit(_input[_pos]) || _input[_pos] == '_'))
                {
                    fieldBuf.Append(_input[_pos]);
                    _pos++;
                }
                return _env.GetObjectField(objName, fieldBuf.ToString()) ?? "";
            }

            if (Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows)
                return "$" + objName;
        }

        string name = nameBuf.ToString();
        if (string.IsNullOrEmpty(name))
            return "$";

        if (Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows)
        {
            if (_pos < _input.Length && _input[_pos] == ':' && IsPowerShellScopeName(name))
                return "$" + name;

            if (IsPowerShellAutomaticVariable(name) && _env.Get(name) == null)
                return "$" + name;
        }

        return _env.Get(name) ?? "";
    }

    private string ExpandBracedContent(string content)
    {
        if (Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows && IsPowerShellBracedVariable(content))
            return "${" + content + "}";

        if (content.StartsWith("#"))
        {
            string varName = content.Substring(1);
            if (varName.EndsWith("[@]") || varName.EndsWith("[*]"))
            {
                string arrName = varName.Substring(0, varName.Length - 3);
                int length = _env.GetArrayLength(arrName);
                if (length == 0 && _env.GetAssocArray(arrName) != null)
                    length = _env.GetAssocArray(arrName)!.Count;
                return length.ToString();
            }
            string? v = _env.Get(varName);
            return v != null ? v.Length.ToString() : "0";
        }

        int colonDash = content.IndexOf(":-", StringComparison.Ordinal);
        if (colonDash >= 0)
        {
            string name = content.Substring(0, colonDash);
            string def = content.Substring(colonDash + 2);
            string? val = _env.Get(name);
            return string.IsNullOrEmpty(val) ? _env.Expand(def) : val;
        }

        int colonEquals = content.IndexOf(":=", StringComparison.Ordinal);
        if (colonEquals >= 0)
        {
            string name = content.Substring(0, colonEquals);
            string def = content.Substring(colonEquals + 2);
            string? val = _env.Get(name);
            if (string.IsNullOrEmpty(val))
            {
                val = _env.Expand(def);
                _env.Set(name, val);
            }
            return val;
        }

        int colonPlus = content.IndexOf(":+", StringComparison.Ordinal);
        if (colonPlus >= 0)
        {
            string name = content.Substring(0, colonPlus);
            string alt = content.Substring(colonPlus + 2);
            string? val = _env.Get(name);
            return !string.IsNullOrEmpty(val) ? _env.Expand(alt) : "";
        }

        int colonQ = content.IndexOf(":?", StringComparison.Ordinal);
        if (colonQ >= 0)
        {
            string name = content.Substring(0, colonQ);
            string err = content.Substring(colonQ + 2);
            string? val = _env.Get(name);
            if (string.IsNullOrEmpty(val))
            {
                Console.Error.WriteLine($"aursh: {name}: {(string.IsNullOrEmpty(err) ? "parameter not set" : err)}");
                return "";
            }
            return val;
        }

        int dotIdx = content.IndexOf('.');
        if (dotIdx > 0)
        {
            string objName = content.Substring(0, dotIdx);
            string field = content.Substring(dotIdx + 1);
            return _env.GetObjectField(objName, field) ?? "";
        }

        // String Manipulation Expansions
        if (content.Contains("//"))
        {
            var parts = content.Split(new[] { "//" }, 2, StringSplitOptions.None);
            string name = parts[0];
            var subParts = parts[1].Split(new[] { '/' }, 2);
            string pattern = subParts[0];
            string replacement = subParts.Length > 1 ? subParts[1] : "";
            string? val = _env.Get(name) ?? "";
            return Regex.Replace(val, GlobToRegex(pattern, true), replacement);
        }
        else if (content.Contains("/"))
        {
            var parts = content.Split(new[] { '/' }, 3);
            string name = parts[0];
            string pattern = parts[1];
            string replacement = parts.Length > 2 ? parts[2] : "";
            string? val = _env.Get(name) ?? "";
            return new Regex(GlobToRegex(pattern, true)).Replace(val, replacement, 1);
        }

        if (content.Contains("##"))
        {
            var parts = content.Split(new[] { "##" }, 2, StringSplitOptions.None);
            string name = parts[0];
            string pattern = parts[1];
            string? val = _env.Get(name) ?? "";
            return Regex.Replace(val, "^" + GlobToRegex(pattern, true), "");
        }
        else if (content.Contains("#"))
        {
            var parts = content.Split(new[] { '#' }, 2);
            string name = parts[0];
            string pattern = parts[1];
            string? val = _env.Get(name) ?? "";
            return Regex.Replace(val, "^" + GlobToRegex(pattern, false), "");
        }

        if (content.Contains("%%"))
        {
            var parts = content.Split(new[] { "%%" }, 2, StringSplitOptions.None);
            string name = parts[0];
            string pattern = parts[1];
            string? val = _env.Get(name) ?? "";
            return Regex.Replace(val, GlobToRegex(pattern, true) + "$", "");
        }
        else if (content.Contains("%"))
        {
            var parts = content.Split(new[] { '%' }, 2);
            string name = parts[0];
            string pattern = parts[1];
            string? val = _env.Get(name) ?? "";
            return Regex.Replace(val, GlobToRegex(pattern, false) + "$", "", RegexOptions.RightToLeft);
        }

        if (content.EndsWith("^^"))
        {
            string name = content.Substring(0, content.Length - 2);
            return (_env.Get(name) ?? "").ToUpperInvariant();
        }
        else if (content.EndsWith("^"))
        {
            string name = content.Substring(0, content.Length - 1);
            string? val = _env.Get(name) ?? "";
            if (val.Length > 0) return char.ToUpperInvariant(val[0]) + val.Substring(1);
            return val;
        }

        if (content.EndsWith(",,"))
        {
            string name = content.Substring(0, content.Length - 2);
            return (_env.Get(name) ?? "").ToLowerInvariant();
        }
        else if (content.EndsWith(","))
        {
            string name = content.Substring(0, content.Length - 1);
            string? val = _env.Get(name) ?? "";
            if (val.Length > 0) return char.ToLowerInvariant(val[0]) + val.Substring(1);
            return val;
        }

        return _env.Get(content) ?? "";
    }

    private static string GlobToRegex(string glob, bool greedy)
    {
        string escaped = Regex.Escape(glob).Replace("\\?", ".");
        return greedy ? escaped.Replace("\\*", ".*") : escaped.Replace("\\*", ".*?");
    }

    private static bool IsPowerShellBracedVariable(string content)
    {
        if (content == "_")
            return true;

        int colonIdx = content.IndexOf(':');
        if (colonIdx > 0)
        {
            string scopeName = content.Substring(0, colonIdx);
            return IsPowerShellScopeName(scopeName);
        }

        return IsPowerShellAutomaticVariable(content);
    }

    private static bool IsPowerShellScopeName(string name)
    {
        return name.Equals("env", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("global", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("local", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("private", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("script", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("using", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("variable", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPowerShellAutomaticVariable(string name)
    {
        return name.Equals("PSItem", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("this", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("input", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("args", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("foreach", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("switch", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("false", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("null", StringComparison.OrdinalIgnoreCase);
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
