using System.Text;

namespace AurShell.Core;

public enum TokenType
{
    Word,
    Pipe,
    And,
    Or,
    Semicolon,
    Background,
    RedirectOut,
    RedirectAppend,
    RedirectIn,
    RedirectErr,
    RedirectErrAppend,
    RedirectErrToOut,
    Newline,
    EOF
}

public class Token
{
    public TokenType Type { get; }
    public string Value { get; }
    public bool WasQuoted { get; }

    public Token(TokenType type, string value, bool wasQuoted = false)
    {
        Type = type;
        Value = value;
        WasQuoted = wasQuoted;
    }

    public override string ToString() => $"[{Type}: {Value}]";
}

public class Lexer
{
    private readonly string _input;
    private readonly ShellEnvironment _env;
    private int _pos;

    public Lexer(string input, ShellEnvironment env)
    {
        _input = input;
        _env = env;
        _pos = 0;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (_pos < _input.Length)
        {
            SkipWhitespace();

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
                tokens.Add(new Token(TokenType.Newline, "\n"));
                _pos++;
                continue;
            }

            if (c == ';')
            {
                tokens.Add(new Token(TokenType.Semicolon, ";"));
                _pos++;
                continue;
            }

            if (c == '|')
            {
                _pos++;
                if (_pos < _input.Length && _input[_pos] == '|')
                {
                    tokens.Add(new Token(TokenType.Or, "||"));
                    _pos++;
                }
                else
                {
                    tokens.Add(new Token(TokenType.Pipe, "|"));
                }
                continue;
            }

            if (c == '&')
            {
                _pos++;
                if (_pos < _input.Length && _input[_pos] == '&')
                {
                    tokens.Add(new Token(TokenType.And, "&&"));
                    _pos++;
                }
                else
                {
                    tokens.Add(new Token(TokenType.Background, "&"));
                }
                continue;
            }

            if (c == '2' && _pos + 1 < _input.Length && _input[_pos + 1] == '>')
            {
                _pos += 2;
                if (_pos < _input.Length && _input[_pos] == '>')
                {
                    tokens.Add(new Token(TokenType.RedirectErrAppend, "2>>"));
                    _pos++;
                }
                else if (_pos + 1 < _input.Length && _input[_pos] == '&' && _input[_pos + 1] == '1')
                {
                    tokens.Add(new Token(TokenType.RedirectErrToOut, "2>&1"));
                    _pos += 2;
                }
                else
                {
                    tokens.Add(new Token(TokenType.RedirectErr, "2>"));
                }
                continue;
            }

            if (c == '>')
            {
                _pos++;
                if (_pos < _input.Length && _input[_pos] == '>')
                {
                    tokens.Add(new Token(TokenType.RedirectAppend, ">>"));
                    _pos++;
                }
                else
                {
                    tokens.Add(new Token(TokenType.RedirectOut, ">"));
                }
                continue;
            }

            if (c == '<')
            {
                tokens.Add(new Token(TokenType.RedirectIn, "<"));
                _pos++;
                continue;
            }

            tokens.Add(ReadWord());
        }

        tokens.Add(new Token(TokenType.EOF, ""));
        return tokens;
    }

    private void SkipWhitespace()
    {
        while (_pos < _input.Length && _input[_pos] != '\n' && char.IsWhiteSpace(_input[_pos]))
            _pos++;
    }

    private Token ReadWord()
    {
        var sb = new StringBuilder();
        bool wasQuoted = false;

        while (_pos < _input.Length)
        {
            char c = _input[_pos];

            if (char.IsWhiteSpace(c) && c != '\n' && !wasQuoted)
                break;

            if (c == '\n' || c == ';' || c == '|' || c == '<')
                break;

            if (c == '>' && !wasQuoted)
                break;

            if (c == '&' && !wasQuoted)
                break;

            if (c == '#' && sb.Length == 0)
                break;

            if (c == '\\' && _pos + 1 < _input.Length)
            {
                _pos++;
                char escaped = _input[_pos];
                sb.Append(MapEscapeChar(escaped));
                _pos++;
                continue;
            }

            if (c == '\'')
            {
                sb.Append(ReadSingleQuoted());
                wasQuoted = true;
                continue;
            }

            if (c == '"')
            {
                sb.Append(ReadDoubleQuoted());
                wasQuoted = true;
                continue;
            }

            if (c == '$')
            {
                sb.Append(ExpandInline());
                continue;
            }

            if (c == '~' && sb.Length == 0)
            {
                if (_pos + 1 >= _input.Length || _input[_pos + 1] == '/' || _input[_pos + 1] == '\\' || char.IsWhiteSpace(_input[_pos + 1]))
                {
                    sb.Append(Utils.Platform.HomeDirectory);
                    _pos++;
                    continue;
                }
            }

            if (c == '*' || c == '?')
            {
                sb.Append(c);
                _pos++;
                continue;
            }

            sb.Append(c);
            _pos++;
        }

        return new Token(TokenType.Word, sb.ToString(), wasQuoted);
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
            return _env.LastExitCode.ToString();
        }

        if (c == '$')
        {
            _pos++;
            return _env.ShellPid.ToString();
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
        }

        string name = nameBuf.ToString();
        if (string.IsNullOrEmpty(name))
            return "$";

        return _env.Get(name) ?? "";
    }

    private string ExpandBracedContent(string content)
    {
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

        return _env.Get(content) ?? "";
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
